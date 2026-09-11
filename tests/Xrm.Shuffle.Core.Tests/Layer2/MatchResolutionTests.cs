namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer2
{
    using System.Linq;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// What a match attribute resolves to, and what the block does with each answer.
    /// </summary>
    /// <remarks>
    /// Three answers are possible and all three are reachable from the same definition: no
    /// match creates, one match updates, several matches fail the record without touching the
    /// organization. The interesting part is that the first two go into a batch while the third
    /// does not, so a block of mixed records ends up with fewer batched rows than it read.
    /// </remarks>
    [TestFixture]
    public class MatchResolutionTests : FakeOrgTestBase
    {
        private static Types.DataBlock MatchOnName()
        {
            return DefinitionXml.DataBlock("Accounts", "account")
                .BatchSize(10)
                .ImportAttribute("UpdateIdentical", "true")
                .MatchOn("name")
                .DeserializeBlock();
        }

        private static EntityCollection Sources(params string[] names)
        {
            var entities = new EntityCollection { EntityName = "account" };
            var seed = 1;
            foreach (var name in names)
            {
                entities.Entities.Add(Record("account", Id(seed++), name));
            }
            return entities;
        }

        [Test]
        public void No_match_creates_the_record()
        {
            Online().WithMetadata("account");

            var outcome = NewShuffler().TestImportDataBlock(MatchOnName(), Sources("Alpha", "Beta"));

            Assert.That(outcome.Created, Is.EqualTo(2), DumpAll());
            Assert.That(outcome.Updated, Is.EqualTo(0), DumpAll());
            Assert.That(Service.CountOf("CreateMultiple"), Is.EqualTo(1), DumpAll());
            Assert.That(Service.CountOf("UpdateMultiple"), Is.EqualTo(0), DumpAll());
        }

        [Test]
        public void One_match_updates_it_in_place()
        {
            Online()
                .WithEntity(
                    Seeded("account", Id(101), "Alpha"),
                    Seeded("account", Id(102), "Beta"));

            var outcome = NewShuffler().TestImportDataBlock(MatchOnName(), Sources("Alpha", "Beta"));

            Assert.That(outcome.Updated, Is.EqualTo(2), DumpAll());
            Assert.That(outcome.Created, Is.EqualTo(0), DumpAll());
            Assert.That(Service.CountOf("UpdateMultiple"), Is.EqualTo(1), DumpAll());
            Assert.That(Org.Rows("account").Count, Is.EqualTo(2), "no row should have been added");
        }

        [Test]
        public void An_update_is_written_against_the_matched_id_not_the_source_id()
        {
            // Two matching records, because a batch of one is flushed as a plain Update and the
            // request this asserts on would never be sent.
            Online()
                .WithEntity(
                    Seeded("account", Id(101), "Alpha"),
                    Seeded("account", Id(102), "Beta"));

            NewShuffler().TestImportDataBlock(MatchOnName(), Sources("Alpha", "Beta"));

            var targets = RecordingOrganizationService.TargetsOf(
                Service.RequestsNamed("UpdateMultiple")[0]);
            Assert.That(
                targets.Select(t => t.Id).ToList(),
                Is.EqualTo(new[] { Id(101), Id(102) }),
                DumpAll());
            Assert.That(targets.Select(t => t.Id), Has.No.Member(Id(1)), DumpAll());
        }

        [Test]
        public void Several_matches_fail_the_record_and_write_nothing()
        {
            Online()
                .WithEntity(
                    Seeded("account", Id(101), "Alpha"),
                    Seeded("account", Id(102), "Alpha"));

            var outcome = NewShuffler().TestImportDataBlock(MatchOnName(), Sources("Alpha"));

            Assert.That(outcome.Failed, Is.EqualTo(1), DumpAll());
            Assert.That(outcome.Accounted, Is.EqualTo(1), DumpAll());
            Assert.That(
                Org.Logger.Logged("001 Match Failed: Alpha matches 2 records in target database"),
                DumpAll());
            Assert.That(Service.CountOf("UpdateMultiple"), Is.EqualTo(0), DumpAll());
            Assert.That(Service.CountOf("CreateMultiple"), Is.EqualTo(0), DumpAll());
        }

        [Test]
        public void An_ambiguous_record_does_not_stop_the_rest_of_the_block()
        {
            Online()
                .WithEntity(
                    Seeded("account", Id(101), "Alpha"),
                    Seeded("account", Id(102), "Alpha"),
                    Seeded("account", Id(103), "Beta"));

            var outcome = NewShuffler().TestImportDataBlock(
                MatchOnName(), Sources("Alpha", "Beta", "Gamma"));

            Assert.That(outcome.Failed, Is.EqualTo(1), DumpAll());
            Assert.That(outcome.Updated, Is.EqualTo(1), DumpAll());
            Assert.That(outcome.Created, Is.EqualTo(1), DumpAll());
            Assert.That(outcome.Accounted, Is.EqualTo(3), DumpAll());
        }

        /// <summary>
        /// PreRetrieveAll takes one snapshot for the whole block, which is the property that
        /// lets a matched block batch at all - a per-record match query would have to see the
        /// rows the batch has not written yet.
        /// </summary>
        [Test]
        public void PreRetrieveAll_reads_the_target_entity_once_for_the_whole_block()
        {
            Online().WithEntity(
                Seeded("account", Id(101), "Alpha"),
                Seeded("account", Id(102), "Beta"));

            NewShuffler().TestImportDataBlock(MatchOnName(), Sources("Alpha", "Beta"));

            var accountQueries = 0;
            foreach (var query in Service.Queries)
            {
                var expression = query as Microsoft.Xrm.Sdk.Query.QueryExpression;
                if (expression != null && expression.EntityName == "account")
                {
                    accountQueries++;
                }
            }
            Assert.That(accountQueries, Is.EqualTo(1), DumpAll());
        }
    }
}
