namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer1
{
    using System;
    using System.Collections.Generic;
    using Cinteros.Crm.Utils.Shuffle;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// The same response pairing for the update and delete rungs. They are separate loops in
    /// the product with their own log wording, so they need their own tests rather than a
    /// parameterised sweep - a copy-paste slip between them is exactly what this catches.
    /// </summary>
    [TestFixture]
    public class UpdateAndDeletePairingTests : ShuffleTestBase
    {
        private const string ExecuteMultiple = "ExecuteMultiple";

        private static Shuffler.TestUpdateBatch Updates(int count)
        {
            var batch = new Shuffler.TestUpdateBatch();
            for (var i = 1; i <= count; i++)
            {
                batch.Add(Record("account", Id(i), "Account " + i), "account Acme " + i);
            }
            return batch;
        }

        private static List<Entity> Deletes(int count)
        {
            var batch = new List<Entity>();
            for (var i = 1; i <= count; i++)
            {
                batch.Add(Record("account", Id(i)));
            }
            return batch;
        }

        [Test]
        public void An_update_fault_is_one_failure_and_the_rest_still_count()
        {
            Service.OnMessage(ExecuteMultiple, new ExecuteMultipleResponseBuilder()
                .Succeeded()
                .Fault("Bad row")
                .Succeeded()
                .Build());

            var outcome = NewShuffler().TestFlushUpdatesWithExecuteMultiple(Updates(3));

            Assert.That(outcome.Updated, Is.EqualTo(2), outcome.ToString());
            Assert.That(outcome.Failed, Is.EqualTo(1), outcome.ToString());
            Recorder.AssertSent("002 Update Failed: account Acme 2 account Bad row");
            Recorder.AssertSent("003 Updated: account Acme 3");
        }

        [Test]
        public void Unanswered_updates_are_reported_as_not_executed()
        {
            Service.OnMessage(ExecuteMultiple, new ExecuteMultipleResponseBuilder()
                .Succeeded()
                .Omit()
                .Omit()
                .Build());

            var outcome = NewShuffler().TestFlushUpdatesWithExecuteMultiple(Updates(3));

            Assert.That(outcome.Updated, Is.EqualTo(1), outcome.ToString());
            Assert.That(outcome.Failed, Is.EqualTo(2), outcome.ToString());
            Recorder.AssertSent("002 Update Not Executed: account Acme 2 account");
        }

        [Test]
        public void A_batch_update_that_throws_falls_back_to_one_update_per_record()
        {
            Service.OnMessage(ExecuteMultiple, request =>
            {
                throw ExecuteMultipleResponseBuilder.Faulted("Batch too large");
            });

            var outcome = NewShuffler().TestFlushUpdatesWithExecuteMultiple(Updates(3));

            Assert.That(outcome.Updated, Is.EqualTo(3), outcome.ToString());
            Assert.That(Service.CountOf("Update"), Is.EqualTo(3));
            Recorder.AssertLogged("Falling back to sequential updates");
        }

        [Test]
        public void A_delete_of_a_record_that_is_already_gone_is_not_a_failure()
        {
            // Deleting what is not there is the expected state, not an error - the import is
            // being asked to make the record absent, and it is.
            Service.OnMessage(ExecuteMultiple, new ExecuteMultipleResponseBuilder()
                .Succeeded()
                .Fault("account With Id = ... Does Not Exist")
                .Build());

            var outcome = NewShuffler().TestFlushPendingDeletes(Deletes(2));

            Assert.That(outcome.Deleted, Is.EqualTo(1), outcome.ToString());
            Assert.That(outcome.Failed, Is.EqualTo(0), outcome.ToString());
            Recorder.AssertSent("      ...already deleted");
        }

        [Test]
        public void A_real_delete_fault_is_counted_and_named()
        {
            Service.OnMessage(ExecuteMultiple, new ExecuteMultipleResponseBuilder()
                .Succeeded()
                .Fault("Cannot delete, still referenced")
                .Build());

            var outcome = NewShuffler().TestFlushPendingDeletes(Deletes(2));

            Assert.That(outcome.Deleted, Is.EqualTo(1), outcome.ToString());
            Assert.That(outcome.Failed, Is.EqualTo(1), outcome.ToString());
            Recorder.AssertSent("Delete Failed: account Cannot delete, still referenced");
        }

        [Test]
        public void Unanswered_deletes_are_reported_as_not_executed()
        {
            Service.OnMessage(ExecuteMultiple, new ExecuteMultipleResponseBuilder()
                .Succeeded()
                .Omit()
                .Build());

            var outcome = NewShuffler().TestFlushPendingDeletes(Deletes(2));

            Assert.That(outcome.Deleted, Is.EqualTo(1), outcome.ToString());
            Assert.That(outcome.Failed, Is.EqualTo(1), outcome.ToString());
            Recorder.AssertSent("Delete Not Executed: account");
        }

        [Test]
        public void A_batch_delete_that_throws_falls_back_to_one_delete_per_record()
        {
            Service.OnMessage(ExecuteMultiple, request =>
            {
                throw ExecuteMultipleResponseBuilder.Faulted("Batch too large");
            });

            var outcome = NewShuffler().TestFlushPendingDeletes(Deletes(3));

            Assert.That(outcome.Deleted, Is.EqualTo(3), outcome.ToString());
            Assert.That(Service.CountOf("Delete"), Is.EqualTo(3));
            Recorder.AssertLogged("Falling back to sequential deletes");
        }

        [Test]
        public void A_single_record_batch_never_reaches_ExecuteMultiple()
        {
            // Every dispatcher short-circuits at one record. A fixture that forgets this
            // scripts a batch response that is never asked for.
            var outcome = NewShuffler().TestFlushPendingDeletes(Deletes(1));

            Assert.That(outcome.Deleted, Is.EqualTo(1));
            Assert.That(Service.CountOf(ExecuteMultiple), Is.EqualTo(0));
            Assert.That(Service.CountOf("Delete"), Is.EqualTo(1));
        }
    }
}
