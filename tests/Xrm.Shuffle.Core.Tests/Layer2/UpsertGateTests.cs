namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer2
{
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// The five conditions that together decide whether a block upserts instead of matching.
    /// </summary>
    /// <remarks>
    /// Upsert skips the pre-retrieval query entirely, which is a large behaviour change for a
    /// block that asked for none of it. The gate is one <c>&amp;&amp;</c> chain of five clauses -
    /// Save is CreateUpdate, CreateWithId is set, there is at least one match attribute, Delete
    /// is None, and UpdateIdentical is set - so each test here opens the gate and then breaks
    /// exactly one clause. Two of them are off by default, which is why the opt-in test has to
    /// set them explicitly.
    /// </remarks>
    [TestFixture]
    public class UpsertGateTests : FakeOrgTestBase
    {
        /// <summary>
        /// Every clause of the upsert gate met at once. <paramref name="createWithId"/> is a
        /// parameter rather than a later override because the builder appends attributes instead
        /// of replacing them, so setting one twice emits duplicate XML and the document will not
        /// load.
        /// </summary>
        private static DefinitionXml.DataBlockBuilder GateOpen(bool createWithId = true)
        {
            return DefinitionXml.DataBlock("Accounts", "account")
                .BatchSize(10)
                .CreateWithId(createWithId)
                .ImportAttribute("UpdateIdentical", "true")
                .MatchOn("name");
        }

        private static EntityCollection TwoAccounts()
        {
            var entities = new EntityCollection { EntityName = "account" };
            entities.Entities.Add(Record("account", Id(1), "Alpha"));
            entities.Entities.Add(Record("account", Id(2), "Beta"));
            return entities;
        }

        private void Run(DefinitionXml.DataBlockBuilder block)
        {
            Online().WithMetadata("account");
            NewShuffler().TestImportDataBlock(block.DeserializeBlock(), TwoAccounts());
        }

        /// <summary>
        /// How many times the target entity itself was queried. The capability probe queries
        /// <c>sdkmessagefilter</c>, so counting all queries would never reach zero.
        /// </summary>
        private int AccountQueries()
        {
            var count = 0;
            foreach (var query in Service.Queries)
            {
                var expression = query as Microsoft.Xrm.Sdk.Query.QueryExpression;
                if (expression != null && expression.EntityName == "account")
                {
                    count++;
                }
            }
            return count;
        }

        private void AssertGateClosed()
        {
            Assert.That(Service.CountOf("UpsertMultiple"), Is.EqualTo(0), DumpAll());
            Assert.That(
                Org.Logger.Logged("Upsert path enabled"),
                Is.False,
                DumpAll());
        }

        [Test]
        public void All_five_clauses_met_upserts_the_block()
        {
            Run(GateOpen());

            Assert.That(Service.CountOf("UpsertMultiple"), Is.EqualTo(1), DumpAll());
            Assert.That(
                Org.Logger.Logged(
                    "Upsert path enabled - records will be upserted without pre-retrieval queries"),
                DumpAll());
        }

        /// <summary>
        /// PreRetrieveAll is the switch that made the block batchable in the first place, so the
        /// product says out loud that it is ignoring it rather than leaving the reader to wonder
        /// why no query went out.
        /// </summary>
        [Test]
        public void An_upserting_block_says_it_is_skipping_PreRetrieveAll()
        {
            Run(GateOpen());

            Assert.That(
                Org.Logger.Logged(
                    "Note: PreRetrieveAll is not needed when using Upsert and will be skipped"),
                DumpAll());
            Assert.That(AccountQueries(), Is.EqualTo(0), DumpAll());
        }

        [Test]
        public void Save_other_than_CreateUpdate_closes_the_gate()
        {
            Run(GateOpen().ImportAttribute("Save", "CreateOnly"));

            AssertGateClosed();
        }

        [Test]
        public void Without_CreateWithId_the_gate_stays_closed()
        {
            Run(GateOpen(createWithId: false));

            AssertGateClosed();
        }

        [Test]
        public void Without_a_match_attribute_the_gate_stays_closed()
        {
            Run(DefinitionXml.DataBlock("Accounts", "account")
                .BatchSize(10)
                .CreateWithId(true)
                .ImportAttribute("UpdateIdentical", "true"));

            AssertGateClosed();
            Assert.That(Service.CountOf("CreateMultiple"), Is.EqualTo(1), DumpAll());
        }

        [Test]
        public void Any_delete_setting_closes_the_gate()
        {
            Run(GateOpen().ImportAttribute("Delete", "Existing"));

            AssertGateClosed();
        }

        /// <summary>
        /// UpdateIdentical defaults to false, so a definition that sets only CreateWithId and a
        /// match attribute does not silently become an upserting block.
        /// </summary>
        [Test]
        public void The_default_UpdateIdentical_keeps_the_gate_closed()
        {
            Run(DefinitionXml.DataBlock("Accounts", "account")
                .BatchSize(10)
                .CreateWithId(true)
                .MatchOn("name"));

            AssertGateClosed();
        }
    }
}
