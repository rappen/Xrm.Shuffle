namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer1
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Crm.Sdk.Messages;
    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.Messages;
    using Microsoft.Xrm.Sdk.Query;
    using NUnit.Framework;

    /// <summary>
    /// BypassLogicService puts the bypass parameters on every write and on nothing else, and
    /// picks the parameters the server understands.
    /// </summary>
    [TestFixture]
    public class BypassLogicServiceTests
    {
        private static readonly Version Dataverse = new Version(9, 2, 0, 0);

        /// <summary>Answers every call and keeps the requests; direct calls are kept under their own name.</summary>
        private class Recorder : IOrganizationService
        {
            public readonly List<OrganizationRequest> Requests = new List<OrganizationRequest>();

            public OrganizationResponse Execute(OrganizationRequest request)
            {
                Requests.Add(request);
                var response = new OrganizationResponse();
                if (request is CreateRequest)
                {
                    response = new CreateResponse();
                    response.Results["id"] = Guid.NewGuid();
                }
                return response;
            }

            public Guid Create(Entity entity)
            {
                Requests.Add(new OrganizationRequest("direct Create"));
                return Guid.NewGuid();
            }

            public void Update(Entity entity) => Requests.Add(new OrganizationRequest("direct Update"));

            public void Delete(string entityName, Guid id) => Requests.Add(new OrganizationRequest("direct Delete"));

            public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
                Requests.Add(new OrganizationRequest("direct Associate"));

            public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
                Requests.Add(new OrganizationRequest("direct Disassociate"));

            public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
            {
                Requests.Add(new OrganizationRequest("Retrieve"));
                return new Entity(entityName, id);
            }

            public EntityCollection RetrieveMultiple(QueryBase query)
            {
                Requests.Add(new OrganizationRequest("RetrieveMultiple"));
                return new EntityCollection();
            }
        }

        private static Dictionary<string, object> Parameters(bool sync, bool async, bool flows, Version server) =>
            BypassLogicService.ParametersFor(sync, async, flows, server, out _);

        private static BypassLogicService Bypassing(Recorder inner, bool sync = true, bool async = false, bool flows = false) =>
            new BypassLogicService(inner, Parameters(sync, async, flows, Dataverse));

        private static object Parameter(OrganizationRequest request, string name) =>
            request.Parameters.TryGetValue(name, out var value) ? value : null;

        [Test]
        public void Direct_writes_are_sent_as_requests_that_carry_the_parameter()
        {
            var inner = new Recorder();
            var service = Bypassing(inner);
            var account = new Entity("account", Guid.NewGuid());

            service.Create(account);
            service.Update(account);
            service.Delete("account", account.Id);
            service.Associate("account", account.Id, new Relationship("account_contacts"), new EntityReferenceCollection());
            service.Disassociate("account", account.Id, new Relationship("account_contacts"), new EntityReferenceCollection());

            Assert.That(inner.Requests.Select(r => r.RequestName), Is.EqualTo(new[] { "Create", "Update", "Delete", "Associate", "Disassociate" }));
            Assert.That(inner.Requests.Select(r => Parameter(r, "BypassBusinessLogicExecution")), Is.All.EqualTo("CustomSync"));
        }

        [TestCase("CreateMultiple")]
        [TestCase("UpdateMultiple")]
        [TestCase("UpsertMultiple")]
        public void Bulk_messages_carry_the_parameter(string message)
        {
            var inner = new Recorder();

            Bypassing(inner).Execute(new OrganizationRequest(message));

            Assert.That(Parameter(inner.Requests.Single(), "BypassBusinessLogicExecution"), Is.EqualTo("CustomSync"));
        }

        [Test]
        public void State_and_owner_changes_carry_the_parameter()
        {
            var inner = new Recorder();
            var service = Bypassing(inner);
            var target = new EntityReference("account", Guid.NewGuid());

            service.Execute(new SetStateRequest { EntityMoniker = target, State = new OptionSetValue(1), Status = new OptionSetValue(2) });
            service.Execute(new AssignRequest { Target = target, Assignee = new EntityReference("systemuser", Guid.NewGuid()) });

            Assert.That(inner.Requests.Select(r => Parameter(r, "BypassBusinessLogicExecution")), Is.All.EqualTo("CustomSync"));
        }

        /// <summary>Dataverse reads the parameters on each request inside an ExecuteMultiple.</summary>
        [Test]
        public void Every_request_inside_an_ExecuteMultiple_carries_the_parameter()
        {
            var multiple = new ExecuteMultipleRequest { Settings = new ExecuteMultipleSettings(), Requests = new OrganizationRequestCollection() };
            multiple.Requests.Add(new CreateRequest { Target = new Entity("account") });
            multiple.Requests.Add(new UpdateRequest { Target = new Entity("account", Guid.NewGuid()) });

            Bypassing(new Recorder()).Execute(multiple);

            Assert.That(multiple.Requests.Select(r => Parameter(r, "BypassBusinessLogicExecution")), Is.All.EqualTo("CustomSync"));
        }

        [Test]
        public void Reads_carry_no_parameters()
        {
            var inner = new Recorder();
            var service = Bypassing(inner, true, true, true);

            service.Retrieve("account", Guid.NewGuid(), new ColumnSet(true));
            service.RetrieveMultiple(new QueryExpression("account"));
            service.Execute(new RetrieveVersionRequest());

            Assert.That(inner.Requests.Select(r => r.Parameters.Count), Is.All.EqualTo(0));
        }

        [TestCase(true, false, false, "CustomSync", null)]
        [TestCase(false, true, false, "CustomAsync", null)]
        [TestCase(true, true, false, "CustomSync,CustomAsync", null)]
        [TestCase(false, false, true, null, true)]
        [TestCase(true, true, true, "CustomSync,CustomAsync", true)]
        public void Dataverse_gets_the_current_parameters(bool sync, bool async, bool flows, string logic, object suppressFlows)
        {
            var parameters = BypassLogicService.ParametersFor(sync, async, flows, Dataverse, out var warnings);

            Assert.That(warnings, Is.Empty);

            Assert.That(parameters.TryGetValue("BypassBusinessLogicExecution", out var l) ? l : null, Is.EqualTo(logic));
            Assert.That(parameters.TryGetValue("SuppressCallbackRegistrationExpanderJob", out var f) ? f : null, Is.EqualTo(suppressFlows));
            Assert.That(parameters.ContainsKey("BypassCustomPluginExecution"), Is.False);
        }

        [Test]
        public void Nothing_asked_gives_no_parameters_on_any_server()
        {
            Assert.That(BypassLogicService.ParametersFor(false, false, false, new Version(8, 2), out var warnings), Is.Empty);
            Assert.That(warnings, Is.Empty);
        }

        /// <summary>On-premises 9.0 and 9.1 only know the legacy parameter, which covers sync logic.</summary>
        [Test]
        public void An_older_server_bypasses_sync_logic_with_the_legacy_parameter()
        {
            var parameters = BypassLogicService.ParametersFor(true, false, false, new Version(9, 1, 0, 0), out var warnings);

            Assert.That(parameters, Is.EquivalentTo(new Dictionary<string, object> { ["BypassCustomPluginExecution"] = true }));
            Assert.That(warnings, Is.Empty);
        }

        /// <summary>
        /// A definition is often shared between online and on-premises: what the server cannot
        /// bypass runs as usual, with a warning, and the rest is still bypassed.
        /// </summary>
        [Test]
        public void An_older_server_bypasses_what_it_can_and_warns_about_the_rest()
        {
            var parameters = BypassLogicService.ParametersFor(true, true, true, new Version(9, 1, 0, 0), out var warnings);

            Assert.That(parameters, Is.EquivalentTo(new Dictionary<string, object> { ["BypassCustomPluginExecution"] = true }));
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("9.1.0.0").And.Contain("cannot bypass async plugins and workflows or Power Automate flows").And.Contain("run as usual"));
        }

        [Test]
        public void A_server_before_9_0_bypasses_nothing_and_warns()
        {
            var parameters = BypassLogicService.ParametersFor(true, false, false, new Version(8, 2, 0, 0), out var warnings);

            Assert.That(parameters, Is.Empty);
            Assert.That(warnings.Single(), Does.Contain("cannot bypass sync plugins and workflows"));
        }

        [Test]
        public void The_log_describes_what_is_actually_bypassed()
        {
            Assert.That(BypassLogicService.Describe(Parameters(true, true, true, Dataverse)), Is.EqualTo("sync, async, flows"));
            Assert.That(BypassLogicService.Describe(Parameters(true, true, true, new Version(9, 1))), Is.EqualTo("sync"));
            Assert.That(BypassLogicService.Describe(Parameters(false, false, true, Dataverse)), Is.EqualTo("flows"));
        }
    }
}
