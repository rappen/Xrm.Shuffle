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
        /// The parameters that bypass as much of what was asked for as a server of this version
        /// can, and a warning for each part it cannot.
        /// </summary>
        /// <remarks>
        /// Dataverse (9.2) takes BypassBusinessLogicExecution and
        /// SuppressCallbackRegistrationExpanderJob. Older on-premises servers only know the
        /// legacy BypassCustomPluginExecution, which covers sync logic alone, and servers before
        /// 9.0 cannot bypass anything. A definition is often shared between online and
        /// on-premises environments, so what a server cannot bypass runs as usual, with a
        /// warning, rather than stopping the import.
        /// </remarks>
        public static Dictionary<string, object> ParametersFor(bool sync, bool async, bool flows, Version server, out List<string> warnings)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            warnings = new List<string>();
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
            var notBypassed = new List<string>();
            if (sync)
            {
                if (server >= new Version(9, 0))
                {
                    result["BypassCustomPluginExecution"] = true;
                }
                else
                {
                    notBypassed.Add("sync plugins and workflows");
                }
            }
            if (async)
            {
                notBypassed.Add("async plugins and workflows");
            }
            if (flows)
            {
                notBypassed.Add("Power Automate flows");
            }
            if (notBypassed.Count > 0)
            {
                warnings.Add($"This server ({server}) cannot bypass {string.Join(" or ", notBypassed)}. They run as usual during this import.");
            }
            return result;
        }

        /// <summary>What the parameters bypass, for the log, e.g. "sync, async, flows".</summary>
        public static string Describe(IDictionary<string, object> parameters)
        {
            var logic = parameters.TryGetValue("BypassBusinessLogicExecution", out var value) ? value as string ?? "" : "";
            var sync = logic.Contains("CustomSync") || parameters.ContainsKey("BypassCustomPluginExecution");
            var async = logic.Contains("CustomAsync");
            var flows = parameters.ContainsKey("SuppressCallbackRegistrationExpanderJob");
            return string.Join(", ", new[] { sync ? "sync" : null, async ? "async" : null, flows ? "flows" : null }.Where(s => s != null));
        }

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
