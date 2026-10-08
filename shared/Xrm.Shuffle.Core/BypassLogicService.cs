namespace Cinteros.Crm.Utils.Shuffle
{
    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.Messages;
    using Microsoft.Xrm.Sdk.Query;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Sends every write with the optional parameters that tell Dataverse to skip custom logic:
    /// sync and async plugins and workflows, and Power Automate flows.
    /// </summary>
    /// <remarks>
    /// The import writes through typed requests, bulk messages, ExecuteMultiple and the
    /// Xrm.Utils.Core helpers (container.Create, SetState, Associate...), which call the service
    /// directly and cannot carry parameters. Wrapping the service catches all of them: the
    /// direct calls are sent as their request, and an ExecuteMultiple gets the parameters on
    /// each inner request, which is where Dataverse reads them. Reads pass through untouched.
    /// </remarks>
    public sealed class BypassLogicService : IOrganizationService
    {
        private static readonly HashSet<string> WriteMessages = new HashSet<string>(StringComparer.Ordinal)
        {
            "Create", "Update", "Upsert", "Delete",
            "CreateMultiple", "UpdateMultiple", "UpsertMultiple",
            "SetState", "Assign", "Associate", "Disassociate"
        };

        private readonly IOrganizationService inner;
        private readonly IDictionary<string, object> parameters;

        public BypassLogicService(IOrganizationService inner, IDictionary<string, object> parameters)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        }

        /// <summary>
        /// The parameters that bypass what was asked for, on a server of this version.
        /// </summary>
        /// <exception cref="NotSupportedException">The server cannot bypass something that was asked for.</exception>
        /// <remarks>
        /// Dataverse (9.2) takes BypassBusinessLogicExecution and
        /// SuppressCallbackRegistrationExpanderJob. Older on-premises servers only know the
        /// legacy BypassCustomPluginExecution, which covers sync logic alone. Running logic the
        /// definition said to skip would be worse than not importing, so anything else fails.
        /// </remarks>
        public static Dictionary<string, object> ParametersFor(bool sync, bool async, bool flows, Version server)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            if (!sync && !async && !flows)
            {
                return result;
            }
            if (server >= new Version(9, 2))
            {
                var logic = new[] { sync ? "CustomSync" : null, async ? "CustomAsync" : null }.Where(s => s != null).ToArray();
                if (logic.Length > 0)
                {
                    result["BypassBusinessLogicExecution"] = string.Join(",", logic);
                }
                if (flows)
                {
                    result["SuppressCallbackRegistrationExpanderJob"] = true;
                }
                return result;
            }
            if (server >= new Version(9, 0) && !async && !flows)
            {
                result["BypassCustomPluginExecution"] = true;
                return result;
            }
            throw new NotSupportedException(server >= new Version(9, 0)
                ? $"This server ({server}) can only bypass sync plugins and workflows. Turn off BypassAsyncLogic and BypassFlows in the definition, or import with them running."
                : $"This server ({server}) cannot bypass custom logic. Turn off BypassSyncLogic, BypassAsyncLogic and BypassFlows in the definition.");
        }

        /// <summary>A short description for the log, e.g. "sync, async, flows".</summary>
        public static string Describe(bool sync, bool async, bool flows) =>
            string.Join(", ", new[] { sync ? "sync" : null, async ? "async" : null, flows ? "flows" : null }.Where(s => s != null));

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            AddParameters(request);
            return inner.Execute(request);
        }

        public Guid Create(Entity entity) =>
            ((CreateResponse)Execute(new CreateRequest { Target = entity })).id;

        public void Update(Entity entity) =>
            Execute(new UpdateRequest { Target = entity });

        public void Delete(string entityName, Guid id) =>
            Execute(new DeleteRequest { Target = new EntityReference(entityName, id) });

        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            Execute(new AssociateRequest { Target = new EntityReference(entityName, entityId), Relationship = relationship, RelatedEntities = relatedEntities });

        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            Execute(new DisassociateRequest { Target = new EntityReference(entityName, entityId), Relationship = relationship, RelatedEntities = relatedEntities });

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) =>
            inner.Retrieve(entityName, id, columnSet);

        public EntityCollection RetrieveMultiple(QueryBase query) =>
            inner.RetrieveMultiple(query);

        private void AddParameters(OrganizationRequest request)
        {
            if (request is ExecuteMultipleRequest multiple)
            {
                foreach (var each in multiple.Requests)
                {
                    AddParameters(each);
                }
                return;
            }
            if (!WriteMessages.Contains(request.RequestName))
            {
                return;
            }
            foreach (var parameter in parameters)
            {
                request.Parameters[parameter.Key] = parameter.Value;
            }
        }
    }
}
