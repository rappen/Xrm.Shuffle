namespace Cinteros.Crm.Utils.Shuffle.Tests.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using FakeXrmEasy;
    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.Messages;
    using Microsoft.Xrm.Sdk.Query;

    /// <summary>
    /// The fake org's own <see cref="IOrganizationService"/>, wrapped so that a fixture can see
    /// which requests the import actually sent, and so that the three bulk messages work at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two jobs, both of which have to happen at this layer. The first is recording: FakeXrmEasy
    /// answers a request and forgets it, but the whole point of these fixtures is which rung of
    /// the fallback chain the product landed on, so every call is kept in order.
    /// </para>
    /// <para>
    /// The second is CreateMultiple, UpdateMultiple and UpsertMultiple. FakeXrmEasy 1.x ships no
    /// executor for any of them - they postdate it - and the product sends them untyped, as
    /// <c>new OrganizationRequest("CreateMultiple")</c> with a <c>Targets</c> collection. So they
    /// are served here by fanning the targets out over the single-record messages the fake does
    /// understand, and answering in the shape the product reads back: <c>Ids</c> for
    /// CreateMultiple, nothing for UpdateMultiple, <c>Results</c> for UpsertMultiple.
    /// </para>
    /// <para>
    /// Fanning out is a deliberate simplification, and it is worth being clear about what it
    /// costs. A real CreateMultiple is one transaction: one bad row rolls the batch back. Here
    /// the rows before the bad one are already written. Tests about that boundary belong in
    /// Layer 1, where the service is scripted and can fault the batch as a unit; see
    /// <see cref="ScriptedOrganizationService"/>. What this class is for is routing - proving
    /// that the block reached CreateMultiple at all, with the targets it should have carried.
    /// </para>
    /// </remarks>
    public class RecordingOrganizationService : IOrganizationService
    {
        /// <summary>The messages this wrapper serves itself rather than passing to the fake.</summary>
        private static readonly string[] BulkMessages = { "CreateMultiple", "UpdateMultiple", "UpsertMultiple" };

        private readonly IOrganizationService inner;
        private readonly List<OrganizationRequest> requests = new List<OrganizationRequest>();
        private readonly List<Entity> created = new List<Entity>();
        private readonly List<Entity> updated = new List<Entity>();
        private readonly List<Tuple<string, Guid>> deleted = new List<Tuple<string, Guid>>();
        private readonly List<QueryBase> queries = new List<QueryBase>();

        private readonly Dictionary<string, Queue<Exception>> throwOnce =
            new Dictionary<string, Queue<Exception>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Exception> throwAlways =
            new Dictionary<string, Exception>(StringComparer.Ordinal);

        public RecordingOrganizationService(XrmFakedContext faked)
        {
            if (faked == null)
            {
                throw new ArgumentNullException("faked");
            }
            inner = faked.GetOrganizationService();
        }

        /// <summary>Every request that reached <see cref="Execute"/>, in order.</summary>
        public IReadOnlyList<OrganizationRequest> Requests
        {
            get { return requests; }
        }

        /// <summary>Entities passed to the individual <see cref="Create"/> path.</summary>
        public IReadOnlyList<Entity> Created
        {
            get { return created; }
        }

        /// <summary>Entities passed to the individual <see cref="Update"/> path.</summary>
        public IReadOnlyList<Entity> Updated
        {
            get { return updated; }
        }

        /// <summary>Records passed to the individual <see cref="Delete"/> path.</summary>
        public IReadOnlyList<Tuple<string, Guid>> Deleted
        {
            get { return deleted; }
        }

        /// <summary>
        /// Every query, in order - capability probes and match retrievals alike. Queries do not
        /// go through Execute, so this is the only place they are visible.
        /// </summary>
        public IReadOnlyList<QueryBase> Queries
        {
            get { return queries; }
        }

        /// <summary>The request names seen, in order - the routing assertion most fixtures make.</summary>
        public IReadOnlyList<string> RequestNames
        {
            get { return requests.Select(r => r.RequestName).ToList(); }
        }

        /// <summary>How many requests named <paramref name="messageName"/> were executed.</summary>
        public int CountOf(string messageName)
        {
            return requests.Count(r => string.Equals(r.RequestName, messageName, StringComparison.Ordinal));
        }

        /// <summary>The requests named <paramref name="messageName"/>, in order.</summary>
        public IReadOnlyList<OrganizationRequest> RequestsNamed(string messageName)
        {
            return requests.Where(r => string.Equals(r.RequestName, messageName, StringComparison.Ordinal)).ToList();
        }

        /// <summary>The targets a bulk request carried, or an empty list if it carried none.</summary>
        public static IReadOnlyList<Entity> TargetsOf(OrganizationRequest request)
        {
            var targets = request.Parameters.Contains("Targets")
                ? request.Parameters["Targets"] as EntityCollection
                : null;
            return targets == null ? new List<Entity>() : targets.Entities.ToList();
        }

        /// <summary>
        /// Throws <paramref name="exception"/> the next time <paramref name="messageName"/> is
        /// executed, then lets the message through. This is how the fallback chain is reached:
        /// the bulk message fails once and the product is expected to drop a rung and succeed.
        /// </summary>
        public RecordingOrganizationService ThrowOnce(string messageName, Exception exception)
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

        /// <summary>Throws <paramref name="exception"/> every time <paramref name="messageName"/> is executed.</summary>
        public RecordingOrganizationService ThrowAlways(string messageName, Exception exception)
        {
            throwAlways[messageName] = exception;
            return this;
        }

        #region IOrganizationService

        public Guid Create(Entity entity)
        {
            created.Add(entity);
            Record("Create", "Target", entity);
            Fault("Create");
            return inner.Create(entity);
        }

        public void Update(Entity entity)
        {
            updated.Add(entity);
            Record("Update", "Target", entity);
            Fault("Update");
            inner.Update(entity);
        }

        public void Delete(string entityName, Guid id)
        {
            deleted.Add(Tuple.Create(entityName, id));
            Record("Delete", "Target", new EntityReference(entityName, id));
            Fault("Delete");
            inner.Delete(entityName, id);
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            Record("Retrieve", "Target", new EntityReference(entityName, id));
            Fault("Retrieve");
            return inner.Retrieve(entityName, id, columnSet);
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            queries.Add(query);
            return inner.RetrieveMultiple(query);
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            requests.Add(request);
            Fault(request.RequestName);

            return BulkMessages.Contains(request.RequestName, StringComparer.Ordinal)
                ? ExecuteBulk(request)
                : inner.Execute(request);
        }

        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            inner.Associate(entityName, entityId, relationship, relatedEntities);
        }

        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            inner.Disassociate(entityName, entityId, relationship, relatedEntities);
        }

        #endregion

        /// <summary>
        /// Serves one of the three bulk messages by fanning its targets out over the
        /// single-record messages the fake understands. See the remarks on this class for why
        /// that is not the same thing as a real bulk request.
        /// </summary>
        private OrganizationResponse ExecuteBulk(OrganizationRequest request)
        {
            var targets = TargetsOf(request);
            var response = new OrganizationResponse { ResponseName = request.RequestName };

            switch (request.RequestName)
            {
                case "CreateMultiple":
                    var ids = new Guid[targets.Count];
                    for (var i = 0; i < targets.Count; i++)
                    {
                        ids[i] = inner.Create(targets[i]);
                    }
                    response.Results["Ids"] = ids;
                    break;

                case "UpdateMultiple":
                    foreach (var target in targets)
                    {
                        inner.Update(target);
                    }
                    break;

                case "UpsertMultiple":
                    var results = new UpsertResponse[targets.Count];
                    for (var i = 0; i < targets.Count; i++)
                    {
                        results[i] = (UpsertResponse)inner.Execute(new UpsertRequest { Target = targets[i] });
                    }
                    response.Results["Results"] = results;
                    break;

                default:
                    throw new InvalidOperationException(request.RequestName + " is not a bulk message.");
            }

            return response;
        }

        /// <summary>Throws whatever the fixture armed for this message, if anything.</summary>
        private void Fault(string messageName)
        {
            Exception always;
            if (throwAlways.TryGetValue(messageName, out always))
            {
                throw always;
            }

            Queue<Exception> pending;
            if (throwOnce.TryGetValue(messageName, out pending) && pending.Count > 0)
            {
                throw pending.Dequeue();
            }
        }

        /// <summary>
        /// Logs an individual operation as a request, so RequestNames and CountOf see the
        /// individual rungs of the fallback chain the same way they see the batched ones.
        /// </summary>
        private void Record(string messageName, string parameterName, object target)
        {
            var request = new OrganizationRequest(messageName);
            request[parameterName] = target;
            requests.Add(request);
        }
    }
}
