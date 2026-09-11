namespace Cinteros.Crm.Utils.Shuffle.Tests.Regressions
{
    using System.ServiceModel;
    using Cinteros.Crm.Utils.Shuffle;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// Which record a batch failure is blamed on, and the lowest rung of the upsert chain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without batching a failure is reported by the record loop, which knows exactly which
    /// record it was working on. A batch breaks that: the loop is several records past the one
    /// that faulted by the time the platform answers, so the catch would name whichever record
    /// happened to fill the batch. StopOnBatchError exists to carry the right label out of the
    /// flush and into that catch.
    /// </para>
    /// <para>
    /// The other half of this fixture is FlushUpsertsAsCreateUpdate, the rung reached when the
    /// target has no Upsert message at all. It has to work out create-versus-update from the
    /// fault a create came back with, which is guesswork against error codes and message text.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class BatchErrorReportingTests : ShuffleTestBase
    {
        private const int DuplicateRecordEntityKey = -2147220937;
        private const int DuplicateRecord = -2147220685;
        private const int SomethingElse = -2147220989;

        private static Shuffler.TestUpsertBatch Upserts(int count)
        {
            var batch = new Shuffler.TestUpsertBatch();
            for (var i = 1; i <= count; i++)
            {
                batch.Add(Record("account", Id(i), "Account " + i), Id(i), "account Acme " + i);
            }
            return batch;
        }

        private static FaultException<OrganizationServiceFault> Fault(string message, int errorCode)
        {
            return new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { Message = message, ErrorCode = errorCode },
                message);
        }

        #region The label a batch failure is reported under

        [Test]
        public void The_label_is_the_position_and_the_identifier_of_the_record_that_failed()
        {
            var shuffler = NewShuffler(stopOnError: true);

            Assert.That(shuffler.TestStopOnBatchError(7, "Acme Corp"), Is.True);
            Assert.That(shuffler.TestBatchFailureLabel, Is.EqualTo("007 Acme Corp"));
        }

        /// <summary>
        /// The position is padded the same way the log lines are, so the label reads like the
        /// line the record would have produced had it succeeded.
        /// </summary>
        [Test]
        public void The_position_is_padded_to_three_digits_but_not_truncated()
        {
            var shuffler = NewShuffler(stopOnError: true);

            shuffler.TestStopOnBatchError(3, "early");
            Assert.That(shuffler.TestBatchFailureLabel, Is.EqualTo("003 early"));

            shuffler.TestStopOnBatchError(1234, "late");
            Assert.That(shuffler.TestBatchFailureLabel, Is.EqualTo("1234 late"));
        }

        /// <summary>
        /// Without StopOnError there is nothing to report: the flush carries on and each failed
        /// record has already been logged under its own number.
        /// </summary>
        [Test]
        public void Without_StopOnError_nothing_is_labelled_and_the_caller_is_told_to_continue()
        {
            var shuffler = NewShuffler();

            Assert.That(shuffler.TestStopOnBatchError(7, "Acme Corp"), Is.False);
            Assert.That(shuffler.TestBatchFailureLabel, Is.Null);
        }

        #endregion The label a batch failure is reported under

        #region The lowest upsert rung: create, then update if it was already there

        [Test]
        public void Every_record_is_created_when_none_of_them_exists()
        {
            Service.OnCreate(entity => Id(500));

            var outcome = NewShuffler().TestFlushUpsertsAsCreateUpdate(Upserts(2));

            Assert.That(outcome.Created, Is.EqualTo(2), outcome.ToString());
            Assert.That(outcome.Updated, Is.EqualTo(0), outcome.ToString());
            Recorder.AssertLogged("Falling back to Create/Update for 2 records (Upsert not available)");
            Recorder.AssertSent("001 Created: account Acme 1");
        }

        [Test]
        public void A_duplicate_key_fault_is_read_as_already_there_and_becomes_an_update()
        {
            Service.OnCreate(entity =>
            {
                throw Fault("A record with these values exists", DuplicateRecordEntityKey);
            });

            var outcome = NewShuffler().TestFlushUpsertsAsCreateUpdate(Upserts(2));

            Assert.That(outcome.Updated, Is.EqualTo(2), outcome.ToString());
            Assert.That(outcome.Created, Is.EqualTo(0), outcome.ToString());
            Assert.That(Service.CountOf("Update"), Is.EqualTo(2), DumpAll());
            Recorder.AssertSent("001 Updated: account Acme 1");
        }

        [Test]
        public void The_other_duplicate_error_code_is_read_the_same_way()
        {
            Service.OnCreate(entity =>
            {
                throw Fault("Duplicate detected by a rule", DuplicateRecord);
            });

            var outcome = NewShuffler().TestFlushUpsertsAsCreateUpdate(Upserts(2));

            Assert.That(outcome.Updated, Is.EqualTo(2), outcome.ToString());
        }

        /// <summary>
        /// A fault with no recognised error code still counts as already there when its message
        /// says so, which is what keeps the rung working against targets that report duplicates
        /// through text rather than a code.
        /// </summary>
        [Test]
        public void A_fault_whose_message_says_it_already_exists_is_also_an_update()
        {
            Service.OnCreate(entity =>
            {
                throw Fault("The record already exists", SomethingElse);
            });

            var outcome = NewShuffler().TestFlushUpsertsAsCreateUpdate(Upserts(2));

            Assert.That(outcome.Updated, Is.EqualTo(2), outcome.ToString());
        }

        [Test]
        public void The_word_duplicate_in_lower_case_is_enough_on_its_own()
        {
            Service.OnCreate(entity =>
            {
                throw Fault("rejected as a duplicate of another record", SomethingElse);
            });

            var outcome = NewShuffler().TestFlushUpsertsAsCreateUpdate(Upserts(2));

            Assert.That(outcome.Updated, Is.EqualTo(2), outcome.ToString());
        }

        /// <summary>
        /// Known defect. The message check is case sensitive, so a target that capitalises
        /// Duplicate falls past it and the record is reported as a create failure even though
        /// it exists and an update would have worked.
        /// </summary>
        /// <remarks>
        /// The error codes cover the platform messages, so this is only reached by a target or
        /// a plugin that raises its own text. Fixing it means comparing case insensitively, at
        /// which point this test should fail and be rewritten as an update.
        /// </remarks>
        [Test]
        public void Known_defect_a_capitalised_Duplicate_message_is_not_recognised()
        {
            Service.OnCreate(entity =>
            {
                throw Fault("Duplicate record found", SomethingElse);
            });

            var outcome = NewShuffler().TestFlushUpsertsAsCreateUpdate(Upserts(2));

            Assert.That(outcome.Failed, Is.EqualTo(2), outcome.ToString());
            Assert.That(outcome.Updated, Is.EqualTo(0), "an update was never attempted");
            Assert.That(Service.CountOf("Update"), Is.EqualTo(0), DumpAll());
            Recorder.AssertSent("001 Create Failed: account Acme 1 Duplicate record found");
        }

        [Test]
        public void A_create_that_fails_for_any_other_reason_is_a_failure()
        {
            Service.OnCreate(entity =>
            {
                throw Fault("Privilege denied", SomethingElse);
            });

            var outcome = NewShuffler().TestFlushUpsertsAsCreateUpdate(Upserts(2));

            Assert.That(outcome.Failed, Is.EqualTo(2), outcome.ToString());
            Assert.That(Service.CountOf("Update"), Is.EqualTo(0), DumpAll());
        }

        /// <summary>
        /// The record exists but the update fails too - reported under its own message so the
        /// log says which of the two calls went wrong.
        /// </summary>
        [Test]
        public void An_update_that_fails_after_a_duplicate_create_is_reported_as_an_update_failure()
        {
            Service.OnCreate(entity =>
            {
                throw Fault("duplicate", DuplicateRecord);
            });
            Service.OnUpdate(entity =>
            {
                throw Fault("Read only field", SomethingElse);
            });

            var outcome = NewShuffler().TestFlushUpsertsAsCreateUpdate(Upserts(2));

            Assert.That(outcome.Failed, Is.EqualTo(2), outcome.ToString());
            Assert.That(outcome.Updated, Is.EqualTo(0), outcome.ToString());
            Recorder.AssertSent("001 Update Failed (fallback): account Acme 1 Read only field");
        }

        /// <summary>
        /// StopOnError stops at the first record and says how many of the batch never ran, so
        /// the run can be resumed without guessing where it stopped.
        /// </summary>
        [Test]
        public void StopOnError_abandons_the_rest_of_the_batch_and_says_how_many_were_left()
        {
            Service.OnCreate(entity =>
            {
                throw Fault("Privilege denied", SomethingElse);
            });
            var shuffler = NewShuffler(stopOnError: true);

            Assert.Throws<FaultException<OrganizationServiceFault>>(
                () => shuffler.TestFlushUpsertsAsCreateUpdate(Upserts(3)));

            Assert.That(shuffler.TestBatchFailureLabel, Is.EqualTo("001 account Acme 1"));
            Recorder.AssertLogged("StopOnError: aborting, 2 record(s) in this batch were not executed");
            Assert.That(Service.CountOf("Create"), Is.EqualTo(1), "the two records behind it never ran");
        }

        #endregion The lowest upsert rung: create, then update if it was already there
    }
}
