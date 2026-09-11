namespace Cinteros.Crm.Utils.Shuffle.Tests.Regressions
{
    using System.ServiceModel;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// Failures that leave the block by a route the per-record catch cannot see.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ImportDataBlock wraps each record in a try, counts a failure, names the record and carries
    /// on. That is the whole error story for anything a record does on its own. Batching adds two
    /// routes around it. The delete-all pass runs before the record loop opens, and the three
    /// flushes that empty the pending batches run after it closes, so a throw from either lands
    /// outside the try and takes the whole block with it.
    /// </para>
    /// <para>
    /// The other half is what the catch prints when it does fire. A flush that faults in the
    /// middle of the loop is reported against whichever record happened to fill the batch, not
    /// the one that faulted, unless the flush left a label behind. StopOnBatchError is what
    /// leaves that label; where nothing sets one, the wrong record gets the blame and the tests
    /// below say so plainly.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class LogMessageTests : FakeOrgTestBase
    {
        private static Types.DataBlock DeleteEverything()
        {
            return DefinitionXml.DataBlock("Accounts", "account")
                .BatchSize(10)
                .ImportAttribute("Delete", "All")
                .DeserializeBlock();
        }

        private static Types.DataBlock CreateInBatchesOf(int batchSize)
        {
            return DefinitionXml.DataBlock("Accounts", "account")
                .BatchSize(batchSize)
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

        private static EntityCollection NoSources()
        {
            return new EntityCollection { EntityName = "account" };
        }

        #region Deletes that escape the block

        /// <summary>
        /// The delete-all pass sits above the record loop, so the rethrow in the sequential
        /// fallback has nothing to catch it. Nothing after it in the block runs.
        /// </summary>
        [Test]
        public void A_delete_fault_that_is_not_a_missing_record_escapes_the_block_entirely()
        {
            Online().WithEntity(
                Seeded("account", Id(101), "Alpha"),
                Seeded("account", Id(102), "Beta"));
            Service
                .ThrowAlways("ExecuteMultiple", ExecuteMultipleResponseBuilder.Faulted("Batch delete refused"))
                .ThrowAlways("Delete", ExecuteMultipleResponseBuilder.Faulted("Privilege denied"));

            var shuffler = NewShuffler();

            Assert.Throws<FaultException<OrganizationServiceFault>>(
                () => shuffler.TestImportDataBlock(DeleteEverything(), NoSources()),
                DumpAll());
            Recorder.AssertLogged("Falling back to sequential deletes");
            Recorder.AssertNeverSent("*** Error record");
        }

        /// <summary>
        /// The one fault the fallback does swallow, because a cascade delete in the target may
        /// legitimately have taken the record already. It is not counted as a delete either.
        /// </summary>
        [Test]
        public void A_record_that_was_already_gone_is_tolerated_by_the_same_fallback()
        {
            Online().WithEntity(
                Seeded("account", Id(101), "Alpha"),
                Seeded("account", Id(102), "Beta"));
            Service
                .ThrowAlways("ExecuteMultiple", ExecuteMultipleResponseBuilder.Faulted("Batch delete refused"))
                .ThrowAlways("Delete", ExecuteMultipleResponseBuilder.Faulted(
                    "account With Id = 00000000-0000-0000-0000-000000000065 Does Not Exist"));

            var outcome = NewShuffler().TestImportDataBlock(DeleteEverything(), NoSources());

            Assert.That(outcome.Deleted, Is.EqualTo(0), DumpAll());
            Assert.That(outcome.Failed, Is.EqualTo(0), DumpAll());
            Recorder.AssertSent("...already deleted");
        }

        /// <summary>
        /// StopOnError does not reach the delete-all pass at all - the rethrow is unconditional,
        /// so the block ends the same way whether the definition asked to stop or not.
        /// </summary>
        [Test]
        public void StopOnError_makes_no_difference_to_a_delete_that_escapes()
        {
            Online().WithEntity(
                Seeded("account", Id(101), "Alpha"),
                Seeded("account", Id(102), "Beta"));
            Service
                .ThrowAlways("ExecuteMultiple", ExecuteMultipleResponseBuilder.Faulted("Batch delete refused"))
                .ThrowAlways("Delete", ExecuteMultipleResponseBuilder.Faulted("Privilege denied"));

            var shuffler = NewShuffler(stopOnError: false);

            Assert.Throws<FaultException<OrganizationServiceFault>>(
                () => shuffler.TestImportDataBlock(DeleteEverything(), NoSources()),
                DumpAll());
            Assert.That(shuffler.TestBatchFailureLabel, Is.Null, DumpAll());
        }

        #endregion Deletes that escape the block

        #region Flushes that escape the block

        /// <summary>
        /// The three flushes at the end of the block run after the record loop has closed, so a
        /// StopOnError rethrow from one of them is never seen by the per-record catch and no
        /// record is named at all.
        /// </summary>
        [Test]
        public void An_end_of_block_flush_that_fails_under_StopOnError_escapes_without_naming_a_record()
        {
            // On-prem: no bulk messages, so the batch goes out as ExecuteMultiple and that is
            // the rung whose catch rethrows under StopOnError.
            OnPrem().WithMetadata("account");
            Service.ThrowAlways(
                "ExecuteMultiple", ExecuteMultipleResponseBuilder.Faulted("Batch create refused"));

            var shuffler = NewShuffler(stopOnError: true);

            Assert.Throws<FaultException<OrganizationServiceFault>>(
                () => shuffler.TestImportDataBlock(CreateInBatchesOf(10), Sources("Alpha", "Beta")),
                DumpAll());
            Recorder.AssertLogged("ExecuteMultiple batch create failed");
            Recorder.AssertNeverSent("*** Error record");
            Assert.That(shuffler.TestBatchFailureLabel, Is.Null, DumpAll());
        }

        /// <summary>
        /// Without StopOnError the same two faults are contained: the flush drops to one create
        /// per record and the block finishes normally.
        /// </summary>
        [Test]
        public void Without_StopOnError_the_same_failure_drops_to_one_create_per_record()
        {
            // On-prem: no bulk messages, so the batch goes out as ExecuteMultiple and that is
            // the rung whose catch rethrows under StopOnError.
            OnPrem().WithMetadata("account");
            Service.ThrowAlways(
                "ExecuteMultiple", ExecuteMultipleResponseBuilder.Faulted("Batch create refused"));

            var outcome = NewShuffler().TestImportDataBlock(CreateInBatchesOf(10), Sources("Alpha", "Beta"));

            Assert.That(outcome.Created, Is.EqualTo(2), DumpAll());
            Assert.That(outcome.Failed, Is.EqualTo(0), DumpAll());
            Recorder.AssertLogged("Falling back to sequential creates");
            Assert.That(Service.Created.Count, Is.EqualTo(2), DumpAll());
        }

        /// <summary>
        /// A flush in the middle of the loop is inside the try, so the catch does fire - and with
        /// no label set it blames the record that filled the batch. Both records were lost here,
        /// and only the second one is named.
        /// </summary>
        [Test]
        public void A_mid_loop_flush_failure_is_blamed_on_the_record_that_filled_the_batch()
        {
            // On-prem: no bulk messages, so the batch goes out as ExecuteMultiple and that is
            // the rung whose catch rethrows under StopOnError.
            OnPrem().WithMetadata("account");
            Service.ThrowAlways(
                "ExecuteMultiple", ExecuteMultipleResponseBuilder.Faulted("Batch create refused"));

            var shuffler = NewShuffler(stopOnError: true);

            Assert.Throws<FaultException<OrganizationServiceFault>>(
                () => shuffler.TestImportDataBlock(
                    CreateInBatchesOf(2), Sources("Alpha", "Beta", "Gamma")),
                DumpAll());
            Recorder.AssertSent("*** Error record: Beta ***");
            Recorder.AssertNeverSent("*** Error record: Alpha ***");
        }

        #endregion Flushes that escape the block
    }
}
