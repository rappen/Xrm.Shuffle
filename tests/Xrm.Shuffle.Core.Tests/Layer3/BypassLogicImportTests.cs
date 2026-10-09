namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer3
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.Messages;
    using NUnit.Framework;

    /// <summary>
    /// A definition that asks to bypass custom logic sends every write of every data block with
    /// the bypass parameters - whichever path the block takes - and a definition that does not
    /// sends none.
    /// </summary>
    [TestFixture]
    public class BypassLogicImportTests : FakeOrgTestBase
    {
        private static readonly HashSet<string> Writes = new HashSet<string>
        {
            "Create", "Update", "Upsert", "Delete", "CreateMultiple", "UpdateMultiple", "UpsertMultiple",
            "SetState", "Assign", "Associate", "Disassociate"
        };

        /// <summary>
        /// Two blocks: accounts batched, contacts one at a time and matched on name - with
        /// UpdateIdentical, so the matched contact is updated rather than skipped.
        /// </summary>
        private static DefinitionXml.DataBlockBuilder Definition(params string[] bypass)
        {
            var definition = DefinitionXml.DataBlock("Accounts", "account").BatchSize(10).CreateWithId()
                .AndDataBlock("Contacts", "contact").ImportAttribute("UpdateIdentical", "true").MatchOn("name", false);
            foreach (var attribute in bypass)
            {
                definition = definition.DefinitionAttribute(attribute, "true");
            }
            return definition;
        }

        private static XmlDocument Data() =>
            DataXml.Block("Accounts")
                .Record("account", Id(1)).With("name", "Alpha")
                .AndRecord("account", Id(2)).With("name", "Beta")
                .AndBlock("Contacts")
                .Record("contact", Id(3)).With("name", "Pat")
                .AndRecord("contact", Id(4)).With("name", "New")
                .Build();

        /// <summary>Every write sent, with the requests inside an ExecuteMultiple in place of it.</summary>
        private List<OrganizationRequest> WritesSent() =>
            Service.Requests
                .SelectMany(r => r is ExecuteMultipleRequest multiple ? multiple.Requests.AsEnumerable() : new[] { r })
                .Where(r => Writes.Contains(r.RequestName))
                .ToList();

        private static object Parameter(OrganizationRequest request, string name) =>
            request.Parameters.TryGetValue(name, out var value) ? value : null;

        private void SeedOnline()
        {
            Online().WithMetadata("account").WithMetadata("contact").WithEntity(Seeded("contact", Id(103), "Pat"));
        }

        [Test]
        public void Every_write_in_every_data_block_carries_the_bypass_parameters()
        {
            SeedOnline();

            var result = Shuffler.QuickImport(Org.Container, Definition("BypassSyncLogic", "BypassAsyncLogic", "BypassFlows").Build(), Data(), null);

            Assert.That(result.Item5, Is.EqualTo(0), "failed records. " + DumpAll());
            var writes = WritesSent();
            Assert.That(writes.Select(w => w.RequestName), Does.Contain("CreateMultiple").And.Contain("Create").And.Contain("Update"), DumpAll());
            Assert.That(writes.Select(w => Parameter(w, "BypassBusinessLogicExecution")), Is.All.EqualTo("CustomSync,CustomAsync"), DumpAll());
            Assert.That(writes.Select(w => Parameter(w, "SuppressCallbackRegistrationExpanderJob")), Is.All.EqualTo(true), DumpAll());
            Assert.That(Org.Logger.Logged("Bypassing custom logic: sync, async, flows"), DumpAll());
        }

        [Test]
        public void A_definition_without_bypass_sends_no_bypass_parameters()
        {
            SeedOnline();

            var result = Shuffler.QuickImport(Org.Container, Definition().Build(), Data(), null);

            Assert.That(result.Item5, Is.EqualTo(0), DumpAll());
            Assert.That(WritesSent().Select(w => w.Parameters.Keys.Count(k => k.StartsWith("Bypass") || k.StartsWith("Suppress"))), Is.All.EqualTo(0));
            Assert.That(Service.RequestNames, Does.Not.Contain("RetrieveVersion"), "the version is only read when bypassing");
        }

        /// <summary>The second pass that applies deferred state and owner is part of the block.</summary>
        [Test]
        public void Deferred_state_changes_carry_the_parameter()
        {
            Online().WithMetadata("account");
            var definition = DefinitionXml.DataBlock("Accounts", "account").BatchSize(10).CreateWithId().DeferStateAndOwner()
                .DefinitionAttribute("BypassSyncLogic", "true");
            var data = DataXml.Block("Accounts")
                .Record("account", Id(1)).With("name", "Alpha").WithOptionSet("statecode", 1).WithOptionSet("statuscode", 2)
                .Build();

            Shuffler.QuickImport(Org.Container, definition.Build(), data, null);

            var writes = WritesSent();
            Assert.That(writes.Count, Is.GreaterThanOrEqualTo(2), "the create and the state change. " + DumpAll());
            Assert.That(writes.Select(w => Parameter(w, "BypassBusinessLogicExecution")), Is.All.EqualTo("CustomSync"), DumpAll());
        }

        /// <summary>On-premises 9.1 batches through ExecuteMultiple and only knows the legacy parameter.</summary>
        [Test]
        public void An_on_premises_server_gets_the_legacy_parameter_on_each_batched_request()
        {
            OnPrem().WithMetadata("account").WithMetadata("contact").WithEntity(Seeded("contact", Id(103), "Pat"));
            Service.WithVersion("9.1.0.0");

            var result = Shuffler.QuickImport(Org.Container, Definition("BypassSyncLogic").Build(), Data(), null);

            Assert.That(result.Item5, Is.EqualTo(0), DumpAll());
            Assert.That(Service.RequestNames, Does.Contain("ExecuteMultiple"), DumpAll());
            Assert.That(WritesSent().Select(w => Parameter(w, "BypassCustomPluginExecution")), Is.All.EqualTo(true), DumpAll());
        }

        /// <summary>
        /// The same definition on an on-premises server: sync logic is bypassed with the legacy
        /// parameter, the rest runs as usual, and the run says so - but it is not stopped.
        /// </summary>
        [Test]
        public void An_on_premises_server_warns_about_what_it_cannot_bypass_and_imports()
        {
            OnPrem().WithMetadata("account").WithMetadata("contact").WithEntity(Seeded("contact", Id(103), "Pat"));
            Service.WithVersion("9.1.0.0");
            var heard = new List<string>();

            var result = Shuffler.QuickImport(Org.Container, Definition("BypassSyncLogic", "BypassAsyncLogic", "BypassFlows").Build(), Data(), (sender, e) => heard.Add(e.Message));

            Assert.That(result.Item5, Is.EqualTo(0), DumpAll());
            Assert.That(result.Item1, Is.GreaterThan(0), DumpAll());
            Assert.That(WritesSent().Select(w => Parameter(w, "BypassCustomPluginExecution")), Is.All.EqualTo(true), DumpAll());
            Assert.That(WritesSent().Select(w => Parameter(w, "BypassBusinessLogicExecution")), Is.All.Null, DumpAll());
            Assert.That(heard, Has.Some.Contains("WARNING: This server (9.1.0.0) cannot bypass async plugins and workflows or Power Automate flows"));
            Assert.That(Org.Logger.Logged("Bypassing custom logic: sync (BypassCustomPluginExecution=True)"), DumpAll());
        }

        /// <summary>A server that can bypass nothing imports exactly as without the attributes.</summary>
        [Test]
        public void A_server_before_9_0_imports_without_bypass_and_warns()
        {
            OnPrem().WithMetadata("account").WithMetadata("contact").WithEntity(Seeded("contact", Id(103), "Pat"));
            Service.WithVersion("8.2.0.0");

            var result = Shuffler.QuickImport(Org.Container, Definition("BypassSyncLogic").Build(), Data(), null);

            Assert.That(result.Item5, Is.EqualTo(0), DumpAll());
            Assert.That(WritesSent().Select(w => w.Parameters.Keys.Count(k => k.StartsWith("Bypass") || k.StartsWith("Suppress"))), Is.All.EqualTo(0));
            Assert.That(Org.Logger.Logged("WARNING: This server (8.2.0.0) cannot bypass sync plugins and workflows"), DumpAll());
        }

        [Test]
        public void The_definition_attributes_are_read()
        {
            var definition = Definition("BypassSyncLogic", "BypassFlows").Deserialize();

            Assert.That(definition.BypassSyncLogic, Is.True);
            Assert.That(definition.BypassAsyncLogic, Is.False);
            Assert.That(definition.BypassFlows, Is.True);
        }
    }
}
