namespace Cinteros.Crm.Utils.Shuffle.Tests.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using FakeXrmEasy;
    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.Messages;
    using Microsoft.Xrm.Sdk.Metadata;
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
        private readonly XrmFakedContext faked;
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
            this.faked = faked;
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

        private readonly Dictionary<string, Dictionary<string, AttributeTypeCode>> attributeTypes =
            new Dictionary<string, Dictionary<string, AttributeTypeCode>>(StringComparer.Ordinal);

        /// <summary>
        /// Declares a column's type for RetrieveMetadataChangesRequest, which the fake org does not
        /// implement. The export asks for it to build a Relation filter.
        /// </summary>
        public RecordingOrganizationService WithAttributeType(string entityLogicalName, string attribute, AttributeTypeCode type)
        {
            if (!attributeTypes.TryGetValue(entityLogicalName, out var types))
            {
                types = new Dictionary<string, AttributeTypeCode>(StringComparer.Ordinal);
                attributeTypes[entityLogicalName] = types;
            }
            types[attribute] = type;
            return this;
        }

        private OrganizationResponse AnswerAttributeTypes(RetrieveMetadataChangesRequest request)
        {
            var entityName = request.Query.Criteria.Conditions
                .Where(c => c.PropertyName == "LogicalName")
                .Select(c => c.Value as string)
                .FirstOrDefault();
            var collection = new EntityMetadataCollection();
            if (entityName != null && attributeTypes.TryGetValue(entityName, out var types))
            {
                var entity = new EntityMetadata { LogicalName = entityName };
                var attributes = types.Select(t =>
                {
                    var attribute = new StringAttributeMetadata { LogicalName = t.Key };
                    SetNonPublic(attribute, "AttributeType", (AttributeTypeCode?)t.Value);
                    return (AttributeMetadata)attribute;
                }).ToArray();
                SetNonPublic(entity, "Attributes", attributes);
                collection.Add(entity);
            }
            var response = new RetrieveMetadataChangesResponse();
            response.Results["EntityMetadata"] = collection;
            response.Results["ServerVersionStamp"] = "1";
            response.Results["DeletedMetadata"] = null;
            return response;
        }

        private static void SetNonPublic(object target, string property, object value) =>
            target.GetType().GetProperty(property).GetSetMethod(true).Invoke(target, new[] { value });

        /// <summary>The most records Dataverse returns from one RetrieveMultiple.</summary>
        public const int MaxPageSize = 5000;

        /// <summary>
        /// Pages a query the way Dataverse does. Without this the fake returns every row at once,
        /// and a caller that never follows the paging cookie looks correct. FetchXML is translated
        /// to a QueryExpression and paged by its page and count attributes.
        /// </summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            queries.Add(query);
            Fault("RetrieveMultiple");
            if (query is FetchExpression fetch)
            {
                return RetrievePagedFetch(fetch);
            }
            var expression = query as QueryExpression;
            if (expression == null || expression.TopCount.HasValue)
            {
                return inner.RetrieveMultiple(query);
            }
            return RetrievePaged(expression, expression.PageInfo);
        }

        private EntityCollection RetrievePagedFetch(FetchExpression fetch)
        {
            var xml = new System.Xml.XmlDocument();
            xml.LoadXml(fetch.Query);
            var xFetch = xml.DocumentElement;
            if (xFetch.HasAttribute("top") || xFetch.GetAttribute("aggregate") == "true")
            {
                return inner.RetrieveMultiple(fetch);
            }
            int.TryParse(xFetch.GetAttribute("page"), out var page);
            int.TryParse(xFetch.GetAttribute("count"), out var count);
            foreach (var paging in new[] { "page", "count", "paging-cookie" })
            {
                xFetch.RemoveAttribute(paging);
            }
            var expression = XrmFakedContext.TranslateFetchXmlToQueryExpression(faked, xml.OuterXml);
            return RetrievePaged(expression, new PagingInfo { PageNumber = page, Count = count });
        }

        private EntityCollection RetrievePaged(QueryExpression expression, PagingInfo pageInfo)
        {
            var size = pageInfo != null && pageInfo.Count > 0 ? Math.Min(pageInfo.Count, MaxPageSize) : MaxPageSize;
            var number = pageInfo != null && pageInfo.PageNumber > 0 ? pageInfo.PageNumber : 1;
            var originalPageInfo = expression.PageInfo;
            var all = new List<Entity>();
            string entityName = null;
            try
            {
                // The fake caps an unpaged query at 5000 itself, so read the whole result from
                // it page by page and hand out the requested page from that.
                for (var fakePage = 1; ; fakePage++)
                {
                    expression.PageInfo = new PagingInfo { Count = MaxPageSize, PageNumber = fakePage };
                    var chunk = inner.RetrieveMultiple(expression);
                    entityName = chunk.EntityName;
                    all.AddRange(chunk.Entities);
                    if (!chunk.MoreRecords || chunk.Entities.Count == 0)
                    {
                        break;
                    }
                }
            }
            finally
            {
                expression.PageInfo = originalPageInfo;
            }

            var page = new EntityCollection(all.Skip((number - 1) * size).Take(size).ToList())
            {
                EntityName = entityName,
                MoreRecords = all.Count > number * size,
            };
            page.PagingCookie = page.MoreRecords ? "<cookie page=\"" + number + "\" />" : null;
            return page;
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (FetchXmlConversion.IsConversion(request))
            {
                return FetchXmlConversion.Answer(request);
            }
            if (request is RetrieveMetadataChangesRequest metadataRequest)
            {
                requests.Add(request);
                return AnswerAttributeTypes(metadataRequest);
            }

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
