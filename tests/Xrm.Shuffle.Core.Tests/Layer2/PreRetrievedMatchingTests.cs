namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer2
{
    using System;
    using System.Linq;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using global::Xrm.Utils.Core.Common.Extensions;
    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.Query;
    using NUnit.Framework;

    /// <summary>
    /// Matching against the target records a PreRetrieveAll block reads up front.
    /// </summary>
    /// <remarks>
    /// The snapshot used to be a single RetrieveMultiple, which Dataverse cuts off at 5000
    /// records, so every source record whose match lay past that was created a second time. It
    /// was also scanned in full for every source record. It is now read page by page and looked
    /// up by match key, and these tests pin both - plus the matching rules the lookup has to keep.
    /// </remarks>
    [TestFixture]
    public class PreRetrievedMatchingTests : FakeOrgTestBase
    {
        private static DefinitionXml.DataBlockBuilder MatchOnName()
        {
            return DefinitionXml.DataBlock("Accounts", "account")
                .BatchSize(10)
                .ImportAttribute("UpdateIdentical", "true")
                .MatchOn("name");
        }

        private static EntityCollection Sources(params Entity[] records)
        {
            var entities = new EntityCollection { EntityName = "account" };
            entities.Entities.AddRange(records);
            return entities;
        }

        private int AccountQueries()
        {
            return Service.Queries.OfType<QueryExpression>().Count(q => q.EntityName == "account");
        }

        [Test]
        public void A_match_past_the_first_page_of_5000_target_records_is_found()
        {
            const int targets = RecordingOrganizationService.MaxPageSize + 50;
            Online().WithEntity(Enumerable.Range(0, targets)
                .Select(i => Seeded("account", Id(1000 + i), "Account " + i))
                .ToArray());

            var outcome = NewShuffler().TestImportDataBlock(
                MatchOnName().DeserializeBlock(),
                Sources(Record("account", Id(1), "Account " + (targets - 1))));

            Assert.That(outcome.Created, Is.EqualTo(0), DumpAll());
            Assert.That(outcome.Failed, Is.EqualTo(0), DumpAll());
            Assert.That(outcome.Updated, Is.EqualTo(1), DumpAll());
            Assert.That(Org.Rows("account").Count, Is.EqualTo(targets), "no duplicate should have been created");
            Assert.That(AccountQueries(), Is.EqualTo(2), "the snapshot should be read as two pages");
            Assert.That(Org.Logger.Logged("Pre-retrieved " + targets + " records for matching"), DumpAll());
        }

        /// <summary>
        /// A Match on a date column builds its key from the same comparable text as the identical
        /// check: a file date (local) and the stored date (UTC) of the same moment must meet, or
        /// the record is created a second time.
        /// </summary>
        [Test]
        public void A_match_on_a_date_column_finds_the_existing_record()
        {
            using (LocalTimeZone.Stockholm())
            {
                var existing = Seeded("account", Id(101), "Alpha");
                existing["lastusedincampaign"] = new DateTime(2026, 10, 5, 12, 30, 0, DateTimeKind.Utc);
                Online().WithEntity(existing);
                var source = Record("account", Id(1), "Alpha");
                source.SetAttribute(new TestExecutionContainer(null), "lastusedincampaign", "DateTime", "2026-10-05T12:30:00.0000000Z");

                var outcome = NewShuffler().TestImportDataBlock(
                    DefinitionXml.DataBlock("Accounts", "account")
                        .BatchSize(10)
                        .ImportAttribute("UpdateIdentical", "true")
                        .MatchOn("lastusedincampaign")
                        .DeserializeBlock(),
                    Sources(source));

                Assert.That(outcome.Created, Is.EqualTo(0), DumpAll());
                Assert.That(outcome.Updated, Is.EqualTo(1), DumpAll());
                Assert.That(Org.Rows("account").Count, Is.EqualTo(1), "no duplicate should have been created");
            }
        }

        /// <summary>
        /// A Match on an amount compares values, not text: 4999.0000 from the data file meets
        /// 4999.00 in the target instead of creating the record a second time.
        /// </summary>
        [Test]
        public void A_match_on_an_amount_finds_the_existing_record_whatever_its_decimals()
        {
            var existing = Seeded("account", Id(101), "Alpha");
            existing["revenue"] = new Money(4999.00m);
            Online().WithEntity(existing);
            var source = Record("account", Id(1), "Alpha");
            source.SetAttribute(new TestExecutionContainer(null), "revenue", "Money", "4999.0000");

            var outcome = NewShuffler().TestImportDataBlock(
                DefinitionXml.DataBlock("Accounts", "account")
                    .BatchSize(10)
                    .ImportAttribute("UpdateIdentical", "true")
                    .MatchOn("revenue")
                    .DeserializeBlock(),
                Sources(source));

            Assert.That(outcome.Created, Is.EqualTo(0), DumpAll());
            Assert.That(outcome.Updated, Is.EqualTo(1), DumpAll());
            Assert.That(Org.Rows("account").Count, Is.EqualTo(1), "no duplicate should have been created");
        }

        [Test]
        public void Several_target_records_with_the_same_match_values_fail_the_match()
        {
            Online().WithEntity(
                Seeded("account", Id(101), "Alpha"),
                Seeded("account", Id(102), "Alpha"),
                Seeded("account", Id(103), "Beta"));

            var outcome = NewShuffler().TestImportDataBlock(
                MatchOnName().DeserializeBlock(),
                Sources(Record("account", Id(1), "Alpha")));

            Assert.That(outcome.Failed, Is.EqualTo(1), DumpAll());
            Assert.That(outcome.Created + outcome.Updated, Is.EqualTo(0), DumpAll());
            Assert.That(Org.Logger.Logged("matches 2 records in target database"), DumpAll());
            Assert.That(Org.Logger.Logged("2 distinct match keys"), DumpAll());
        }

        /// <summary>
        /// A match attribute absent on both sides compares as "&lt;null&gt;" against
        /// "&lt;null&gt;", so it matches - as it did when the snapshot was scanned.
        /// </summary>
        [Test]
        public void A_match_attribute_missing_on_both_sides_still_matches()
        {
            Online()
                .WithAttributes("account", "accountnumber")
                .WithEntity(Seeded("account", Id(101), "Alpha"));
            var block = MatchOnName().MatchOn("accountnumber").DeserializeBlock();

            var withoutNumber = Record("account", Id(1), "Alpha");
            var withNumber = Record("account", Id(2), "Alpha");
            withNumber["accountnumber"] = "A-1";

            var outcome = NewShuffler().TestImportDataBlock(block, Sources(withoutNumber, withNumber));

            Assert.That(outcome.Updated, Is.EqualTo(1), "the record without a number matches the target without one. " + DumpAll());
            Assert.That(outcome.Created, Is.EqualTo(1), "the record with a number has no match. " + DumpAll());
        }

        /// <summary>
        /// A source record carries its primary key in Entity.Id, not as an attribute, so the
        /// source side of the key has to be read from there.
        /// </summary>
        [Test]
        public void Matching_on_the_primary_key_reads_the_source_id()
        {
            Online().WithEntity(
                Seeded("account", Id(5), "Alpha"),
                Seeded("account", Id(6), "Beta"));
            var block = DefinitionXml.DataBlock("Accounts", "account")
                .BatchSize(10)
                .MatchOn("accountid")
                .DeserializeBlock();

            var outcome = NewShuffler().TestImportDataBlock(block, Sources(Record("account", Id(5), "Alpha renamed")));

            Assert.That(outcome.Updated, Is.EqualTo(1), DumpAll());
            Assert.That(outcome.Created, Is.EqualTo(0), DumpAll());
            Assert.That(Org.Rows("account").Count, Is.EqualTo(2), DumpAll());
        }
    }
}
