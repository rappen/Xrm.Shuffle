namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer1
{
    using System;
    using System.Linq;
    using Cinteros.Crm.Utils.Shuffle;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// How the ExecuteMultiple rungs pair response items back to the requests that produced
    /// them. This is where the counters come from, so a pairing bug shows up as a run that
    /// reports more work than it did.
    /// </summary>
    /// <remarks>
    /// These go straight at the ExecuteMultiple rung rather than through the dispatcher, so
    /// that no capability probe has to be scripted and each test is about one thing. The
    /// dispatcher's own routing is covered separately.
    /// </remarks>
    [TestFixture]
    public class BatchResponsePairingTests : ShuffleTestBase
    {
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

        [Test]
        public void One_fault_in_a_batch_of_twenty_is_one_failure_and_nineteen_successes()
        {
            // The regression behind 4f9233d: the fault used to end the accounting, so the
            // records after it were never counted at all and the totals did not add up.
            var responses = new ExecuteMultipleResponseBuilder();
            for (var i = 0; i < 20; i++)
            {
                if (i == 11)
                {
                    responses.Fault("Nope");
                }
                else
                {
                    responses.CreatedAt(Id(100 + i));
                }
            }
            Service.OnMessage(ExecuteMultiple, responses.Build());

            var outcome = NewShuffler().TestFlushCreatesWithExecuteMultiple(Creates(20));

            Assert.That(outcome.Created, Is.EqualTo(19), outcome.ToString());
            Assert.That(outcome.Failed, Is.EqualTo(1), outcome.ToString());
            Assert.That(outcome.Accounted, Is.EqualTo(20), "every request must be accounted for exactly once");
            Recorder.AssertSent("012 Create Failed: account Acme 12 Nope");
            Recorder.AssertSent("013 Created: account Acme 13");
        }

        [Test]
        public void Responses_are_matched_by_request_index_not_by_position()
        {
            // The platform is free to return items in any order; the loop looks them up by
            // RequestIndex for exactly that reason.
            Service.OnMessage(ExecuteMultiple, new ExecuteMultipleResponseBuilder()
                .SuccessAt(2, CreateResponseFor(Id(30)))
                .FaultAt(0, "First one failed")
                .SuccessAt(1, CreateResponseFor(Id(20)))
                .Build());

            var batch = Creates(3);
            var outcome = NewShuffler().TestFlushCreatesWithExecuteMultiple(batch);

            Assert.That(outcome.Created, Is.EqualTo(2), outcome.ToString());
            Assert.That(outcome.Failed, Is.EqualTo(1), outcome.ToString());
            Recorder.AssertSent("001 Create Failed: account Acme 1 First one failed");
            Assert.That(batch.Entities[1].Id, Is.EqualTo(Id(20)), "the id must come from the matching response");
            Assert.That(batch.Entities[2].Id, Is.EqualTo(Id(30)));
        }

        [Test]
        public void Requests_the_platform_never_answered_count_as_failures_not_successes()
        {
            // ContinueOnError=false stops the platform at the first fault, so the collection
            // is shorter than the request list. Nothing after the hole was executed.
            Service.OnMessage(ExecuteMultiple, new ExecuteMultipleResponseBuilder()
                .CreatedAt(Id(10))
                .Fault("Bad row")
                .Omit()
                .Omit()
                .Build());

            var outcome = NewShuffler().TestFlushCreatesWithExecuteMultiple(Creates(4));

            Assert.That(outcome.Created, Is.EqualTo(1), outcome.ToString());
            Assert.That(outcome.Failed, Is.EqualTo(3), outcome.ToString());
            Assert.That(outcome.Accounted, Is.EqualTo(4));
            Recorder.AssertSent("003 Create Not Executed: account Acme 3");
            Recorder.AssertSent("004 Create Not Executed: account Acme 4");
        }

        [Test]
        public void A_created_record_takes_the_id_the_platform_returned()
        {
            Service.OnMessage(ExecuteMultiple, new ExecuteMultipleResponseBuilder()
                .CreatedAt(Id(901))
                .CreatedAt(Id(902))
                .Build());

            var batch = Creates(2);
            var outcome = NewShuffler().TestFlushCreatesWithExecuteMultiple(batch);

            Assert.That(batch.Entities[0].Id, Is.EqualTo(Id(901)));
            Assert.That(batch.Entities[1].Id, Is.EqualTo(Id(902)));
            Assert.That(outcome.References.Select(r => r.Id).ToArray(), Is.EqualTo(new[] { Id(901), Id(902) }));
        }

        [Test]
        public void A_batch_create_that_throws_falls_back_to_one_create_per_record()
        {
            Service.OnMessage(ExecuteMultiple, request =>
            {
                throw ExecuteMultipleResponseBuilder.Faulted("Batch too large");
            });
            Service.OnCreate(entity => Id(500));

            var outcome = NewShuffler().TestFlushCreatesWithExecuteMultiple(Creates(3));

            Assert.That(outcome.Created, Is.EqualTo(3), outcome.ToString());
            Assert.That(Service.CountOf("Create"), Is.EqualTo(3), "one individual create per record");
            Recorder.AssertLogged("Falling back to sequential creates");
        }

        [Test]
        public void A_batch_create_that_throws_under_StopOnError_does_not_fall_back()
        {
            Service.OnMessage(ExecuteMultiple, request =>
            {
                throw ExecuteMultipleResponseBuilder.Faulted("Batch too large");
            });

            var shuffler = NewShuffler(stopOnError: true);
            var batch = Creates(3);

            Assert.Throws<System.ServiceModel.FaultException<OrganizationServiceFault>>(
                () => shuffler.TestFlushCreatesWithExecuteMultiple(batch));
            Assert.That(Service.CountOf("Create"), Is.EqualTo(0), "StopOnError must not retry the records one by one");
        }

        [Test]
        public void A_fault_under_StopOnError_aborts_the_batch_and_names_the_record()
        {
            Service.OnMessage(ExecuteMultiple, new ExecuteMultipleResponseBuilder()
                .CreatedAt(Id(10))
                .Fault("Bad row")
                .CreatedAt(Id(30))
                .Build());

            var shuffler = NewShuffler(stopOnError: true);

            Assert.Throws<InvalidOperationException>(() => shuffler.TestFlushCreatesWithExecuteMultiple(Creates(3)));
            Assert.That(shuffler.TestBatchFailureLabel, Is.EqualTo("002 account Acme 2"));
            Recorder.AssertLogged("StopOnError: aborting, 1 record(s) in this batch were not executed");
            Recorder.AssertNeverSent("003 Created: account Acme 3");
        }

        private static OrganizationResponse CreateResponseFor(Guid id)
        {
            var response = new Microsoft.Xrm.Sdk.Messages.CreateResponse();
            response.Results["id"] = id;
            return response;
        }
    }
}
