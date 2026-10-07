namespace Cinteros.Crm.Utils.Shuffle.Tests.Helpers
{
    using System;
    using System.Linq;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// Base for the fixtures that run against a fake organization rather than a scripted one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Layer 1 scripts every answer, which is what the response-pairing tests need and what
    /// makes them unreadable as descriptions of ordinary behaviour. These fixtures go the other
    /// way: a plausible org, seeded with rows, and a whole data block run through it. What they
    /// test is the decisions taken before any flush happens - whether a block is batchable,
    /// which records it matched, whether the upsert gate opened, which bulk message the
    /// capability probe chose.
    /// </para>
    /// <para>
    /// Each test builds its own org, because the shape of the org is the arrangement:
    /// <c>OnPrem()</c> and <c>Online()</c> differ only in what the capability probe answers,
    /// and that difference is what most of these tests are about. Seeding has to finish before
    /// the container is asked for - see <see cref="ShuffleTestContext"/>.
    /// </para>
    /// </remarks>
    public abstract class FakeOrgTestBase
    {
        /// <summary>The org the current test built, once it has built one.</summary>
        protected ShuffleTestContext Org { get; private set; }

        /// <summary>Both output channels, once a shuffler has been built.</summary>
        protected ShuffleEventRecorder Recorder { get; private set; }

        /// <summary>The recording service, for asserting which requests were sent.</summary>
        protected RecordingOrganizationService Service
        {
            get { return Org.Service; }
        }

        /// <summary>Fails the fixture early if the cache reset has stopped finding its fields.</summary>
        [OneTimeSetUp]
        public void AssertCachesAreReachable()
        {
            SharedStaticCaches.AssertResolved();
        }

        /// <summary>
        /// Empty caches and no org before every test. The attribute-name dictionaries in
        /// Xrm.Utils.Core.Common are process-wide, so metadata one fixture seeded would
        /// otherwise answer another fixture's lookup and hide a missing arrangement.
        /// </summary>
        [SetUp]
        public void SetUpFakeOrgTest()
        {
            SharedStaticCaches.Reset();
            Org = null;
            Recorder = null;
        }

        /// <summary>An org that supports no bulk messages - the MMSTEST2 shape.</summary>
        protected ShuffleTestContext OnPrem()
        {
            Org = ShuffleTestContext.AsOnPrem();
            return Org;
        }

        /// <summary>An org where every seeded entity supports every bulk message - the ImransDev shape.</summary>
        protected ShuffleTestContext Online()
        {
            Org = ShuffleTestContext.AsOnline();
            return Org;
        }

        /// <summary>A shuffler over the current org, with its events already captured.</summary>
        protected Shuffler NewShuffler(bool stopOnError = false)
        {
            if (Org == null)
            {
                throw new InvalidOperationException("Build an org with OnPrem() or Online() first.");
            }
            var shuffler = Shuffler.CreateForTest(Org.Container, stopOnError);
            Recorder = new ShuffleEventRecorder(Org.Logger);
            Recorder.Attach(shuffler);
            return shuffler;
        }

        /// <summary>A record with a name, for fixtures that only care that it is distinguishable.</summary>
        protected static Entity Record(string entityLogicalName, Guid id, string name = null)
        {
            var entity = new Entity(entityLogicalName, id);
            entity["name"] = name ?? id.ToString();
            return entity;
        }

        /// <summary>
        /// A row as the target database holds it: like <see cref="Record"/>, but with the primary
        /// id attribute populated.
        /// </summary>
        /// <remarks>
        /// Seeded rows need this and source records do not. Match queries always ask for the
        /// primary id attribute (<c>GetMatchingRecords</c> seeds the column set with it), and a
        /// real retrieve answers with it populated, so a row that omits it is less faithful than
        /// one that carries it - and the fake org refuses to project an attribute a row does not
        /// hold.
        /// </remarks>
        protected static Entity Seeded(string entityLogicalName, Guid id, string name = null)
        {
            var entity = Record(entityLogicalName, id, name);
            entity[entityLogicalName + "id"] = id;
            return entity;
        }

        /// <summary>Deterministic guids, so a failure message names the same record every run.</summary>
        protected static Guid Id(int seed)
        {
            return new Guid(seed, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 });
        }

        /// <summary>Everything both channels saw plus the requests sent, for a failure message.</summary>
        protected string DumpAll()
        {
            var log = Recorder == null ? "(no shuffler built)" : Recorder.Dump();
            var requests = Org == null ? "(no org built)" : string.Join(", ", Service.RequestNames);
            var faults = Org == null || Org.Logger.Exceptions.Count == 0
                ? string.Empty
                : Environment.NewLine + "--- exceptions ---" + Environment.NewLine +
                  string.Join(Environment.NewLine, Org.Logger.Exceptions);
            return log + Environment.NewLine + "Requests: " + requests + faults;
        }
    }
}
