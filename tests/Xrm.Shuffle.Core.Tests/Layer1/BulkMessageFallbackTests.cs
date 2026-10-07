namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer1
{
    using System;
    using System.Linq;
    using System.ServiceModel;
    using Cinteros.Crm.Utils.Shuffle;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.Messages;
    using NUnit.Framework;

    /// <summary>
    /// What happens when a bulk message is asked for and does not answer. There are two very
    /// different cases and the product must not confuse them: the message does not exist on
    /// this org, which is permanent and should be remembered; or the message exists and the
    /// batch failed, which says nothing about the next batch.
    /// </summary>
    /// <remarks>
    /// The second case also has to undo itself. CreateMultiple, UpdateMultiple and
    /// UpsertMultiple are each a single transaction, so a fault rolled the whole batch back
    /// and nothing was written - the rows have to be re-run one at a time, both to get the
    /// records ahead of the bad one committed and to name the one that actually failed.
    /// </remarks>
    [TestFixture]
    public class BulkMessageFallbackTests : ShuffleTestBase
    {
        private const string CreateMultiple = "CreateMultiple";
        private const string UpdateMultiple = "UpdateMultiple";
        private const string UpsertMultiple = "UpsertMultiple";
        private const string ExecuteMultiple = "ExecuteMultiple";

        private static Shuffler.TestCreateBatch Creates(int count)
        {
            var batch = new Shuffler.TestCreateBatch();
            for (var i = 1; i <= count; i++)
            {
                batch.Add(Record("account", Id(i), "Account " + i), Id(i), "account Acme " + i);
            }
            return batch;
        }

        private static Shuffler.TestUpdateBatch Updates(int count)
        {
            var batch = new Shuffler.TestUpdateBatch();
            for (var i = 1; i <= count; i++)
            {
                batch.Add(Record("account", Id(i), "Account " + i), "account Acme " + i);
            }
            return batch;
        }

        private static Shuffler.TestUpsertBatch Upserts(int count)
        {
            var batch = new Shuffler.TestUpsertBatch();
            for (var i = 1; i <= count; i++)
            {
                batch.Add(Record("account", Id(i), "Account " + i), Id(i), "account Acme " + i);
            }
            return batch;
        }

        private static ExecuteMultipleResponse TwoCreates()
        {
            return new ExecuteMultipleResponseBuilder().CreatedAt(Id(101)).CreatedAt(Id(102)).Build();
        }

        [Test]
        public void A_not_implemented_CreateMultiple_falls_back_to_ExecuteMultiple()
        {
            // 0x80040265 is the on-premises answer: the message is not on this org at all.
            Service.SupportsBulkMessage("account", CreateMultiple);
            Service.ThrowOnce(CreateMultiple, ExecuteMultipleResponseBuilder.MessageNotImplemented(CreateMultiple));
            Service.OnMessage(ExecuteMultiple, TwoCreates());

            var outcome = NewShuffler().TestFlushPendingCreates(Creates(2));

            Assert.That(outcome.Created, Is.EqualTo(2), outcome.ToString());
            Assert.That(outcome.Failed, Is.EqualTo(0), outcome.ToString());
            Assert.That(Service.CountOf(ExecuteMultiple), Is.EqualTo(1), DumpAll());
            Recorder.AssertLogged("CreateMultiple not implemented, marking as unsupported and falling back");
        }

        [Test]
        public void A_NotSupportedException_is_read_as_not_implemented_too()
        {
            // Some channels surface an absent message this way rather than as a fault.
            Service.SupportsBulkMessage("account", CreateMultiple);
            Service.ThrowOnce(CreateMultiple, new NotSupportedException("no such message"));
            Service.OnMessage(ExecuteMultiple, TwoCreates());

            NewShuffler().TestFlushPendingCreates(Creates(2));

            Assert.That(Service.CountOf(ExecuteMultiple), Is.EqualTo(1), DumpAll());
            Recorder.AssertLogged("CreateMultiple not implemented");
        }

        [Test]
        public void A_not_implemented_fault_wrapped_in_another_exception_is_still_found()
        {
            // The check walks InnerException, because the proxy layer wraps.
            Service.SupportsBulkMessage("account", CreateMultiple);
            Service.ThrowOnce(CreateMultiple, new InvalidOperationException(
                "wrapped", ExecuteMultipleResponseBuilder.MessageNotImplemented(CreateMultiple)));
            Service.OnMessage(ExecuteMultiple, TwoCreates());

            NewShuffler().TestFlushPendingCreates(Creates(2));

            Assert.That(Service.CountOf(ExecuteMultiple), Is.EqualTo(1), DumpAll());
            Recorder.AssertLogged("CreateMultiple not implemented");
        }

        [Test]
        public void An_org_that_answered_not_implemented_once_is_not_asked_again()
        {
            // Without the cache every batch for the rest of the run pays a doomed round trip.
            Service.SupportsBulkMessage("account", CreateMultiple);
            Service.ThrowOnce(CreateMultiple, ExecuteMultipleResponseBuilder.MessageNotImplemented(CreateMultiple));
            Service.OnMessage(ExecuteMultiple, TwoCreates());

            var shuffler = NewShuffler();
            shuffler.TestFlushPendingCreates(Creates(2));
            shuffler.TestFlushPendingCreates(Creates(2));

            Assert.That(Service.CountOf(CreateMultiple), Is.EqualTo(1), DumpAll());
            Assert.That(Service.CountOf(ExecuteMultiple), Is.EqualTo(2), DumpAll());
        }

        [Test]
        public void An_ordinary_CreateMultiple_fault_re_runs_the_rows_one_at_a_time()
        {
            // The batch was one transaction, so nothing was written. Going to ExecuteMultiple
            // instead would be wrong twice over: the message does exist, and the per-record
            // path is the only one that can name the row that faulted.
            Service.SupportsBulkMessage("account", CreateMultiple);
            Service.ThrowOnce(CreateMultiple, ExecuteMultipleResponseBuilder.Faulted("bad row somewhere"));
            var ids = new System.Collections.Generic.Queue<Guid>(new[] { Id(901), Id(902) });
            Service.OnCreate(e => ids.Dequeue());

            var outcome = NewShuffler().TestFlushPendingCreates(Creates(2));

            Assert.That(outcome.Created, Is.EqualTo(2), outcome.ToString());
            Assert.That(Service.CountOf(ExecuteMultiple), Is.EqualTo(0), DumpAll());
            Assert.That(Service.Created.Count, Is.EqualTo(2), DumpAll());
            Recorder.AssertLogged("CreateMultiple batch failed, falling back to individual creates");
            Recorder.AssertNeverLogged("marking as unsupported");
        }

        [Test]
        public void The_row_that_faulted_the_batch_is_named_by_the_re_run()
        {
            Service.SupportsBulkMessage("account", CreateMultiple);
            Service.ThrowOnce(CreateMultiple, ExecuteMultipleResponseBuilder.Faulted("bad row somewhere"));
            Service.OnCreate(Refuse(Id(2), "duplicate name"));

            var outcome = NewShuffler().TestFlushPendingCreates(Creates(3));

            Assert.That(outcome.Created, Is.EqualTo(2), outcome.ToString());
            Assert.That(outcome.Failed, Is.EqualTo(1), outcome.ToString());
            Recorder.AssertSent("002 Create Failed: account Acme 2 duplicate name");
            Recorder.AssertSent("003 Created: account Acme 3");
        }

        [Test]
        public void StopOnError_does_not_stop_the_re_run_from_starting()
        {
            // Deliberate: with the batch rolled back, refusing to re-run would abort the
            // import without ever saying which record was at fault. The per-record path
            // honours StopOnError itself, once it has named the row.
            Service.SupportsBulkMessage("account", CreateMultiple);
            Service.ThrowOnce(CreateMultiple, ExecuteMultipleResponseBuilder.Faulted("bad row somewhere"));
            Service.OnCreate(Refuse(Id(2), "duplicate name"));

            var shuffler = NewShuffler(stopOnError: true);

            Assert.Throws<InvalidOperationException>(() => shuffler.TestFlushPendingCreates(Creates(3)));
            Recorder.AssertSent("002 Create Failed: account Acme 2 duplicate name");
            Recorder.AssertNeverSent("003 Created: account Acme 3");
            Assert.That(shuffler.TestBatchFailureLabel, Is.EqualTo("002 account Acme 2"));
        }

        [Test]
        public void A_not_implemented_UpdateMultiple_falls_back_to_ExecuteMultiple()
        {
            Service.SupportsBulkMessage("account", UpdateMultiple);
            Service.ThrowOnce(UpdateMultiple, ExecuteMultipleResponseBuilder.MessageNotImplemented(UpdateMultiple));
            Service.OnMessage(ExecuteMultiple, new ExecuteMultipleResponseBuilder().Succeeded().Succeeded().Build());

            var outcome = NewShuffler().TestFlushPendingUpdates(Updates(2));

            Assert.That(outcome.Updated, Is.EqualTo(2), outcome.ToString());
            Assert.That(Service.CountOf(ExecuteMultiple), Is.EqualTo(1), DumpAll());
            Recorder.AssertLogged("UpdateMultiple not implemented");
        }

        [Test]
        public void An_ordinary_UpdateMultiple_fault_re_runs_the_rows_one_at_a_time()
        {
            Service.SupportsBulkMessage("account", UpdateMultiple);
            Service.ThrowOnce(UpdateMultiple, ExecuteMultipleResponseBuilder.Faulted("bad row somewhere"));

            var outcome = NewShuffler().TestFlushPendingUpdates(Updates(2));

            Assert.That(outcome.Updated, Is.EqualTo(2), outcome.ToString());
            Assert.That(Service.CountOf(ExecuteMultiple), Is.EqualTo(0), DumpAll());
            Assert.That(Service.Updated.Count, Is.EqualTo(2), DumpAll());
            Recorder.AssertLogged("UpdateMultiple batch failed, falling back to individual updates");
        }

        [Test]
        public void A_not_implemented_UpsertMultiple_drops_to_ExecuteMultiple_carrying_Upsert_requests()
        {
            // Upsert has one more rung than create and update: a batch of single Upserts
            // before it gives up and does Create/Update per record.
            Service.SupportsBulkMessage("account", UpsertMultiple, "Upsert");
            Service.ThrowOnce(UpsertMultiple, ExecuteMultipleResponseBuilder.MessageNotImplemented(UpsertMultiple));
            Service.OnMessage(ExecuteMultiple, new ExecuteMultipleResponseBuilder()
                .Success(Upserted(true, Id(901)))
                .Success(Upserted(false, Id(2)))
                .Build());

            var outcome = NewShuffler().TestFlushPendingUpserts(Upserts(2));

            Assert.That(outcome.Created, Is.EqualTo(1), outcome.ToString());
            Assert.That(outcome.Updated, Is.EqualTo(1), outcome.ToString());
            var inner = (ExecuteMultipleRequest)Service.RequestsNamed(ExecuteMultiple).Single();
            Assert.That(inner.Requests.All(r => r is UpsertRequest), Is.True, DumpAll());
            Recorder.AssertLogged("UpsertMultiple not implemented");
        }

        [Test]
        public void An_ordinary_UpsertMultiple_fault_retries_as_a_batch_of_single_upserts()
        {
            // Unlike create and update, this one does not drop to per-record work: Upsert is
            // idempotent, so the cheaper rung is tried first and Create/Update is left as the
            // last resort.
            Service.SupportsBulkMessage("account", UpsertMultiple, "Upsert");
            Service.ThrowOnce(UpsertMultiple, ExecuteMultipleResponseBuilder.Faulted("bad row somewhere"));
            Service.OnMessage(ExecuteMultiple, new ExecuteMultipleResponseBuilder()
                .Success(Upserted(true, Id(901)))
                .Success(Upserted(true, Id(902)))
                .Build());

            var outcome = NewShuffler().TestFlushPendingUpserts(Upserts(2));

            Assert.That(outcome.Created, Is.EqualTo(2), outcome.ToString());
            Assert.That(Service.CountOf(ExecuteMultiple), Is.EqualTo(1), DumpAll());
            Assert.That(Service.Created.Count, Is.EqualTo(0), "Create/Update is the rung below this one");
            Recorder.AssertLogged("UpsertMultiple batch failed, falling back to ExecuteMultiple with Upsert");
        }

        [Test]
        public void Known_defect_a_late_not_implemented_upsert_fault_counts_the_rows_ahead_of_it_twice()
        {
            // TryFlushUpsertsWithExecuteMultiple can discover "Upsert not implemented" from a
            // response item partway down the batch and return false - but the rows before it
            // have already been counted, and the caller then re-runs the whole batch through
            // Create/Update. Two records go in, three are reported.
            //
            // Asserted as it behaves today, deliberately. A fix should either roll the
            // counters back or finish the batch, and either way this test should then fail
            // and be rewritten rather than quietly keep passing.
            Service.SupportsBulkMessage("account", "Upsert");
            Service.OnMessage(ExecuteMultiple, new ExecuteMultipleResponseBuilder()
                .Success(Upserted(true, Id(901)))
                .Fault("not implemented here", unchecked((int)0x80040265))
                .Build());
            Service.OnCreate(e => Id(902));

            var outcome = NewShuffler().TestFlushPendingUpserts(Upserts(2));

            Assert.That(outcome.Created, Is.EqualTo(3),
                "known defect: the first row is counted by both the upsert rung and the re-run");
            Assert.That(Service.Created.Count, Is.EqualTo(2), "only two records were actually written");
        }

        /// <summary>A create handler that refuses one record and accepts the rest.</summary>
        private static Func<Entity, Guid> Refuse(Guid id, string message)
        {
            return entity =>
            {
                if (entity.Id == id)
                {
                    throw new InvalidOperationException(message);
                }
                return Id(901);
            };
        }

        private static UpsertResponse Upserted(bool recordCreated, Guid id)
        {
            var response = new UpsertResponse();
            response.Results["RecordCreated"] = recordCreated;
            response.Results["Target"] = new EntityReference("account", id);
            return response;
        }
    }
}
