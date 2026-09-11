namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer2
{
    using System.Linq;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// Which message a batch goes out as, decided by what the organization says it supports.
    /// </summary>
    /// <remarks>
    /// The product asks <c>sdkmessagefilter</c> once per entity and message, caches the answer
    /// for the run, and silently caches <c>false</c> if the query throws. That makes an
    /// unasserted routing decision worthless - a test that only counted records would pass
    /// against the fallback path just as happily. Every test here names the message it expects.
    /// </remarks>
    [TestFixture]
    public class CapabilityDetectionTests : FakeOrgTestBase
    {
        private static EntityCollection TwoAccounts()
        {
            var entities = new EntityCollection { EntityName = "account" };
            entities.Entities.Add(Record("account", Id(1), "Alpha"));
            entities.Entities.Add(Record("account", Id(2), "Beta"));
            return entities;
        }

        private static Types.DataBlock CreateBlock(int batchSize)
        {
            return DefinitionXml.DataBlock("Accounts", "account")
                .BatchSize(batchSize)
                .DeserializeBlock();
        }

        [Test]
        public void An_org_that_supports_CreateMultiple_gets_one_CreateMultiple()
        {
            Online().WithMetadata("account");

            var outcome = NewShuffler().TestImportDataBlock(CreateBlock(10), TwoAccounts());

            Assert.That(outcome.Created, Is.EqualTo(2), DumpAll());
            Assert.That(Service.CountOf("CreateMultiple"), Is.EqualTo(1), DumpAll());
            Assert.That(Service.CountOf("ExecuteMultiple"), Is.EqualTo(0), DumpAll());
            Assert.That(Org.Rows("account").Count, Is.EqualTo(2), DumpAll());
        }

        [Test]
        public void An_org_without_CreateMultiple_falls_back_to_ExecuteMultiple()
        {
            OnPrem().WithMetadata("account");

            var outcome = NewShuffler().TestImportDataBlock(CreateBlock(10), TwoAccounts());

            Assert.That(outcome.Created, Is.EqualTo(2), DumpAll());
            Assert.That(Service.CountOf("CreateMultiple"), Is.EqualTo(0), DumpAll());
            Assert.That(Service.CountOf("ExecuteMultiple"), Is.EqualTo(1), DumpAll());
            Assert.That(Org.Rows("account").Count, Is.EqualTo(2), DumpAll());
        }

        [Test]
        public void The_probe_result_is_cached_for_the_whole_run()
        {
            Online().WithMetadata("account");

            var shuffler = NewShuffler();
            shuffler.TestImportDataBlock(CreateBlock(2), TwoAccounts());
            shuffler.TestImportDataBlock(CreateBlock(2), TwoAccounts());

            Assert.That(Service.CountOf("CreateMultiple"), Is.EqualTo(2), DumpAll());
            Assert.That(
                Org.Logger.CountLogged("CreateMultiple support for account"),
                Is.EqualTo(1),
                DumpAll());
        }

        [Test]
        public void The_probe_announces_the_answer_it_cached()
        {
            OnPrem().WithMetadata("account");

            NewShuffler().TestImportDataBlock(CreateBlock(10), TwoAccounts());

            Assert.That(Org.Logger.Logged("CreateMultiple support for account: False"), DumpAll());
        }

        /// <summary>
        /// A landmine marker, not a requirement. <c>sdkmessagefilter.primaryobjecttypecode</c>
        /// holds a numeric entity type code on a real platform, but the product queries it with
        /// the logical name, so the seed in <see cref="ShuffleTestContext"/> matches the product
        /// rather than the platform. If the product is ever fixed to query by type code, this
        /// test fails and points at both places that have to change together.
        /// </summary>
        [Test]
        public void Capability_query_uses_the_logical_name_not_the_entity_type_code()
        {
            Online().WithMetadata("account");

            NewShuffler().TestImportDataBlock(CreateBlock(10), TwoAccounts());

            var probe = Service.Queries
                .OfType<Microsoft.Xrm.Sdk.Query.QueryExpression>()
                .FirstOrDefault(q => q.EntityName == "sdkmessagefilter");

            Assert.That(probe, Is.Not.Null, DumpAll());
            var condition = probe.Criteria.Conditions
                .First(c => c.AttributeName == "primaryobjecttypecode");
            Assert.That(condition.Values.Single(), Is.EqualTo("account"), DumpAll());
        }
    }
}
