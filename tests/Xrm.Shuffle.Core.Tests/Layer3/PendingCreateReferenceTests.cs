namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer3
{
    using System;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// The guard that keeps batching from writing a lookup to a record that does not exist yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Batching defers the create, so the id the target assigns is not known until the batch is
    /// flushed. A later record in the same block pointing at an earlier one would therefore be
    /// written with the id of the source system - a lookup into nothing.
    /// </para>
    /// <para>
    /// ReferencesPendingCreate is checked before each record is prepared, and a hit flushes the
    /// create batch early so the ids exist and the guid map can translate them. The cost is a
    /// shorter batch; the alternative is silent data loss, so the guard is deliberately eager -
    /// it looks at every attribute of the record, matches on the id of the source system rather
    /// than on what the record holds now, and does not care which entity is referenced.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class PendingCreateReferenceTests : FakeOrgTestBase
    {
        private static Shuffler.TestCreateBatch Pending(params Guid[] oldIds)
        {
            var batch = new Shuffler.TestCreateBatch();
            var seed = 500;
            foreach (var oldId in oldIds)
            {
                batch.Add(Record("account", Id(seed++)), oldId);
            }
            return batch;
        }

        private static Entity Pointing(Guid id, string attribute, object value)
        {
            var entity = Record("contact", id);
            entity[attribute] = value;
            return entity;
        }

        private static Types.DataBlock Creates()
        {
            return DefinitionXml.DataBlock("Accounts", "account")
                .BatchSize(10)
                .DeserializeBlock();
        }

        private static EntityCollection Referencing()
        {
            var sources = new EntityCollection { EntityName = "account" };
            sources.Entities.Add(Record("account", Id(1), "Alpha"));
            sources.Entities.Add(Record("account", Id(2), "Beta"));
            sources.Entities.Add(Child(Id(3), "Gamma", Id(1)));
            sources.Entities.Add(Record("account", Id(4), "Delta"));
            return sources;
        }

        private static Entity Child(Guid id, string name, Guid parent)
        {
            var entity = Record("account", id, name);
            entity["parentaccountid"] = new EntityReference("account", parent);
            return entity;
        }

        // --- the guard itself ---------------------------------------------------------------

        [Test]
        public void An_empty_batch_is_never_referenced()
        {
            OnPrem();

            Assert.That(
                Shuffler.TestReferencesPendingCreate(
                    Pointing(Id(5), "parentcustomerid", new EntityReference("account", Id(1))),
                    Pending()),
                Is.False);
        }

        [Test]
        public void A_lookup_to_a_queued_record_is_a_hit()
        {
            OnPrem();

            Assert.That(
                Shuffler.TestReferencesPendingCreate(
                    Pointing(Id(5), "parentcustomerid", new EntityReference("account", Id(1))),
                    Pending(Id(1))));
        }

        /// <summary>
        /// A uniqueidentifier column counts too - the guard cannot tell a lookup stored as a raw
        /// guid from one stored as an EntityReference, and guessing wrong loses data.
        /// </summary>
        [Test]
        public void A_raw_guid_attribute_pointing_at_a_queued_record_is_a_hit()
        {
            OnPrem();

            Assert.That(
                Shuffler.TestReferencesPendingCreate(
                    Pointing(Id(5), "cint_sourceid", Id(1)),
                    Pending(Id(1))));
        }

        [Test]
        public void A_lookup_to_something_not_queued_is_not_a_hit()
        {
            OnPrem();

            Assert.That(
                Shuffler.TestReferencesPendingCreate(
                    Pointing(Id(5), "parentcustomerid", new EntityReference("account", Id(2))),
                    Pending(Id(1))),
                Is.False);
        }

        [Test]
        public void An_empty_lookup_is_not_a_hit()
        {
            OnPrem();

            Assert.That(
                Shuffler.TestReferencesPendingCreate(
                    Pointing(Id(5), "parentcustomerid", new EntityReference("account", Guid.Empty)),
                    Pending(Id(1))),
                Is.False);
        }

        [Test]
        public void A_record_with_no_lookups_at_all_is_not_a_hit()
        {
            OnPrem();

            Assert.That(
                Shuffler.TestReferencesPendingCreate(Record("contact", Id(5)), Pending(Id(1))),
                Is.False);
        }

        [Test]
        public void Any_one_of_several_queued_records_is_enough()
        {
            OnPrem();

            Assert.That(
                Shuffler.TestReferencesPendingCreate(
                    Pointing(Id(5), "parentcustomerid", new EntityReference("account", Id(3))),
                    Pending(Id(1), Id(2), Id(3))));
        }

        /// <summary>
        /// The match is on the id of the source system, not on the id the queued entity holds.
        /// The no-match create branch blanks the id before queueing, so by the time the guard
        /// runs the entity in the batch usually has no id at all.
        /// </summary>
        [Test]
        public void The_match_is_on_the_source_id_not_on_what_the_queued_entity_holds()
        {
            OnPrem();
            var batch = new Shuffler.TestCreateBatch();
            var queued = Record("account", Guid.Empty);
            batch.Add(queued, Id(1));

            Assert.That(
                Shuffler.TestReferencesPendingCreate(
                    Pointing(Id(5), "parentcustomerid", new EntityReference("account", Id(1))),
                    batch));
            Assert.That(queued.Id, Is.EqualTo(Guid.Empty), "the queued record still has no id");
        }

        // --- what the guard does to a block ------------------------------------------------

        /// <summary>
        /// The third record points at the first, so the batch is flushed before it is prepared,
        /// and the block ends up sending two batches where an unreferenced block sends one.
        /// </summary>
        [Test]
        public void A_record_pointing_at_an_earlier_one_cuts_the_batch_short()
        {
            Online().WithMetadata("account");

            var outcome = NewShuffler().TestImportDataBlock(Creates(), Referencing());

            Assert.That(outcome.Created, Is.EqualTo(4), DumpAll());
            Assert.That(Service.CountOf("CreateMultiple"), Is.EqualTo(2), DumpAll());
            Assert.That(
                RecordingOrganizationService.TargetsOf(
                    Service.RequestsNamed("CreateMultiple")[0]).Count,
                Is.EqualTo(2),
                "cut short at the two records queued before the reference");
        }

        [Test]
        public void A_block_with_no_cross_references_stays_one_batch()
        {
            Online().WithMetadata("account");
            var sources = new EntityCollection { EntityName = "account" };
            sources.Entities.Add(Record("account", Id(1), "Alpha"));
            sources.Entities.Add(Record("account", Id(2), "Beta"));
            sources.Entities.Add(Record("account", Id(3), "Gamma"));
            sources.Entities.Add(Record("account", Id(4), "Delta"));

            var outcome = NewShuffler().TestImportDataBlock(Creates(), sources);

            Assert.That(outcome.Created, Is.EqualTo(4), DumpAll());
            Assert.That(Service.CountOf("CreateMultiple"), Is.EqualTo(1), DumpAll());
        }

        /// <summary>
        /// The point of flushing early: the lookup is rewritten to the id the target actually
        /// assigned, not the id the source system used.
        /// </summary>
        [Test]
        public void The_lookup_is_written_against_the_id_the_target_assigned()
        {
            Online().WithMetadata("account");

            NewShuffler().TestImportDataBlock(Creates(), Referencing());

            var first = RecordingOrganizationService.TargetsOf(
                Service.RequestsNamed("CreateMultiple")[0]);
            var second = RecordingOrganizationService.TargetsOf(
                Service.RequestsNamed("CreateMultiple")[1]);
            var written = ((EntityReference)second[0]["parentaccountid"]).Id;
            Assert.That(written, Is.EqualTo(first[0].Id), DumpAll());
            Assert.That(written, Is.Not.EqualTo(Id(1)), "the source id should not have survived");
        }

        /// <summary>
        /// A record pointing at one that an earlier batch already wrote does not cut the batch
        /// again - the pending list no longer holds it, so the guard has nothing to hit and the
        /// guid map is what supplies the id.
        /// </summary>
        [Test]
        public void Pointing_at_an_already_written_record_does_not_cut_the_batch()
        {
            Online().WithMetadata("account");
            var sources = Referencing();
            sources.Entities[3]["parentaccountid"] = new EntityReference("account", Id(1));

            var outcome = NewShuffler().TestImportDataBlock(Creates(), sources);

            Assert.That(outcome.Created, Is.EqualTo(4), DumpAll());
            Assert.That(Service.CountOf("CreateMultiple"), Is.EqualTo(2), DumpAll());
            var second = RecordingOrganizationService.TargetsOf(
                Service.RequestsNamed("CreateMultiple")[1]);
            Assert.That(second.Count, Is.EqualTo(2), "both referencing records batch together");
        }

        /// <summary>
        /// The early flush takes whatever is queued, so a reference to the record immediately
        /// before it leaves a batch of one - which the dispatcher then sends as a plain Create.
        /// The guard trades throughput for correctness and does not try to soften that.
        /// </summary>
        [Test]
        public void An_early_flush_of_one_queued_record_goes_out_as_a_plain_create()
        {
            Online().WithMetadata("account");
            var sources = new EntityCollection { EntityName = "account" };
            sources.Entities.Add(Record("account", Id(1), "Alpha"));
            sources.Entities.Add(Child(Id(2), "Beta", Id(1)));
            sources.Entities.Add(Record("account", Id(3), "Gamma"));

            var outcome = NewShuffler().TestImportDataBlock(Creates(), sources);

            Assert.That(outcome.Created, Is.EqualTo(3), DumpAll());
            Assert.That(Service.CountOf("Create"), Is.EqualTo(1), DumpAll());
            Assert.That(Service.CountOf("CreateMultiple"), Is.EqualTo(1), DumpAll());
        }
    }
}
