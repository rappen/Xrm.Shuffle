namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer1
{
    using System;
    using System.Linq;
    using Cinteros.Crm.Utils.Shuffle;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.Messages;
    using NUnit.Framework;

    /// <summary>
    /// Which message a batch actually goes out as. The dispatchers pick between the bulk
    /// message, ExecuteMultiple and one request per record, and the choice is invisible from
    /// the counters - a batch that quietly took the slow path reports exactly the same totals
    /// as one that took the fast one. So every test here asserts the routing it expected.
    /// </summary>
    [TestFixture]
    public class BulkMessageRoutingTests : ShuffleTestBase
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

        private static OrganizationResponse CreateMultipleReturning(params Guid[] ids)
        {
            var response = new OrganizationResponse();
            response.Results["Ids"] = ids;
            return response;
        }

        [Test]
        public void A_supported_entity_creates_through_CreateMultiple_and_never_reaches_ExecuteMultiple()
        {
            Service.SupportsBulkMessage("account", CreateMultiple);
            Service.OnMessage(CreateMultiple, CreateMultipleReturning(Id(101), Id(102), Id(103)));

            var outcome = NewShuffler().TestFlushPendingCreates(Creates(3));

            Assert.That(outcome.Created, Is.EqualTo(3), outcome.ToString());
            Assert.That(Service.CountOf(CreateMultiple), Is.EqualTo(1), DumpAll());
            Assert.That(Service.CountOf(ExecuteMultiple), Is.EqualTo(0), DumpAll());
            Assert.That(Service.Created.Count, Is.EqualTo(0), "nothing should have gone out one at a time");
        }

        [Test]
        public void One_CreateMultiple_carries_every_record_in_the_batch()
        {
            Service.SupportsBulkMessage("account", CreateMultiple);
            Service.OnMessage(CreateMultiple, CreateMultipleReturning(Id(101), Id(102), Id(103)));

            NewShuffler().TestFlushPendingCreates(Creates(3));

            var targets = (EntityCollection)Service.RequestsNamed(CreateMultiple).Single()["Targets"];
            Assert.That(targets.EntityName, Is.EqualTo("account"));
            Assert.That(targets.Entities.Count, Is.EqualTo(3));
        }

        [Test]
        public void The_ids_CreateMultiple_returns_are_written_back_onto_the_records()
        {
            // The platform allocates the ids, so the batch has to take them back or every
            // later block that points at these records maps to the wrong row.
            Service.SupportsBulkMessage("account", CreateMultiple);
            Service.OnMessage(CreateMultiple, CreateMultipleReturning(Id(901), Id(902)));

            var batch = Creates(2);

            // Held onto before the flush: the dispatcher clears the batch once it has flushed,
            // so reading batch.Entities afterwards yields nothing. The records themselves are
            // the same objects, and it is their Id the batch writes to.
            var records = batch.Entities;
            var outcome = NewShuffler().TestFlushPendingCreates(batch);

            Assert.That(records.Select(e => e.Id).ToArray(), Is.EqualTo(new[] { Id(901), Id(902) }));
            Assert.That(outcome.References.Select(r => r.Id).ToArray(), Is.EqualTo(new[] { Id(901), Id(902) }));
        }

        [Test]
        public void An_entity_without_CreateMultiple_goes_straight_to_ExecuteMultiple()
        {
            Service.OnMessage(ExecuteMultiple, new ExecuteMultipleResponseBuilder()
                .CreatedAt(Id(101)).CreatedAt(Id(102)).Build());

            var outcome = NewShuffler().TestFlushPendingCreates(Creates(2));

            Assert.That(outcome.Created, Is.EqualTo(2), outcome.ToString());
            Assert.That(Service.CountOf(CreateMultiple), Is.EqualTo(0), DumpAll());
            Assert.That(Service.CountOf(ExecuteMultiple), Is.EqualTo(1), DumpAll());
        }

        [Test]
        public void A_supported_entity_updates_through_UpdateMultiple()
        {
            Service.SupportsBulkMessage("account", UpdateMultiple);
            Service.OnMessage(UpdateMultiple, new OrganizationResponse());

            var outcome = NewShuffler().TestFlushPendingUpdates(Updates(3));

            Assert.That(outcome.Updated, Is.EqualTo(3), outcome.ToString());
            Assert.That(Service.CountOf(UpdateMultiple), Is.EqualTo(1), DumpAll());
            Assert.That(Service.CountOf(ExecuteMultiple), Is.EqualTo(0), DumpAll());
        }

        [Test]
        public void Upsert_prefers_UpsertMultiple_over_the_single_Upsert_rung()
        {
            // Both are probed, UpsertMultiple first. An org that has gained both must not
            // fall back to a batch of single Upserts.
            Service.SupportsBulkMessage("account", UpsertMultiple, "Upsert");
            Service.OnMessage(UpsertMultiple, UpsertMultipleReturning(
                UpsertResult(true, Id(901)),
                UpsertResult(false, Id(2))));

            var outcome = NewShuffler().TestFlushPendingUpserts(Upserts(2));

            Assert.That(outcome.Created, Is.EqualTo(1), outcome.ToString());
            Assert.That(outcome.Updated, Is.EqualTo(1), outcome.ToString());
            Assert.That(Service.CountOf(UpsertMultiple), Is.EqualTo(1), DumpAll());
            Assert.That(Service.CountOf(ExecuteMultiple), Is.EqualTo(0), DumpAll());
            Recorder.AssertSent("001 Created (upsert): account Acme 1");
            Recorder.AssertSent("002 Updated (upsert): account Acme 2");
        }

        [Test]
        public void An_upsert_result_the_platform_left_out_is_counted_as_an_update()
        {
            // Not knowing is not the same as failing: the row went in either way, and
            // "Upserted" is the wording that says which of the two counters is a guess.
            Service.SupportsBulkMessage("account", UpsertMultiple);
            Service.OnMessage(UpsertMultiple, UpsertMultipleReturning(UpsertResult(true, Id(901))));

            var outcome = NewShuffler().TestFlushPendingUpserts(Upserts(2));

            Assert.That(outcome.Created, Is.EqualTo(1), outcome.ToString());
            Assert.That(outcome.Updated, Is.EqualTo(1), outcome.ToString());
            Assert.That(outcome.Failed, Is.EqualTo(0), outcome.ToString());
            Recorder.AssertSent("002 Upserted: account Acme 2");
        }

        [Test]
        public void An_entity_with_neither_upsert_message_falls_all_the_way_to_create_and_update()
        {
            Service.OnCreate(e => Id(901));

            var outcome = NewShuffler().TestFlushPendingUpserts(Upserts(2));

            Assert.That(Service.CountOf(UpsertMultiple), Is.EqualTo(0), DumpAll());
            Assert.That(Service.CountOf(ExecuteMultiple), Is.EqualTo(0), DumpAll());
            Assert.That(outcome.Accounted, Is.EqualTo(2), outcome.ToString());
        }

        [Test]
        public void The_capability_probe_runs_once_per_entity_and_message()
        {
            // It is a RetrieveMultiple against sdkmessagefilter, so an uncached probe would
            // cost one extra round trip per batch for the whole run.
            Service.SupportsBulkMessage("account", CreateMultiple);
            Service.OnMessage(CreateMultiple, CreateMultipleReturning(Id(101), Id(102)));

            var shuffler = NewShuffler();
            shuffler.TestFlushPendingCreates(Creates(2));
            shuffler.TestFlushPendingCreates(Creates(2));

            Assert.That(Service.CountOf(CreateMultiple), Is.EqualTo(2), DumpAll());
            Assert.That(Service.Probes.Count, Is.EqualTo(1),
                "the second batch should have read the cached answer: " +
                string.Join(", ", Service.Probes.Select(p => p.Item2 + "/" + p.Item1)));
        }

        [Test]
        public void A_probe_that_cannot_be_answered_is_treated_as_no_rather_than_failing_the_import()
        {
            // A locked-down org can refuse the sdkmessagefilter query outright. That is a
            // reason to use the slow path, not a reason to abandon the import.
            Service.FailTheCapabilityProbe(new InvalidOperationException("no privilege"));
            Service.OnMessage(ExecuteMultiple, new ExecuteMultipleResponseBuilder()
                .CreatedAt(Id(101)).CreatedAt(Id(102)).Build());

            var outcome = NewShuffler().TestFlushPendingCreates(Creates(2));

            Assert.That(outcome.Created, Is.EqualTo(2), outcome.ToString());
            Assert.That(Service.CountOf(ExecuteMultiple), Is.EqualTo(1), DumpAll());
            Recorder.AssertLogged("Failed to check CreateMultiple support for account");
        }

        [Test]
        public void A_single_record_batch_never_probes_for_bulk_support()
        {
            // The Count == 1 shortcut is ahead of the probe, which is what makes BatchSize=1
            // cost nothing at all rather than one sdkmessagefilter query per record.
            Service.OnCreate(e => Id(901));

            var outcome = NewShuffler().TestFlushPendingCreates(Creates(1));

            Assert.That(outcome.Created, Is.EqualTo(1), outcome.ToString());
            Assert.That(Service.Probes.Count, Is.EqualTo(0), DumpAll());
            Assert.That(Service.Created.Count, Is.EqualTo(1), DumpAll());
        }

        [Test]
        public void Capability_query_uses_the_logical_name_not_the_entity_type_code()
        {
            // Landmine marker, not an endorsement. sdkmessagefilter.primaryobjecttypecode
            // holds a numeric entity type code, but the product queries it with the logical
            // name string - and the test double matches the product, so a fixture that says
            // an entity supports CreateMultiple gets the answer it asked for.
            //
            // Against a real org that comparison is what makes the probe answer no for
            // everything, which is why every bulk path also has a working fallback. If this
            // is ever fixed to resolve the type code first, this test fails, and the fixtures
            // that call SupportsBulkMessage need the same treatment.
            NewShuffler().TestIsCreateMultipleSupported("account");

            Assert.That(Service.Probes.Count, Is.EqualTo(1));
            Assert.That(Service.Probes[0].Item1, Is.EqualTo("account"));
            Assert.That(Service.Probes[0].Item2, Is.EqualTo(CreateMultiple));
        }

        private static UpsertResponse UpsertResult(bool recordCreated, Guid id)
        {
            var response = new UpsertResponse();
            response.Results["RecordCreated"] = recordCreated;
            response.Results["Target"] = new EntityReference("account", id);
            return response;
        }

        private static OrganizationResponse UpsertMultipleReturning(params UpsertResponse[] results)
        {
            var response = new OrganizationResponse();
            response.Results["Results"] = results;
            return response;
        }
    }
}
