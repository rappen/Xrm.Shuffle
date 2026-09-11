namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer2
{
    using System;
    using System.Linq;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// Records carrying state or owner leave the batch, and take the batch's ordering with them.
    /// </summary>
    /// <remarks>
    /// <c>IsBatchable</c> rejects any record holding <c>statecode</c>, <c>statuscode</c> or
    /// <c>ownerid</c>, because none of those can be expressed as a plain create or update - they
    /// need a SetState or an Assign afterwards. The consequence worth pinning is not that the
    /// record is excluded, but that reaching it flushes everything queued before it: the product
    /// has to keep the definition's record order, and a batch held open across a non-batchable
    /// record would not.
    /// </remarks>
    [TestFixture]
    public class BatchabilityTests : FakeOrgTestBase
    {
        private static Types.DataBlock CreateBlock()
        {
            return DefinitionXml.DataBlock("Accounts", "account")
                .BatchSize(50)
                .DeserializeBlock();
        }

        private static Entity WithOwner(string name, Guid owner)
        {
            var entity = Record("account", Id(name.Length + owner.GetHashCode()), name);
            entity["ownerid"] = new EntityReference("systemuser", owner);
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

        [Test]
        public void A_record_with_an_owner_is_created_on_its_own()
        {
            Online().WithMetadata("account");

            var outcome = NewShuffler().TestImportDataBlock(
                CreateBlock(),
                Collection(
                    Record("account", Id(1), "Alpha"),
                    WithOwner("Beta", Id(90)),
                    Record("account", Id(3), "Gamma")));

            Assert.That(outcome.Created, Is.EqualTo(3), DumpAll());
            Assert.That(Org.Rows("account").Count, Is.EqualTo(3), DumpAll());
        }

        /// <summary>
        /// Two batched records around one that is not batchable have to come out as two separate
        /// CreateMultiple calls, not one - otherwise the middle record would be written after
        /// both of them.
        /// </summary>
        [Test]
        public void The_batch_is_flushed_before_the_non_batchable_record()
        {
            Online().WithMetadata("account");

            NewShuffler().TestImportDataBlock(
                CreateBlock(),
                Collection(
                    Record("account", Id(1), "Alpha"),
                    Record("account", Id(2), "Beta"),
                    WithOwner("Gamma", Id(90)),
                    Record("account", Id(4), "Delta"),
                    Record("account", Id(5), "Epsilon")));

            Assert.That(Service.CountOf("CreateMultiple"), Is.EqualTo(2), DumpAll());

            var first = RecordingOrganizationService.TargetsOf(
                Service.RequestsNamed("CreateMultiple")[0]);
            Assert.That(first.Count, Is.EqualTo(2), DumpAll());
            Assert.That(first[0]["name"], Is.EqualTo("Alpha"), DumpAll());
            Assert.That(first[1]["name"], Is.EqualTo("Beta"), DumpAll());
        }

        /// <summary>
        /// The interleaving, read off the request stream: batch, single, batch. Anything else
        /// means a record was written out of the order the definition listed it in.
        /// </summary>
        [Test]
        public void The_single_record_is_written_between_the_two_batches()
        {
            Online().WithMetadata("account");

            NewShuffler().TestImportDataBlock(
                CreateBlock(),
                Collection(
                    Record("account", Id(1), "Alpha"),
                    Record("account", Id(2), "Beta"),
                    WithOwner("Gamma", Id(90)),
                    Record("account", Id(4), "Delta"),
                    Record("account", Id(5), "Epsilon")));

            var writes = Service.RequestNames
                .Where(name => name == "Create" || name == "CreateMultiple")
                .ToList();
            Assert.That(
                writes,
                Is.EqualTo(new[] { "CreateMultiple", "Create", "CreateMultiple" }),
                DumpAll());
        }

        /// <summary>
        /// A block made entirely of non-batchable records sends no bulk message at all, which is
        /// the same shape as BatchSize being left at its default.
        /// </summary>
        [Test]
        public void A_block_of_owned_records_never_reaches_a_bulk_message()
        {
            Online().WithMetadata("account");

            var outcome = NewShuffler().TestImportDataBlock(
                CreateBlock(),
                Collection(
                    WithOwner("Alpha", Id(90)),
                    WithOwner("Beta", Id(91))));

            Assert.That(outcome.Created, Is.EqualTo(2), DumpAll());
            Assert.That(Service.CountOf("CreateMultiple"), Is.EqualTo(0), DumpAll());
            Assert.That(Service.CountOf("ExecuteMultiple"), Is.EqualTo(0), DumpAll());
        }
    }
}
