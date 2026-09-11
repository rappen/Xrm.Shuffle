namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer3
{
    using System;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// The guid map, and what gets rewritten through it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A definition carries the ids of the source system. The target assigns its own on create,
    /// so every lookup written after that has to be translated. The map is filled as records are
    /// written and read by ReplaceGuids on each record before it is saved, which is why block
    /// order in a definition is load-bearing: a block pointing at another block only works if
    /// that other block ran first.
    /// </para>
    /// <para>
    /// MapGuid is deliberately picky - it declines an empty id on either side, an id that did
    /// not change, and an id it has already mapped. RecordCreatedId wraps it precisely because
    /// the deferred state and owner queues need the real id in the two cases the map declines.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class GuidRemappingTests : FakeOrgTestBase
    {
        private static Entity Lookup(string entityLogicalName, Guid id, string attribute, Guid target)
        {
            var entity = Record(entityLogicalName, id);
            entity[attribute] = new EntityReference("account", target);
            return entity;
        }

        private static Types.DataBlock Creates(string blockName, string entityLogicalName)
        {
            return DefinitionXml.DataBlock(blockName, entityLogicalName)
                .BatchSize(10)
                .DeserializeBlock();
        }

        // --- MapGuid: what it accepts -------------------------------------------------------

        [Test]
        public void A_changed_id_is_mapped()
        {
            OnPrem();
            var shuffler = NewShuffler();

            shuffler.TestMapGuid(Id(1), Id(101));

            Assert.That(shuffler.TestGuidMap.Count, Is.EqualTo(1));
            Assert.That(shuffler.TestGuidMap[Id(1)], Is.EqualTo(Id(101)));
        }

        [Test]
        public void An_empty_source_id_is_not_mapped()
        {
            OnPrem();
            var shuffler = NewShuffler();

            shuffler.TestMapGuid(Guid.Empty, Id(101));

            Assert.That(shuffler.TestGuidMap, Is.Empty);
        }

        [Test]
        public void An_empty_target_id_is_not_mapped()
        {
            OnPrem();
            var shuffler = NewShuffler();

            shuffler.TestMapGuid(Id(1), Guid.Empty);

            Assert.That(shuffler.TestGuidMap, Is.Empty);
        }

        /// <summary>
        /// An unchanged id needs no translation, so mapping it would only cost a lookup.
        /// </summary>
        [Test]
        public void An_unchanged_id_is_not_mapped()
        {
            OnPrem();
            var shuffler = NewShuffler();

            shuffler.TestMapGuid(Id(1), Id(1));

            Assert.That(shuffler.TestGuidMap, Is.Empty);
        }

        /// <summary>
        /// First writer wins. Two source records sharing an id is a broken definition, and
        /// silently retargeting every later lookup would be worse than keeping the first answer.
        /// </summary>
        [Test]
        public void An_already_mapped_id_keeps_its_first_target()
        {
            OnPrem();
            var shuffler = NewShuffler();

            shuffler.TestMapGuid(Id(1), Id(101));
            shuffler.TestMapGuid(Id(1), Id(102));

            Assert.That(shuffler.TestGuidMap[Id(1)], Is.EqualTo(Id(101)));
        }

        // --- RecordCreatedId: the two cases the map declines --------------------------------

        [Test]
        public void RecordCreatedId_fills_a_deferred_id_even_when_MapGuid_declines_an_unchanged_id()
        {
            OnPrem();
            var shuffler = NewShuffler();
            shuffler.TestDeferState("account", Id(1), Guid.Empty, 1, 2);

            shuffler.TestRecordCreatedId(Id(1), Id(1));

            Assert.That(shuffler.TestGuidMap, Is.Empty, "an unchanged id is still not mapped");
            Assert.That(shuffler.TestDeferredActualId(Id(1)), Is.EqualTo(Id(1)));
        }

        [Test]
        public void RecordCreatedId_fills_a_deferred_id_even_when_the_source_id_is_already_mapped()
        {
            OnPrem();
            var shuffler = NewShuffler();
            shuffler.TestMapGuid(Id(1), Id(101));
            shuffler.TestDeferOwner("account", Id(1), Guid.Empty, new EntityReference("systemuser", Id(9)));

            shuffler.TestRecordCreatedId(Id(1), Id(102));

            Assert.That(shuffler.TestGuidMap[Id(1)], Is.EqualTo(Id(101)), "the map keeps the first target");
            Assert.That(shuffler.TestDeferredOwnerActualId(Id(1)), Is.EqualTo(Id(102)));
        }

        [Test]
        public void RecordCreatedId_leaves_the_deferred_queues_alone_for_an_empty_id()
        {
            OnPrem();
            var shuffler = NewShuffler();
            shuffler.TestDeferState("account", Id(1), Guid.Empty, 1, 2);

            shuffler.TestRecordCreatedId(Id(1), Guid.Empty);

            Assert.That(shuffler.TestDeferredActualId(Id(1)), Is.EqualTo(Guid.Empty));
        }

        // --- ReplaceGuids ------------------------------------------------------------------

        [Test]
        public void A_mapped_lookup_is_rewritten_in_place()
        {
            OnPrem();
            var shuffler = NewShuffler();
            shuffler.TestMapGuid(Id(1), Id(101));
            var record = Lookup("contact", Id(5), "parentcustomerid", Id(1));

            shuffler.TestReplaceGuids(record);

            Assert.That(((EntityReference)record["parentcustomerid"]).Id, Is.EqualTo(Id(101)));
        }

        [Test]
        public void An_unmapped_lookup_is_left_alone()
        {
            OnPrem();
            var shuffler = NewShuffler();
            shuffler.TestMapGuid(Id(1), Id(101));
            var record = Lookup("contact", Id(5), "parentcustomerid", Id(2));

            shuffler.TestReplaceGuids(record);

            Assert.That(((EntityReference)record["parentcustomerid"]).Id, Is.EqualTo(Id(2)));
        }

        [Test]
        public void Every_mapped_lookup_on_a_record_is_rewritten()
        {
            OnPrem();
            var shuffler = NewShuffler();
            shuffler.TestMapGuid(Id(1), Id(101));
            shuffler.TestMapGuid(Id(2), Id(102));
            var record = Record("contact", Id(5));
            record["parentcustomerid"] = new EntityReference("account", Id(1));
            record["originatingleadid"] = new EntityReference("account", Id(2));

            shuffler.TestReplaceGuids(record);

            Assert.That(((EntityReference)record["parentcustomerid"]).Id, Is.EqualTo(Id(101)));
            Assert.That(((EntityReference)record["originatingleadid"]).Id, Is.EqualTo(Id(102)));
        }

        /// <summary>
        /// A raw Guid attribute that happens to be mapped is not a lookup - it is a
        /// uniqueidentifier column holding an id the target does not know about.
        /// </summary>
        [Test]
        public void A_mapped_raw_guid_attribute_is_only_noted_when_ids_are_not_carried_over()
        {
            OnPrem();
            var shuffler = NewShuffler();
            shuffler.TestMapGuid(Id(1), Id(101));
            var record = Record("contact", Id(5));
            record["cint_sourceid"] = Id(1);

            shuffler.TestReplaceGuids(record, includeId: false);

            Assert.That((Guid)record["cint_sourceid"], Is.EqualTo(Id(1)), "left as the source value");
            Assert.That(Org.Logger.Logged("care about the guid of the object"), DumpAll());
        }

        /// <summary>
        /// With CreateWithId the target keeps the source ids, so a mapped raw guid means the
        /// definition asked for something the import cannot honour - and it says so rather than
        /// writing a value it knows is wrong.
        /// </summary>
        [Test]
        public void A_mapped_raw_guid_attribute_is_refused_when_ids_are_carried_over()
        {
            OnPrem();
            var shuffler = NewShuffler();
            shuffler.TestMapGuid(Id(1), Id(101));
            var record = Record("contact", Id(5));
            record["cint_sourceid"] = Id(1);

            Assert.That(
                () => shuffler.TestReplaceGuids(record, includeId: true),
                Throws.TypeOf<NotImplementedException>());
        }

        // --- across blocks -----------------------------------------------------------------

        /// <summary>
        /// The point of all of it: a later block lands its lookups on the records the earlier
        /// block actually wrote, not on the ids the source system used.
        /// </summary>
        [Test]
        public void A_later_block_points_at_what_the_earlier_block_actually_wrote()
        {
            Online().WithMetadata("account").WithMetadata("contact");
            var shuffler = NewShuffler();
            var accounts = new EntityCollection { EntityName = "account" };
            accounts.Entities.Add(Record("account", Id(1), "Alpha"));
            accounts.Entities.Add(Record("account", Id(2), "Beta"));
            var contacts = new EntityCollection { EntityName = "contact" };
            contacts.Entities.Add(Lookup("contact", Id(11), "parentcustomerid", Id(1)));
            contacts.Entities.Add(Lookup("contact", Id(12), "parentcustomerid", Id(2)));

            var accountOutcome = shuffler.TestImportDataBlock(Creates("Accounts", "account"), accounts);
            var contactOutcome = shuffler.TestImportDataBlock(Creates("Contacts", "contact"), contacts);

            Assert.That(accountOutcome.Created, Is.EqualTo(2), DumpAll());
            Assert.That(contactOutcome.Created, Is.EqualTo(2), DumpAll());
            var written = RecordingOrganizationService.TargetsOf(
                Service.RequestsNamed("CreateMultiple")[0]);
            var pointed = RecordingOrganizationService.TargetsOf(
                Service.RequestsNamed("CreateMultiple")[1]);
            Assert.That(
                ((EntityReference)pointed[0]["parentcustomerid"]).Id,
                Is.EqualTo(written[0].Id),
                DumpAll());
            Assert.That(
                ((EntityReference)pointed[1]["parentcustomerid"]).Id,
                Is.EqualTo(written[1].Id),
                DumpAll());
            Assert.That(
                ((EntityReference)pointed[0]["parentcustomerid"]).Id,
                Is.Not.EqualTo(Id(1)),
                "the source id should not have survived");
        }

        [Test]
        public void Creating_a_block_of_records_maps_every_id_it_assigned()
        {
            Online().WithMetadata("account");
            var shuffler = NewShuffler();
            var accounts = new EntityCollection { EntityName = "account" };
            accounts.Entities.Add(Record("account", Id(1), "Alpha"));
            accounts.Entities.Add(Record("account", Id(2), "Beta"));

            shuffler.TestImportDataBlock(Creates("Accounts", "account"), accounts);

            Assert.That(shuffler.TestGuidMap.Count, Is.EqualTo(2), DumpAll());
            Assert.That(shuffler.TestGuidMap.ContainsKey(Id(1)), DumpAll());
            Assert.That(shuffler.TestGuidMap.ContainsKey(Id(2)), DumpAll());
        }
    }
}
