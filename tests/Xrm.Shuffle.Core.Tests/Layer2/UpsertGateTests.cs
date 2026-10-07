namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer2
{
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using System.ServiceModel;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// The conditions that together decide whether a block upserts instead of matching.
    /// </summary>
    /// <remarks>
    /// Upsert skips the pre-retrieval query entirely, which is a large behaviour change for a
    /// block that asked for none of it. The gate is one <c>&amp;&amp;</c> chain - BatchSize is
    /// above 1, Save is CreateUpdate, CreateWithId is set, Delete is None, UpdateIdentical is set,
    /// and the block matches on the primary key alone - so each test here opens the gate and then
    /// breaks exactly one clause. Upsert finds the target by primary key only, so a block matching
    /// on anything else has to keep matching, or it would create duplicates where the ids differ.
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
                .MatchOn("accountid");
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

        private static EntityCollection OneAccount(System.Guid id, string name)
        {
            var entities = new EntityCollection { EntityName = "account" };
            entities.Entities.Add(Record("account", id, name));
            return entities;
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
        public void All_clauses_met_upserts_the_block()
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

        /// <summary>
        /// BatchSize defaults to 1, so a definition that meets every other clause but never asked
        /// for batching keeps the Match-based path it was written against.
        /// </summary>
        [Test]
        public void Without_BatchSize_the_gate_stays_closed()
        {
            Run(DefinitionXml.DataBlock("Accounts", "account")
                .CreateWithId(true)
                .ImportAttribute("UpdateIdentical", "true")
                .MatchOn("accountid"));

            AssertGateClosed();
        }

        [Test]
        public void Matching_on_anything_but_the_primary_key_closes_the_gate()
        {
            Run(GateOpen().MatchOn("name"));

            AssertGateClosed();
        }

        /// <summary>
        /// The failure the gate guards against: the target already holds the record under another
        /// id. Matching on name finds and updates it; an upsert by id would have added a second one.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void A_name_match_updates_the_existing_record_instead_of_adding_one(bool online)
        {
            (online ? Online() : OnPrem()).WithEntity(Seeded("account", Id(101), "Alpha"));
            var block = DefinitionXml.DataBlock("Accounts", "account")
                .BatchSize(10)
                .CreateWithId(true)
                .ImportAttribute("UpdateIdentical", "true")
                .MatchOn("name")
                .DeserializeBlock();

            var outcome = NewShuffler().TestImportDataBlock(block, OneAccount(Id(1), "Alpha"));

            Assert.That(outcome.Updated, Is.EqualTo(1), DumpAll());
            Assert.That(outcome.Created, Is.EqualTo(0), DumpAll());
            Assert.That(Org.Rows("account").Count, Is.EqualTo(1), "no duplicate should have been created");
            AssertGateClosed();
        }

        /// <summary>
        /// A batch of one goes through the single-record upsert. Where Upsert is not available it
        /// falls back like a batch does - Create, then Update when the record already exists -
        /// rather than failing every record that is already in the target.
        /// </summary>
        /// <remarks>
        /// The fault is scripted because the fake org words its duplicate-id error differently from
        /// Dataverse, which answers 0x80040237 "Cannot insert duplicate key."
        /// </remarks>
        [Test]
        public void A_single_upsert_without_Upsert_support_updates_an_existing_record()
        {
            OnPrem().WithEntity(Seeded("account", Id(1), "Alpha"));
            Service.ThrowOnce("Create", new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040237), Message = "Cannot insert duplicate key." },
                new FaultReason("Cannot insert duplicate key.")));

            var outcome = NewShuffler().TestImportDataBlock(GateOpen().DeserializeBlock(), OneAccount(Id(1), "Alpha renamed"));

            Assert.That(outcome.Failed, Is.EqualTo(0), DumpAll());
            Assert.That(outcome.Updated, Is.EqualTo(1), DumpAll());
            Assert.That(Org.Rows("account").Count, Is.EqualTo(1), DumpAll());
            Assert.That(Org.Logger.Logged("Upsert path enabled"), DumpAll());
        }
    }
}
