namespace Cinteros.Crm.Utils.Shuffle.Tests.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.Query;

    /// <summary>
    /// An <see cref="IOrganizationService"/> whose every answer a fixture writes itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FakeXrmEasy 1.x answers plausibly, which is the wrong tool for the batch paths: the
    /// interesting cases are implausible. A batch response with fewer items than requests, an
    /// out-of-order RequestIndex, a bulk message that faults with "not implemented" on the
    /// second call but not the first - none of those can be asked of a fake that models a
    /// real platform. So this service scripts responses instead of simulating an org, and
    /// records every request so a fixture can assert which path the product actually took.
    /// </para>
    /// <para>
    /// Anything a fixture has not scripted throws. Silence would let a test pass while the
    /// product quietly took a different route, which is the one failure mode these tests
    /// exist to catch.
    /// </para>
    /// </remarks>
    public class ScriptedOrganizationService : IOrganizationService
    {
        private readonly List<OrganizationRequest> requests = new List<OrganizationRequest>();
        private readonly List<Entity> created = new List<Entity>();
        private readonly List<Entity> updated = new List<Entity>();
        private readonly List<Tuple<string, Guid>> deleted = new List<Tuple<string, Guid>>();

        private readonly Dictionary<string, Func<OrganizationRequest, OrganizationResponse>> byMessage =
            new Dictionary<string, Func<OrganizationRequest, OrganizationResponse>>(StringComparer.Ordinal);

        private readonly Dictionary<string, Queue<Exception>> throwOnce =
            new Dictionary<string, Queue<Exception>>(StringComparer.Ordinal);

        private readonly Dictionary<string, HashSet<string>> supportedBulkMessages =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        private readonly List<Tuple<string, string>> probes = new List<Tuple<string, string>>();

        private Exception probeFailure;
        private Func<Entity, Guid> onCreate;
        private Action<Entity> onUpdate;
        private Func<QueryBase, EntityCollection> onRetrieveMultiple;

        /// <summary>Every request that reached <see cref="Execute"/>, in order.</summary>
        public IReadOnlyList<OrganizationRequest> Requests => requests;

        /// <summary>Entities passed to the individual <see cref="Create"/> path.</summary>
        public IReadOnlyList<Entity> Created => created;

        /// <summary>Entities passed to the individual <see cref="Update"/> path.</summary>
        public IReadOnlyList<Entity> Updated => updated;

        /// <summary>Records passed to the individual <see cref="Delete"/> path.</summary>
        public IReadOnlyList<Tuple<string, Guid>> Deleted => deleted;

        /// <summary>
        /// Every sdkmessagefilter capability probe, as (entity, message). RetrieveMultiple does
        /// not go through Requests, and the probe is cached per entity and message, so this is
        /// what a fixture counts to show the cache is doing its job.
        /// </summary>
        public IReadOnlyList<Tuple<string, string>> Probes => probes;

        /// <summary>The request names seen, in order - the routing assertion most fixtures make.</summary>
        public IReadOnlyList<string> RequestNames => requests.Select(r => r.RequestName).ToList();

        /// <summary>How many requests named <paramref name="messageName"/> were executed.</summary>
        public int CountOf(string messageName) =>
            requests.Count(r => string.Equals(r.RequestName, messageName, StringComparison.Ordinal));

        /// <summary>The requests named <paramref name="messageName"/>, in order.</summary>
        public IReadOnlyList<OrganizationRequest> RequestsNamed(string messageName) =>
            requests.Where(r => string.Equals(r.RequestName, messageName, StringComparison.Ordinal)).ToList();

        #region scripting

        /// <summary>
        /// Answers <paramref name="messageName"/> with <paramref name="handler"/>.
        /// </summary>
        /// <remarks>
        /// Keyed on RequestName rather than on a request type, because the bulk messages are
        /// built as untyped OrganizationRequest("CreateMultiple") - there is no
        /// CreateMultipleRequest in the 9.0 SDK assemblies this repo builds against.
        /// </remarks>
        public ScriptedOrganizationService OnMessage(string messageName, Func<OrganizationRequest, OrganizationResponse> handler)
        {
            byMessage[messageName] = handler;
            return this;
        }

        /// <summary>Answers <paramref name="messageName"/> with a fixed response.</summary>
        public ScriptedOrganizationService OnMessage(string messageName, OrganizationResponse response)
        {
            return OnMessage(messageName, _ => response);
        }

        /// <summary>
        /// Answers <paramref name="messageName"/> with the queued responses, one per call, in order.
        /// </summary>
        public ScriptedOrganizationService OnMessageSequence(string messageName, params OrganizationResponse[] responses)
        {
            var queue = new Queue<OrganizationResponse>(responses);
            var scripted = responses.Length;
            return OnMessage(messageName, _ =>
            {
                if (queue.Count == 0)
                {
                    throw new InvalidOperationException(string.Format(
                        "{0} was executed more times than the fixture scripted ({1}).", messageName, scripted));
                }
                return queue.Dequeue();
            });
        }

        /// <summary>
        /// Throws <paramref name="exception"/> the next time <paramref name="messageName"/> is
        /// executed, then falls through to whatever else is scripted.
        /// </summary>
        /// <remarks>
        /// This is how the fallback chain gets exercised: the bulk message throws
        /// "not implemented" once, and the product is expected to fall back and succeed on
        /// the next rung rather than give up.
        /// </remarks>
        public ScriptedOrganizationService ThrowOnce(string messageName, Exception exception)
        {
            Queue<Exception> queue;
            if (!throwOnce.TryGetValue(messageName, out queue))
            {
                queue = new Queue<Exception>();
                throwOnce[messageName] = queue;
            }
            queue.Enqueue(exception);
            return this;
        }

        /// <summary>
        /// Declares that <paramref name="entityLogicalName"/> supports the named bulk
        /// messages, so the capability probe answers yes for exactly those.
        /// </summary>
        public ScriptedOrganizationService SupportsBulkMessage(string entityLogicalName, params string[] messageNames)
        {
            HashSet<string> set;
            if (!supportedBulkMessages.TryGetValue(entityLogicalName, out set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                supportedBulkMessages[entityLogicalName] = set;
            }
            foreach (var name in messageNames)
            {
                set.Add(name);
            }
            return this;
        }

        /// <summary>
        /// Makes the capability probe throw. The product catches that and caches a no, which
        /// is the difference between an org that cannot answer and one that answers no.
        /// </summary>
        public ScriptedOrganizationService FailTheCapabilityProbe(Exception exception)
        {
            probeFailure = exception;
            return this;
        }

        /// <summary>Assigns ids to individual creates. Defaults to a fresh guid each time.</summary>
        public ScriptedOrganizationService OnCreate(Func<Entity, Guid> handler)
        {
            onCreate = handler;
            return this;
        }

        /// <summary>Observes individual updates. Defaults to accepting them.</summary>
        public ScriptedOrganizationService OnUpdate(Action<Entity> handler)
        {
            onUpdate = handler;
            return this;
        }

        /// <summary>
        /// Answers any RetrieveMultiple the capability probe did not claim - match queries,
        /// mostly. Returning an empty collection is the common case and has to be explicit.
        /// </summary>
        public ScriptedOrganizationService OnRetrieveMultiple(Func<QueryBase, EntityCollection> handler)
        {
            onRetrieveMultiple = handler;
            return this;
        }

        #endregion

        #region IOrganizationService

        public Guid Create(Entity entity)
        {
            created.Add(entity);
            Record("Create", "Target", entity);
            return onCreate != null ? onCreate(entity) : Guid.NewGuid();
        }

        public void Update(Entity entity)
        {
            updated.Add(entity);
            Record("Update", "Target", entity);
            if (onUpdate != null)
            {
                onUpdate(entity);
            }
        }

        public void Delete(string entityName, Guid id)
        {
            deleted.Add(Tuple.Create(entityName, id));
            Record("Delete", "Target", new EntityReference(entityName, id));
        }

        /// <summary>
        /// Logs an individual operation as a request, so that RequestNames and CountOf see the
        /// individual rungs of the fallback chain the same way they see the batched ones.
        /// </summary>
        private void Record(string messageName, string parameterName, object target)
        {
            var request = new OrganizationRequest(messageName);
            request[parameterName] = target;
            requests.Add(request);
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            requests.Add(request);

            Queue<Exception> pending;
            if (throwOnce.TryGetValue(request.RequestName, out pending) && pending.Count > 0)
            {
                throw pending.Dequeue();
            }

            Func<OrganizationRequest, OrganizationResponse> handler;
            if (byMessage.TryGetValue(request.RequestName, out handler))
            {
                return handler(request);
            }

            throw new InvalidOperationException(string.Format(
                "The fixture did not script {0}. Requests so far: {1}.",
                request.RequestName,
                string.Join(", ", RequestNames)));
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var probe = AsBulkCapabilityProbe(query);
            if (probe != null)
            {
                probes.Add(probe);
                if (probeFailure != null)
                {
                    throw probeFailure;
                }

                HashSet<string> supported;
                var yes = supportedBulkMessages.TryGetValue(probe.Item1, out supported)
                          && supported.Contains(probe.Item2);
                var result = new EntityCollection { EntityName = "sdkmessagefilter" };
                if (yes)
                {
                    result.Entities.Add(new Entity("sdkmessagefilter", Guid.NewGuid()));
                }
                return result;
            }

            if (onRetrieveMultiple != null)
            {
                return onRetrieveMultiple(query);
            }

            throw new InvalidOperationException(
                "The fixture did not script RetrieveMultiple, and the query is not a bulk capability probe.");
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            throw new NotSupportedException(
                "Retrieve is not scripted. The import paths under test use RetrieveMultiple; " +
                "reaching this means the product took an unexpected route.");
        }

        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            throw new NotSupportedException("Associate is not scripted.");
        }

        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            throw new NotSupportedException("Disassociate is not scripted.");
        }

        #endregion

        /// <summary>
        /// Recognises the sdkmessagefilter query IsBulkMessageSupported builds, returning
        /// entity logical name and message name when it matches, and null otherwise.
        /// </summary>
        /// <remarks>
        /// Note which value the entity condition carries: primaryobjecttypecode really holds a
        /// numeric entity type code, but the product queries it with the logical name string.
        /// This helper matches the product, not the platform, deliberately - see the marker
        /// test Capability_query_uses_the_logical_name_not_the_entity_type_code.
        /// </remarks>
        private static Tuple<string, string> AsBulkCapabilityProbe(QueryBase query)
        {
            var expression = query as QueryExpression;
            if (expression == null || expression.EntityName != "sdkmessagefilter")
            {
                return null;
            }

            var entityCondition = expression.Criteria.Conditions
                .FirstOrDefault(c => c.AttributeName == "primaryobjecttypecode");
            var link = expression.LinkEntities.FirstOrDefault(l => l.LinkToEntityName == "sdkmessage");
            if (entityCondition == null || link == null)
            {
                return null;
            }

            var messageCondition = link.LinkCriteria.Conditions.FirstOrDefault(c => c.AttributeName == "name");
            if (messageCondition == null)
            {
                return null;
            }

            return Tuple.Create(
                Convert.ToString(entityCondition.Values.FirstOrDefault()),
                Convert.ToString(messageCondition.Values.FirstOrDefault()));
        }
    }
}
