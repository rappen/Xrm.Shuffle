namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer3
{
    using System;
    using System.Linq;
    using System.ServiceModel;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// The second pass that makes state and owner batchable at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A record carrying statecode, statuscode or ownerid cannot go in a batch - IsBatchable
    /// says so, and the bulk messages would drop the values on the floor. DeferStateAndOwner
    /// strips those three attributes off every record, batches what is left, and applies the
    /// stripped values afterwards against the ids the first pass actually wrote.
    /// </para>
    /// <para>
    /// That split is where the interesting failures live: a value queued against a source id
    /// that never became a real id, a state pass that has UpdateMultiple and an owner pass that
    /// never does, and a fluent Assign helper that cannot report a failure.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class DeferStateAndOwnerTests : FakeOrgTestBase
    {
        private static Entity Stateful(string entityLogicalName, Guid id, int state, int status)
        {
            var entity = Record(entityLogicalName, id);
            entity["statecode"] = new OptionSetValue(state);
            entity["statuscode"] = new OptionSetValue(status);
            return entity;
        }

        private static EntityReference Owner(int seed)
        {
            return new EntityReference("systemuser", Id(seed));
        }

        private static OptionSetValue Option(OrganizationRequest request, string parameter)
        {
            return (OptionSetValue)request[parameter];
        }

        // ---- the strip pass -------------------------------------------------

        [Test]
        public void State_and_owner_are_stripped_off_the_record_and_queued()
        {
            OnPrem().WithMetadata("account");
            var record = Stateful("account", Id(1), 1, 2);
            record["ownerid"] = Owner(9);

            var shuffler = NewShuffler();
            shuffler.TestStripAndDeferStateOwner(record);

            Assert.That(record.Contains("statecode"), Is.False, "statecode should have been stripped");
            Assert.That(record.Contains("statuscode"), Is.False, "statuscode should have been stripped");
            Assert.That(record.Contains("ownerid"), Is.False, "ownerid should have been stripped");
            Assert.That(record.Contains("name"), Is.True, "everything else should be left alone");
            Assert.That(shuffler.TestDeferredStateCount, Is.EqualTo(1));
            Assert.That(shuffler.TestDeferredOwnerCount, Is.EqualTo(1));
            Assert.That(shuffler.TestDeferredStateCodes(Id(1)), Is.EqualTo(Tuple.Create(1, 2)));
        }

        /// <summary>
        /// Stripping a record that carries nothing else would leave an empty record to save.
        /// </summary>
        [Test]
        public void A_record_with_nothing_besides_state_and_owner_is_left_alone()
        {
            OnPrem().WithMetadata("account");
            var record = new Entity("account", Id(1));
            record["statecode"] = new OptionSetValue(1);
            record["statuscode"] = new OptionSetValue(2);

            var shuffler = NewShuffler();
            shuffler.TestStripAndDeferStateOwner(record);

            Assert.That(shuffler.TestDeferredStateCount, Is.EqualTo(0));
            Assert.That(record.Contains("statecode"), Is.True, "nothing should have been stripped");
        }

        /// <summary>
        /// SetState needs both halves, so a record carrying only one is not deferred - and is
        /// therefore not batchable either, which is the honest outcome.
        /// </summary>
        [Test]
        public void Statecode_without_statuscode_is_not_deferred()
        {
            OnPrem().WithMetadata("account");
            var record = Record("account", Id(1));
            record["statecode"] = new OptionSetValue(1);

            var shuffler = NewShuffler();
            shuffler.TestStripAndDeferStateOwner(record);

            Assert.That(shuffler.TestDeferredStateCount, Is.EqualTo(0));
            Assert.That(record.Contains("statecode"), Is.True, "a half state must not be removed");
            Assert.That(Shuffler.TestIsBatchable(record), Is.False);
        }

        /// <summary>This is the whole point of the option.</summary>
        [Test]
        public void A_stripped_record_becomes_batchable()
        {
            OnPrem().WithMetadata("account");
            var record = Stateful("account", Id(1), 1, 2);
            record["ownerid"] = Owner(9);

            Assert.That(Shuffler.TestIsBatchable(record), Is.False, "before");
            NewShuffler().TestStripAndDeferStateOwner(record);
            Assert.That(Shuffler.TestIsBatchable(record), Is.True, "after");
        }

        // ---- filling in the real id ----------------------------------------

        [Test]
        public void The_real_id_reaches_both_queues()
        {
            OnPrem().WithMetadata("account");
            var shuffler = NewShuffler();
            shuffler.TestDeferState("account", Id(1), Guid.Empty, 1, 2);
            shuffler.TestDeferOwner("account", Id(1), Guid.Empty, Owner(9));

            shuffler.TestUpdateDeferredActualIds(Id(1), Id(101));

            Assert.That(shuffler.TestDeferredActualId(Id(1)), Is.EqualTo(Id(101)));
            Assert.That(shuffler.TestDeferredOwnerActualId(Id(1)), Is.EqualTo(Id(101)));
        }

        [Test]
        public void Only_the_matching_record_gets_the_id()
        {
            OnPrem().WithMetadata("account");
            var shuffler = NewShuffler();
            shuffler.TestDeferState("account", Id(1), Guid.Empty, 1, 2);
            shuffler.TestDeferState("account", Id(2), Guid.Empty, 0, 1);

            shuffler.TestUpdateDeferredActualIds(Id(1), Id(101));

            Assert.That(shuffler.TestDeferredActualId(Id(1)), Is.EqualTo(Id(101)));
            Assert.That(shuffler.TestDeferredActualId(Id(2)), Is.EqualTo(Guid.Empty));
        }

        // ---- the state pass -------------------------------------------------

        [Test]
        public void Deferred_states_go_out_as_one_UpdateMultiple_when_supported()
        {
            Online()
                .WithEntity(Seeded("account", Id(101), "Alpha"), Seeded("account", Id(102), "Beta"))
                .WithAttributes("account", "statecode", "statuscode");
            var shuffler = NewShuffler();
            shuffler.TestDeferState("account", Id(1), Id(101), 1, 2, 1, "Alpha");
            shuffler.TestDeferState("account", Id(2), Id(102), 1, 2, 2, "Beta");

            shuffler.TestFlushDeferredStateChanges();

            Assert.That(Service.CountOf("UpdateMultiple"), Is.EqualTo(1), DumpAll());
            Assert.That(Service.CountOf("SetState"), Is.EqualTo(0), DumpAll());
            var targets = RecordingOrganizationService.TargetsOf(Service.RequestsNamed("UpdateMultiple")[0]);
            Assert.That(targets.Select(t => t.Id).ToList(), Is.EqualTo(new[] { Id(101), Id(102) }), DumpAll());
            Assert.That(((OptionSetValue)targets[0]["statecode"]).Value, Is.EqualTo(1), DumpAll());
            Assert.That(((OptionSetValue)targets[0]["statuscode"]).Value, Is.EqualTo(2), DumpAll());
            Assert.That(Org.Logger.Logged("Applied 2 state changes via UpdateMultiple for account"), DumpAll());
            Assert.That(Org.Logger.Logged("Deferred state changes: 2 applied, 0 failed, 0 skipped"), DumpAll());
        }

        /// <summary>
        /// The Targets collection of a bulk message names one entity, so a mixed queue cannot
        /// go out in one request however many records it holds.
        /// </summary>
        [Test]
        public void Each_entity_gets_its_own_UpdateMultiple()
        {
            Online()
                .WithEntity(
                    Seeded("account", Id(101), "Alpha"),
                    Seeded("account", Id(102), "Beta"),
                    Seeded("contact", Id(201), "Carol"),
                    Seeded("contact", Id(202), "Dave"))
                .WithAttributes("account", "statecode", "statuscode")
                .WithAttributes("contact", "statecode", "statuscode");
            var shuffler = NewShuffler();
            shuffler.TestDeferState("account", Id(1), Id(101), 1, 2);
            shuffler.TestDeferState("account", Id(2), Id(102), 1, 2);
            shuffler.TestDeferState("contact", Id(3), Id(201), 1, 2);
            shuffler.TestDeferState("contact", Id(4), Id(202), 1, 2);

            shuffler.TestFlushDeferredStateChanges();

            Assert.That(Service.CountOf("UpdateMultiple"), Is.EqualTo(2), DumpAll());
            Assert.That(
                Service.RequestsNamed("UpdateMultiple")
                    .Select(r => RecordingOrganizationService.TargetsOf(r).Count).ToList(),
                Is.EqualTo(new[] { 2, 2 }),
                DumpAll());
            Assert.That(Org.Logger.Logged("Deferred state changes: 4 applied, 0 failed, 0 skipped"), DumpAll());
        }

        [Test]
        public void Without_UpdateMultiple_the_states_are_set_one_at_a_time()
        {
            OnPrem()
                .WithEntity(Seeded("account", Id(101), "Alpha"), Seeded("account", Id(102), "Beta"))
                .WithAttributes("account", "statecode", "statuscode");
            var shuffler = NewShuffler();
            shuffler.TestDeferState("account", Id(1), Id(101), 1, 2, 1, "Alpha");
            shuffler.TestDeferState("account", Id(2), Id(102), 1, 2, 2, "Beta");

            shuffler.TestFlushDeferredStateChanges();

            Assert.That(Service.CountOf("UpdateMultiple"), Is.EqualTo(0), DumpAll());
            Assert.That(Service.CountOf("SetState"), Is.EqualTo(2), DumpAll());
            Assert.That(Org.Logger.Logged("001 SetState (deferred): Alpha: 1/2"), DumpAll());
            Assert.That(Org.Logger.Logged("Deferred state changes: 2 applied, 0 failed, 0 skipped"), DumpAll());
        }

        /// <summary>
        /// A rejected UpdateMultiple is not a failure of the records - the same states go out
        /// one SetState at a time and the block still reports them applied.
        /// </summary>
        [Test]
        public void A_failed_UpdateMultiple_falls_back_to_SetState()
        {
            Online()
                .WithEntity(Seeded("account", Id(101), "Alpha"), Seeded("account", Id(102), "Beta"))
                .WithAttributes("account", "statecode", "statuscode");
            Service.ThrowAlways("UpdateMultiple", new InvalidOperationException("no bulk here"));
            var shuffler = NewShuffler();
            shuffler.TestDeferState("account", Id(1), Id(101), 1, 2);
            shuffler.TestDeferState("account", Id(2), Id(102), 1, 2);

            shuffler.TestFlushDeferredStateChanges();

            Assert.That(Service.CountOf("SetState"), Is.EqualTo(2), DumpAll());
            Assert.That(Org.Logger.Logged("UpdateMultiple for state changes failed: no bulk here"), DumpAll());
            Assert.That(Org.Logger.Logged("Deferred state changes: 2 applied, 0 failed, 0 skipped"), DumpAll());
        }

        /// <summary>
        /// A record the first pass never wrote has no id to apply anything to. That is not a
        /// failure - the first pass already reported why - so it is dropped and counted apart.
        /// </summary>
        [Test]
        public void Changes_for_records_that_were_never_written_are_skipped_not_failed()
        {
            OnPrem()
                .WithEntity(Seeded("account", Id(101), "Alpha"))
                .WithAttributes("account", "statecode", "statuscode");
            var shuffler = NewShuffler();
            shuffler.TestDeferState("account", Id(1), Id(101), 1, 2);
            shuffler.TestDeferState("account", Id(2), Guid.Empty, 1, 2);

            shuffler.TestFlushDeferredStateChanges();

            Assert.That(Service.CountOf("SetState"), Is.EqualTo(1), DumpAll());
            Assert.That(
                Org.Logger.Logged("Skipping 1 deferred state change(s) for records that were not written"),
                DumpAll());
            Assert.That(Org.Logger.Logged("Deferred state changes: 1 applied, 0 failed, 1 skipped"), DumpAll());
        }

        /// <summary>
        /// savedquery and duplicaterule do not take a plain state update, so they never go
        /// through UpdateMultiple even where the platform offers it. A published savedquery is
        /// also the one place where the state written differs from the state logged.
        /// </summary>
        [Test]
        public void savedquery_never_goes_through_UpdateMultiple_even_where_it_is_supported()
        {
            Online()
                .WithEntity(Seeded("savedquery", Id(101), "Active accounts"))
                .WithAttributes("savedquery", "statecode", "statuscode");
            var shuffler = NewShuffler();
            shuffler.TestDeferState("savedquery", Id(1), Id(101), 1, 1, 1, "Active accounts");

            shuffler.TestFlushDeferredStateChanges();

            Assert.That(Service.CountOf("UpdateMultiple"), Is.EqualTo(0), DumpAll());
            Assert.That(Service.CountOf("SetState"), Is.EqualTo(1), DumpAll());
            var setState = Service.RequestsNamed("SetState")[0];
            Assert.That(Option(setState, "State").Value, Is.EqualTo(1), DumpAll());
            Assert.That(Option(setState, "Status").Value, Is.EqualTo(2), "1/1 is rewritten to 1/2 for savedquery");
            Assert.That(
                Org.Logger.Logged("001 SetState (deferred): Active accounts: 1/1"),
                "the line reports the requested state, not the one sent");
        }

        // ---- the owner pass -------------------------------------------------

        /// <summary>
        /// There is no bulk assign, so the owner pass has no fallback chain to choose from.
        /// </summary>
        [Test]
        public void Deferred_owners_are_assigned_one_at_a_time()
        {
            Online()
                .WithEntity(Seeded("account", Id(101), "Alpha"), Seeded("account", Id(102), "Beta"))
                .WithAttributes("account", "ownerid");
            var shuffler = NewShuffler();
            shuffler.TestDeferOwner("account", Id(1), Id(101), Owner(9), 1, "Alpha");
            shuffler.TestDeferOwner("account", Id(2), Id(102), Owner(9), 2, "Beta");

            shuffler.TestFlushDeferredOwnerChanges();

            Assert.That(Service.CountOf("Assign"), Is.EqualTo(2), DumpAll());
            Assert.That(Service.CountOf("UpdateMultiple"), Is.EqualTo(0), DumpAll());
            Assert.That(Org.Logger.Logged("001 Assigned (deferred): Alpha to systemuser"), DumpAll());
            Assert.That(Org.Logger.Logged("Deferred owner changes: 2 applied, 0 failed, 0 skipped"), DumpAll());
        }

        [Test]
        public void Owner_changes_for_unwritten_records_are_skipped()
        {
            OnPrem()
                .WithEntity(Seeded("account", Id(101), "Alpha"))
                .WithAttributes("account", "ownerid");
            var shuffler = NewShuffler();
            shuffler.TestDeferOwner("account", Id(1), Id(101), Owner(9));
            shuffler.TestDeferOwner("account", Id(2), Guid.Empty, Owner(9));

            shuffler.TestFlushDeferredOwnerChanges();

            Assert.That(Service.CountOf("Assign"), Is.EqualTo(1), DumpAll());
            Assert.That(
                Org.Logger.Logged("Skipping 1 deferred owner change(s) for records that were not written"),
                DumpAll());
            Assert.That(Org.Logger.Logged("Deferred owner changes: 1 applied, 0 failed, 1 skipped"), DumpAll());
        }

        /// <summary>
        /// KNOWN DEFECT - asserts what the code does today, not what it should do.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The owner pass reaches AssignRequest through the fluent helper
        /// <c>container.Principal(x).On(y).Assign()</c>, and OperationsSet2.Assign catches every
        /// exception and returns false. The catch in FlushDeferredOwnerChanges is therefore dead
        /// code: a rejected assign increments applied, logs "Assigned (deferred)", and StopOnError
        /// never fires. The records keep the owner the import was supposed to change.
        /// </para>
        /// <para>
        /// A fix would read the bool the helper returns, or send the AssignRequest directly. When
        /// that lands this test fails, which is the point of writing it down.
        /// </para>
        /// </remarks>
        [Test]
        public void A_failed_assign_is_silently_counted_as_applied()
        {
            OnPrem()
                .WithEntity(Seeded("account", Id(101), "Alpha"))
                .WithAttributes("account", "ownerid");
            Service.ThrowAlways(
                "Assign",
                new FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault(), "principal has no access"));
            var shuffler = NewShuffler(stopOnError: true);
            shuffler.TestDeferOwner("account", Id(1), Id(101), Owner(9), 1, "Alpha");

            Assert.DoesNotThrow(() => shuffler.TestFlushDeferredOwnerChanges(), DumpAll());

            Assert.That(Org.Logger.Logged("001 Assigned (deferred): Alpha to systemuser"), DumpAll());
            Assert.That(Org.Logger.Logged("Deferred owner changes: 1 applied, 0 failed, 0 skipped"), DumpAll());
            Assert.That(Org.Logger.Logged("Assign Failed (deferred)"), Is.False, DumpAll());
        }

        /// <summary>
        /// KNOWN DEFECT - asserts what the code does today, not what it should do.
        /// </summary>
        /// <remarks>
        /// The fluent helper reads Principal as the assignee and On as the target, but the owner
        /// pass calls <c>container.Principal(record).On(owner)</c>, so the request that goes out
        /// asks to assign the account to itself with the user as the target. The swallowed
        /// exception above is what keeps this invisible. Fixing either one of these two defects
        /// without the other only changes which of them is reported.
        /// </remarks>
        [Test]
        public void The_deferred_assign_sends_the_record_and_the_owner_the_wrong_way_round()
        {
            OnPrem()
                .WithEntity(Seeded("account", Id(101), "Alpha"))
                .WithAttributes("account", "ownerid");
            var shuffler = NewShuffler();
            shuffler.TestDeferOwner("account", Id(1), Id(101), Owner(9), 1, "Alpha");

            shuffler.TestFlushDeferredOwnerChanges();

            var assign = Service.RequestsNamed("Assign")[0];
            var assignee = (EntityReference)assign["Assignee"];
            var target = (EntityReference)assign["Target"];
            Assert.That(assignee.LogicalName, Is.EqualTo("account"), "should be the systemuser");
            Assert.That(target.LogicalName, Is.EqualTo("systemuser"), "should be the account");
        }

        // ---- the option seen from a whole block -----------------------------

        private static Types.DataBlock Block(bool defer)
        {
            var builder = DefinitionXml.DataBlock("Accounts", "account").BatchSize(10);
            if (defer)
            {
                builder = builder.DeferStateAndOwner();
            }
            return builder.DeserializeBlock();
        }

        /// <summary>
        /// A block carrying nothing but state and owner is already a second pass of its own -
        /// the common shape where state changes live in a separate UpdateOnly block. Deferring
        /// there would strip every record down to nothing, so the option is refused rather than
        /// obeyed.
        /// </summary>
        [Test]
        public void A_state_and_owner_only_block_ignores_the_option()
        {
            Online().WithMetadata("account").WithAttributes("account", "statecode", "statuscode");
            var sources = new EntityCollection { EntityName = "account" };
            sources.Entities.Add(Stateful("account", Id(1), 1, 2));
            sources.Entities.Add(Stateful("account", Id(2), 1, 2));
            sources.Entities[0].Attributes.Remove("name");
            sources.Entities[1].Attributes.Remove("name");

            NewShuffler().TestImportDataBlock(Block(defer: true), sources);

            Assert.That(
                Org.Logger.Logged(
                    "DeferStateAndOwner ignored - this block carries no attributes besides state and owner"),
                DumpAll());
            Assert.That(Service.CountOf("CreateMultiple"), Is.EqualTo(0), DumpAll());
        }

        /// <summary>
        /// The end-to-end claim: a block whose records set an owner batches with the option on
        /// and does not without it.
        /// </summary>
        [Test]
        public void Deferring_lets_a_block_that_sets_owner_batch()
        {
            Online().WithMetadata("account").WithAttributes("account", "ownerid");
            var outcome = NewShuffler().TestImportDataBlock(Block(defer: true), Owned());

            Assert.That(Org.Logger.Logged("DeferStateAndOwner enabled - state/owner will be applied in second pass"), DumpAll());
            Assert.That(outcome.Created, Is.EqualTo(2), DumpAll());
            Assert.That(Service.CountOf("CreateMultiple"), Is.EqualTo(1), DumpAll());
            Assert.That(Service.CountOf("Assign"), Is.EqualTo(2), DumpAll());
            Assert.That(Org.Logger.Logged("Deferred owner changes: 2 applied, 0 failed, 0 skipped"), DumpAll());
        }

        [Test]
        public void Without_the_option_the_same_block_is_written_one_record_at_a_time()
        {
            Online().WithMetadata("account").WithAttributes("account", "ownerid");
            var outcome = NewShuffler().TestImportDataBlock(Block(defer: false), Owned());

            Assert.That(outcome.Created, Is.EqualTo(2), DumpAll());
            Assert.That(Service.CountOf("CreateMultiple"), Is.EqualTo(0), DumpAll());
            Assert.That(Service.Created.Count, Is.EqualTo(2), DumpAll());
            Assert.That(Service.CountOf("Assign"), Is.EqualTo(0), DumpAll());
        }

        private static EntityCollection Owned()
        {
            var sources = new EntityCollection { EntityName = "account" };
            for (var seed = 1; seed <= 2; seed++)
            {
                var record = Record("account", Id(seed), "Account " + seed);
                record["ownerid"] = Owner(9);
                sources.Entities.Add(record);
            }
            return sources;
        }
    }
}
