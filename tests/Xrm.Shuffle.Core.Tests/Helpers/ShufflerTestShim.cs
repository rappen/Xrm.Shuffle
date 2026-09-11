namespace Cinteros.Crm.Utils.Shuffle
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.Xrm.Sdk;
    using Xrm.Utils.Core.Common.Interfaces;

    /// <summary>
    /// Test-only surface over the private batch machinery in ShuffleDataImport.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shuffler is partial, so this file adds to the same class rather than reaching in from
    /// outside. That matters for two reasons: the flush methods are private, and the batch
    /// item types are private nested structs, which no external fixture can even name. A
    /// wrapper type declared here can name them, because it is nested in Shuffler too.
    /// </para>
    /// <para>
    /// The alternative - reflection, or InternalsVisibleTo on a shared project with no
    /// assembly of its own - would either break silently on a rename or not work at all.
    /// This costs one file that ships with the tests and never runs in production.
    /// </para>
    /// <para>
    /// This file is deliberately mechanical: it unwraps arguments, calls the private member,
    /// and wraps the result. Any logic here would be logic the tests are not testing.
    /// </para>
    /// </remarks>
    public partial class Shuffler
    {
        /// <summary>
        /// A Shuffler ready to have a flush method called on it directly.
        /// </summary>
        /// <remarks>
        /// The constructor only stores the container. guidmap, stoponerror and timeout are
        /// set later, inside ImportToCRM - so a shuffler that has not run an import has a
        /// null guidmap, and calling a flush method on it throws a NullReferenceException
        /// that says nothing about the test. This performs the same initialisation for a
        /// definition-less run.
        /// </remarks>
        public static Shuffler CreateForTest(IExecutionContainer container, bool stopOnError = false)
        {
            var shuffler = new Shuffler(container);
            shuffler.guidmap = new Dictionary<Guid, Guid>();
            shuffler.stoponerror = stopOnError;
            shuffler.timeout = -1;
            return shuffler;
        }

        /// <summary>The guid map, so a fixture can seed and inspect the remapping.</summary>
        public Dictionary<Guid, Guid> TestGuidMap => guidmap;

        /// <summary>
        /// The record label StopOnBatchError stamps when it decides to abort.
        /// </summary>
        /// <remarks>
        /// Null until a batch error is recorded, which is the signal the import block reads
        /// to decide whether to throw - so asserting on it is asserting on stop-on-error.
        /// </remarks>
        public string TestBatchFailureLabel => batchFailureLabel;

        /// <summary>Whether this instance was created with stop-on-error set.</summary>
        public bool TestStopOnError => stoponerror;

        /// <summary>What a flush left behind: the counters and the references it collected.</summary>
        public class BatchOutcome
        {
            /// <summary>Records the flush counted as created.</summary>
            public int Created;

            /// <summary>Records the flush counted as updated.</summary>
            public int Updated;

            /// <summary>Records the flush counted as deleted.</summary>
            public int Deleted;

            /// <summary>Records the flush counted as failed.</summary>
            public int Failed;

            /// <summary>References the flush collected for the created records.</summary>
            public EntityReferenceCollection References = new EntityReferenceCollection();

            /// <summary>Created plus updated plus failed - every record the flush accounted for.</summary>
            public int Accounted => Created + Updated + Failed;

            public override string ToString()
            {
                return string.Format(
                    "created {0}, updated {1}, deleted {2}, failed {3}, references {4}",
                    Created, Updated, Deleted, Failed, References.Count);
            }
        }

        /// <summary>A batch of pending creates, built by a fixture one record at a time.</summary>
        /// <remarks>
        /// PendingCreate is a private nested struct, so this wrapper is the only way a test
        /// can hand one to a flush method. Same for the update and upsert batches below.
        /// </remarks>
        public class TestCreateBatch
        {
            private readonly List<PendingCreate> Items = new List<PendingCreate>();

            /// <summary>Adds a record to the batch, numbering positions from 1 as the import does.</summary>
            public TestCreateBatch Add(Entity entity, Guid oldId = default(Guid), string identifier = null)
            {
                Items.Add(new PendingCreate
                {
                    Entity = entity,
                    OldId = oldId == Guid.Empty ? entity.Id : oldId,
                    Position = Items.Count + 1,
                    Identifier = identifier ?? entity.LogicalName + " " + (Items.Count + 1)
                });
                return this;
            }

            /// <summary>How many records are queued.</summary>
            public int Count => Items.Count;

            /// <summary>
            /// The entities queued, in order. Read this <em>before</em> flushing: every
            /// dispatcher clears its batch once it has flushed, so afterwards this is empty.
            /// The list is a snapshot but the entities in it are the live objects, which is
            /// what makes it useful for asserting ids written back by a bulk message.
            /// </summary>
            public IReadOnlyList<Entity> Entities => Items.Select(i => i.Entity).ToList();
            /// <remarks>
            /// The flush calls live here rather than on the outer class: a containing type cannot
            /// reach a nested type's private members, but a nested type can reach the outer's.
            /// Items has to stay private, because PendingCreate is private (CS0052).
            /// </remarks>
            internal BatchOutcome FlushDispatcher(Shuffler owner)
            {
                var outcome = new BatchOutcome();
                owner.FlushPendingCreates(owner.container, Items, ref outcome.Created, ref outcome.Failed, outcome.References);
                return outcome;
            }

            internal BatchOutcome FlushExecuteMultiple(Shuffler owner)
            {
                var outcome = new BatchOutcome();
                owner.FlushCreatesWithExecuteMultiple(owner.container, Items, ref outcome.Created, ref outcome.Failed, outcome.References);
                return outcome;
            }

            internal BatchOutcome FlushIndividually(Shuffler owner)
            {
                var outcome = new BatchOutcome();
                owner.FlushCreatesIndividually(owner.container, Items, ref outcome.Created, ref outcome.Failed, outcome.References);
                return outcome;
            }

            internal bool References(Entity entity)
            {
                return ReferencesPendingCreate(entity, Items);
            }
        }

        /// <summary>A batch of pending updates.</summary>
        public class TestUpdateBatch
        {
            private readonly List<PendingUpdate> Items = new List<PendingUpdate>();

            public TestUpdateBatch Add(Entity entity, string identifier = null)
            {
                Items.Add(new PendingUpdate
                {
                    Entity = entity,
                    Position = Items.Count + 1,
                    Identifier = identifier ?? entity.LogicalName + " " + (Items.Count + 1)
                });
                return this;
            }

            public int Count => Items.Count;

            public IReadOnlyList<Entity> Entities => Items.Select(i => i.Entity).ToList();
            internal BatchOutcome FlushDispatcher(Shuffler owner)
            {
                var outcome = new BatchOutcome();
                owner.FlushPendingUpdates(owner.container, Items, ref outcome.Updated, ref outcome.Failed, outcome.References);
                return outcome;
            }

            internal BatchOutcome FlushExecuteMultiple(Shuffler owner)
            {
                var outcome = new BatchOutcome();
                owner.FlushUpdatesWithExecuteMultiple(owner.container, Items, ref outcome.Updated, ref outcome.Failed, outcome.References);
                return outcome;
            }

            internal BatchOutcome FlushIndividually(Shuffler owner)
            {
                var outcome = new BatchOutcome();
                owner.FlushUpdatesIndividually(owner.container, Items, ref outcome.Updated, ref outcome.Failed, outcome.References);
                return outcome;
            }
        }

        /// <summary>A batch of pending upserts.</summary>
        public class TestUpsertBatch
        {
            private readonly List<PendingUpsert> Items = new List<PendingUpsert>();

            public TestUpsertBatch Add(Entity entity, Guid oldId = default(Guid), string identifier = null)
            {
                Items.Add(new PendingUpsert
                {
                    Entity = entity,
                    OldId = oldId == Guid.Empty ? entity.Id : oldId,
                    Position = Items.Count + 1,
                    Identifier = identifier ?? entity.LogicalName + " " + (Items.Count + 1)
                });
                return this;
            }

            public int Count => Items.Count;

            public IReadOnlyList<Entity> Entities => Items.Select(i => i.Entity).ToList();
            internal BatchOutcome FlushDispatcher(Shuffler owner)
            {
                var outcome = new BatchOutcome();
                owner.FlushPendingUpserts(owner.container, Items, ref outcome.Created, ref outcome.Updated, ref outcome.Failed, outcome.References);
                return outcome;
            }
        }

        /// <summary>Runs the create dispatcher over <paramref name="batch"/>.</summary>
        public BatchOutcome TestFlushPendingCreates(TestCreateBatch batch)
        {
            return batch.FlushDispatcher(this);
        }

        /// <summary>Runs the update dispatcher over <paramref name="batch"/>.</summary>
        public BatchOutcome TestFlushPendingUpdates(TestUpdateBatch batch)
        {
            return batch.FlushDispatcher(this);
        }

        /// <summary>Runs the upsert dispatcher over <paramref name="batch"/>.</summary>
        public BatchOutcome TestFlushPendingUpserts(TestUpsertBatch batch)
        {
            return batch.FlushDispatcher(this);
        }

        /// <summary>Runs the delete dispatcher over <paramref name="batch"/>.</summary>
        public BatchOutcome TestFlushPendingDeletes(List<Entity> batch)
        {
            var outcome = new BatchOutcome();
            FlushPendingDeletes(container, batch, ref outcome.Deleted, ref outcome.Failed);
            return outcome;
        }

        /// <summary>Calls the ExecuteMultiple rung for creates directly, skipping the dispatcher.</summary>
        /// <remarks>
        /// The dispatcher probes for CreateMultiple first, so a fixture that wants to test
        /// response-to-request pairing on its own would otherwise have to script the probe
        /// as well. Going straight at the rung keeps those tests about one thing.
        /// </remarks>
        public BatchOutcome TestFlushCreatesWithExecuteMultiple(TestCreateBatch batch)
        {
            return batch.FlushExecuteMultiple(this);
        }

        /// <summary>Calls the ExecuteMultiple rung for updates directly.</summary>
        public BatchOutcome TestFlushUpdatesWithExecuteMultiple(TestUpdateBatch batch)
        {
            return batch.FlushExecuteMultiple(this);
        }

        /// <summary>Calls the individual-create rung directly.</summary>
        public BatchOutcome TestFlushCreatesIndividually(TestCreateBatch batch)
        {
            return batch.FlushIndividually(this);
        }

        /// <summary>Calls the individual-update rung directly.</summary>
        public BatchOutcome TestFlushUpdatesIndividually(TestUpdateBatch batch)
        {
            return batch.FlushIndividually(this);
        }

        /// <summary>Asks whether CreateMultiple is supported, driving the sdkmessagefilter probe.</summary>
        public bool TestIsCreateMultipleSupported(string entityLogicalName) =>
            IsCreateMultipleSupported(container, entityLogicalName);

        /// <summary>Asks whether UpdateMultiple is supported.</summary>
        public bool TestIsUpdateMultipleSupported(string entityLogicalName) =>
            IsUpdateMultipleSupported(container, entityLogicalName);

        /// <summary>Asks whether UpsertMultiple is supported.</summary>
        public bool TestIsUpsertMultipleSupported(string entityLogicalName) =>
            IsUpsertMultipleSupported(container, entityLogicalName);

        /// <summary>Asks whether the single Upsert message is supported.</summary>
        public bool TestIsUpsertSupported(string entityLogicalName) =>
            IsUpsertSupported(container, entityLogicalName);

        /// <summary>Whether a record can go in a batch at all.</summary>
        public static bool TestIsBatchable(Entity entity) => IsBatchable(entity);

        /// <summary>Whether a record points at something still waiting in the create batch.</summary>
        public static bool TestReferencesPendingCreate(Entity entity, TestCreateBatch pending) =>
            pending.References(entity);

        /// <summary>Rewrites every lookup on a record through the guid map, in place.</summary>
        public void TestReplaceGuids(Entity entity, bool includeId = false) =>
            ReplaceGuids(container, entity, includeId);

        /// <summary>Adds a pair to the guid map, subject to the same conditions the import applies.</summary>
        public void TestMapGuid(Guid oldId, Guid newId) => MapGuid(oldId, newId);

        /// <summary>Maps a created id and fills any deferred change waiting on that record.</summary>
        public void TestRecordCreatedId(Guid oldId, Guid newId) => RecordCreatedId(oldId, newId);

        /// <summary>Records a batch error and reports whether the import should stop.</summary>
        public bool TestStopOnBatchError(int position, string identifier) =>
            StopOnBatchError(position, identifier);

        /// <summary>Strips state and owner off a record, deferring them to the second pass.</summary>
        public void TestStripAndDeferStateOwner(Entity entity, int position = 1, string identifier = null)
        {
            StripAndDeferStateOwner(entity, deferredStates, deferredOwners, position, identifier ?? entity.LogicalName);
        }

        /// <summary>Queues a deferred state change without going through the strip pass.</summary>
        public void TestDeferState(string entityLogicalName, Guid originalId, Guid actualId, int stateCode, int statusCode, int position = 1, string identifier = null)
        {
            deferredStates.Add(new DeferredStateChange
            {
                EntityLogicalName = entityLogicalName,
                OriginalId = originalId,
                ActualId = actualId,
                StateCode = new OptionSetValue(stateCode),
                StatusCode = new OptionSetValue(statusCode),
                Position = position,
                Identifier = identifier ?? entityLogicalName
            });
        }

        /// <summary>Queues a deferred owner change without going through the strip pass.</summary>
        public void TestDeferOwner(string entityLogicalName, Guid originalId, Guid actualId, EntityReference owner, int position = 1, string identifier = null)
        {
            deferredOwners.Add(new DeferredOwnerChange
            {
                EntityLogicalName = entityLogicalName,
                OriginalId = originalId,
                ActualId = actualId,
                Owner = owner,
                Position = position,
                Identifier = identifier ?? entityLogicalName
            });
        }

        /// <summary>How many state changes are waiting for the second pass.</summary>
        public int TestDeferredStateCount => deferredStates.Count;

        /// <summary>How many owner changes are waiting for the second pass.</summary>
        public int TestDeferredOwnerCount => deferredOwners.Count;

        /// <summary>The state codes queued for <paramref name="originalId"/>, as state and status.</summary>
        public Tuple<int, int> TestDeferredStateCodes(Guid originalId)
        {
            var match = deferredStates.Where(s => s.OriginalId == originalId).ToList();
            if (match.Count == 0)
            {
                return null;
            }
            return Tuple.Create(match[0].StateCode.Value, match[0].StatusCode.Value);
        }

        /// <summary>
        /// The id the deferred pass will actually write to for <paramref name="originalId"/>.
        /// </summary>
        /// <remarks>
        /// Empty means the record was never written, which is what the deferred pass uses to
        /// decide to drop the change rather than update a record that does not exist.
        /// </remarks>
        public Guid? TestDeferredActualId(Guid originalId)
        {
            var match = deferredStates.Where(s => s.OriginalId == originalId).ToList();
            return match.Count == 0 ? (Guid?)null : match[0].ActualId;
        }

        /// <summary>The id the deferred owner pass will write to for <paramref name="originalId"/>.</summary>
        public Guid? TestDeferredOwnerActualId(Guid originalId)
        {
            var match = deferredOwners.Where(o => o.OriginalId == originalId).ToList();
            return match.Count == 0 ? (Guid?)null : match[0].ActualId;
        }

        /// <summary>Fills in real ids on the deferred queues after a record was written.</summary>
        public void TestUpdateDeferredActualIds(Guid originalId, Guid actualId) =>
            UpdateDeferredActualIds(originalId, actualId);

        /// <summary>Runs the deferred state pass.</summary>
        public void TestFlushDeferredStateChanges() =>
            FlushDeferredStateChanges(container, deferredStates);

        /// <summary>What a whole block import did, by counter.</summary>
        public class BlockOutcome
        {
            public int Created;
            public int Updated;
            public int Skipped;
            public int Deleted;
            public int Failed;
            public EntityReferenceCollection References;

            /// <summary>Every record the block accounted for, however it accounted for it.</summary>
            public int Accounted
            {
                get { return Created + Updated + Skipped + Deleted + Failed; }
            }

            public override string ToString()
            {
                return string.Format(
                    "created {0}, updated {1}, skipped {2}, deleted {3}, failed {4}",
                    Created, Updated, Skipped, Deleted, Failed);
            }
        }

        /// <summary>
        /// Runs one whole data block, which is the only way to reach the decisions that are
        /// made before any flush happens - the upsert gate, batchability, match resolution and
        /// the capability probes.
        /// </summary>
        public BlockOutcome TestImportDataBlock(Types.DataBlock block, EntityCollection entities)
        {
            var result = ImportDataBlock(container, block, entities);
            return new BlockOutcome
            {
                Created = result.Item1,
                Updated = result.Item2,
                Skipped = result.Item3,
                Deleted = result.Item4,
                Failed = result.Item5,
                References = result.Item6
            };
        }

        /// <summary>Runs the deferred owner pass.</summary>
        public void TestFlushDeferredOwnerChanges() =>
            FlushDeferredOwnerChanges(container, deferredOwners);
    }
}
