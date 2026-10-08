namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer2
{
    using System;
    using System.Linq;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using global::Xrm.Utils.Core.Common.Extensions;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// A matched record that already holds the source values is skipped rather than batched.
    /// </summary>
    /// <remarks>
    /// This is the one decision the batched path makes per record that has nothing to do with
    /// batching, and it is the easiest to break: the comparison reads the source record's own
    /// attribute keys, minus the primary id, so widening a block with one extra column changes
    /// which records count as identical. The counters are what a definition author sees, so the
    /// tests assert Skipped and Updated, not just the request stream.
    /// </remarks>
    [TestFixture]
    public class SkipIdenticalTests : FakeOrgTestBase
    {
        private static Types.DataBlock MatchOnName(bool updateIdentical = false)
        {
            var block = DefinitionXml.DataBlock("Accounts", "account").BatchSize(10);
            if (updateIdentical)
            {
                block = block.ImportAttribute("UpdateIdentical", "true");
            }
            return block.MatchOn("name").DeserializeBlock();
        }

        private static Entity Source(Guid id, string name, string city = null)
        {
            var entity = new Entity("account", id);
            entity["name"] = name;
            if (city != null)
            {
                entity["address1_city"] = city;
            }
            return entity;
        }

        private static EntityCollection Collection(params Entity[] entities)
        {
            var collection = new EntityCollection { EntityName = "account" };
            foreach (var entity in entities)
            {
                collection.Entities.Add(entity);
            }
            return collection;
        }

        /// <summary>A source record with a date as read from a data file.</summary>
        private static Entity SourceWithDate(Guid id, string name, string fileText)
        {
            var entity = Source(id, name);
            entity.SetAttribute(new TestExecutionContainer(null), "lastusedincampaign", "DateTime", fileText);
            return entity;
        }

        private static Entity SeededWithDate(Guid id, string name, DateTime date)
        {
            var entity = Seeded("account", id, name);
            entity["lastusedincampaign"] = date;
            return entity;
        }

        /// <summary>
        /// A data file date is read as UTC and passed on in local time, while Dataverse returns a
        /// user-local column in UTC. Compared as text the same moment read 14:30 against 12:30,
        /// so a record with a date column was never identical.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void A_user_local_date_that_has_not_changed_is_identical(bool preRetrieveAll)
        {
            using (LocalTimeZone.Stockholm())
            {
                Online().WithEntity(SeededWithDate(Id(101), "Alpha", new DateTime(2026, 10, 5, 12, 30, 0, DateTimeKind.Utc)));

                var outcome = NewShuffler().TestImportDataBlock(
                    DefinitionXml.DataBlock("Accounts", "account").BatchSize(10).MatchOn("name", preRetrieveAll).DeserializeBlock(),
                    Collection(SourceWithDate(Id(1), "Alpha", "2026-10-05T12:30:00.0000000Z")));

                Assert.That(outcome.Skipped, Is.EqualTo(1), DumpAll());
                Assert.That(outcome.Updated, Is.EqualTo(0), DumpAll());
            }
        }

        /// <summary>
        /// Date-only and time-zone independent columns come back as the bare clock time, and the
        /// data file holds it without a zone.
        /// </summary>
        [Test]
        public void A_date_only_value_that_has_not_changed_is_identical()
        {
            using (LocalTimeZone.Stockholm())
            {
                Online().WithEntity(SeededWithDate(Id(101), "Alpha", new DateTime(2016, 9, 29, 0, 0, 0, DateTimeKind.Unspecified)));

                var outcome = NewShuffler().TestImportDataBlock(
                    MatchOnName(),
                    Collection(SourceWithDate(Id(1), "Alpha", "2016-09-29T00:00:00.0000000")));

                Assert.That(outcome.Skipped, Is.EqualTo(1), DumpAll());
            }
        }

        /// <summary>Dataverse keeps whole seconds, so a fraction in the file is no change.</summary>
        [Test]
        public void A_fraction_of_a_second_is_not_a_difference()
        {
            using (LocalTimeZone.Stockholm())
            {
                Online().WithEntity(SeededWithDate(Id(101), "Alpha", new DateTime(2026, 10, 5, 12, 30, 0, DateTimeKind.Utc)));

                var outcome = NewShuffler().TestImportDataBlock(
                    MatchOnName(),
                    Collection(SourceWithDate(Id(1), "Alpha", "2026-10-05T12:30:00.4000000Z")));

                Assert.That(outcome.Skipped, Is.EqualTo(1), DumpAll());
            }
        }

        /// <summary>The other side of the fix: a date that really moved is still written.</summary>
        [Test]
        public void A_date_that_changed_by_an_hour_is_updated()
        {
            using (LocalTimeZone.Stockholm())
            {
                Online().WithEntity(SeededWithDate(Id(101), "Alpha", new DateTime(2026, 10, 5, 12, 30, 0, DateTimeKind.Utc)));

                var outcome = NewShuffler().TestImportDataBlock(
                    MatchOnName(),
                    Collection(SourceWithDate(Id(1), "Alpha", "2026-10-05T13:30:00.0000000Z")));

                Assert.That(outcome.Updated, Is.EqualTo(1), DumpAll());
                Assert.That(outcome.Skipped, Is.EqualTo(0), DumpAll());
            }
        }

        [Test]
        public void An_identical_match_is_skipped_and_nothing_is_written()
        {
            Online().WithEntity(
                Seeded("account", Id(101), "Alpha"),
                Seeded("account", Id(102), "Beta"));

            var outcome = NewShuffler().TestImportDataBlock(
                MatchOnName(),
                Collection(Source(Id(1), "Alpha"), Source(Id(2), "Beta")));

            Assert.That(outcome.Skipped, Is.EqualTo(2), DumpAll());
            Assert.That(outcome.Updated, Is.EqualTo(0), DumpAll());
            Assert.That(Service.CountOf("UpdateMultiple"), Is.EqualTo(0), DumpAll());
            Assert.That(
                Org.Logger.Logged("001 Skipped: Alpha (Identical)"),
                DumpAll());
        }

        [Test]
        public void A_differing_attribute_is_batched_as_an_update()
        {
            var existing = Seeded("account", Id(101), "Alpha");
            existing["address1_city"] = "Stockholm";
            var untouched = Seeded("account", Id(102), "Beta");
            untouched["address1_city"] = "Uppsala";
            Online().WithEntity(existing, untouched);

            var outcome = NewShuffler().TestImportDataBlock(
                MatchOnName(),
                Collection(
                    Source(Id(1), "Alpha", "Gothenburg"),
                    Source(Id(2), "Beta", "Uppsala")));

            Assert.That(outcome.Updated, Is.EqualTo(1), DumpAll());
            Assert.That(outcome.Skipped, Is.EqualTo(1), DumpAll());
            Assert.That(Service.CountOf("UpdateMultiple"), Is.EqualTo(0), "one row is not a batch");
            Assert.That(Service.Updated.Count, Is.EqualTo(1), DumpAll());
            Assert.That(Service.Updated[0].Id, Is.EqualTo(Id(101)), DumpAll());
        }

        /// <summary>
        /// UpdateIdentical turns the comparison off wholesale, which is also the setting that
        /// opens the upsert gate - so a block that sets it writes every matched record whether
        /// anything changed or not.
        /// </summary>
        [Test]
        public void UpdateIdentical_writes_the_record_anyway()
        {
            Online().WithEntity(
                Seeded("account", Id(101), "Alpha"),
                Seeded("account", Id(102), "Beta"));

            var outcome = NewShuffler().TestImportDataBlock(
                MatchOnName(true),
                Collection(Source(Id(1), "Alpha"), Source(Id(2), "Beta")));

            Assert.That(outcome.Updated, Is.EqualTo(2), DumpAll());
            Assert.That(outcome.Skipped, Is.EqualTo(0), DumpAll());
            Assert.That(Service.CountOf("UpdateMultiple"), Is.EqualTo(1), DumpAll());
        }

        /// <summary>
        /// The comparison walks the source record's keys, not the target's, so an attribute the
        /// target holds and the source does not cannot make the two differ.
        /// </summary>
        [Test]
        public void An_attribute_only_the_target_holds_does_not_count_as_a_difference()
        {
            var existing = Seeded("account", Id(101), "Alpha");
            existing["address1_city"] = "Stockholm";
            var second = Seeded("account", Id(102), "Beta");
            second["address1_city"] = "Uppsala";
            Online().WithEntity(existing, second);

            var outcome = NewShuffler().TestImportDataBlock(
                MatchOnName(),
                Collection(Source(Id(1), "Alpha"), Source(Id(2), "Beta")));

            Assert.That(outcome.Skipped, Is.EqualTo(2), DumpAll());
            Assert.That(outcome.Updated, Is.EqualTo(0), DumpAll());
        }

        /// <summary>
        /// Skipping does not cost the record its guid mapping - later blocks still have to be
        /// able to point an EntityReference at it.
        /// </summary>
        [Test]
        public void A_skipped_record_is_still_mapped_from_its_source_id()
        {
            Online().WithEntity(Seeded("account", Id(101), "Alpha"));

            var shuffler = NewShuffler();
            shuffler.TestImportDataBlock(
                MatchOnName(),
                Collection(Source(Id(1), "Alpha"), Source(Id(2), "Beta")));

            Assert.That(shuffler.TestGuidMap.ContainsKey(Id(1)), DumpAll());
            Assert.That(shuffler.TestGuidMap[Id(1)], Is.EqualTo(Id(101)), DumpAll());
        }

        [Test]
        public void A_skipped_record_is_not_reported_as_a_reference()
        {
            Online().WithEntity(Seeded("account", Id(101), "Alpha"));

            var outcome = NewShuffler().TestImportDataBlock(
                MatchOnName(),
                Collection(Source(Id(1), "Alpha"), Source(Id(2), "Beta")));

            Assert.That(
                outcome.References.Any(reference => reference.Id == Id(101)),
                Is.False,
                DumpAll());
        }
    }
}
