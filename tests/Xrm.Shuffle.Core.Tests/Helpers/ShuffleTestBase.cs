using System;
using Microsoft.Xrm.Sdk;
using NUnit.Framework;

namespace Cinteros.Crm.Utils.Shuffle.Tests.Helpers
{
    /// <summary>
    /// Wires the container, the scripted service and the event recorder, and clears the static
    /// caches that would otherwise leak between fixtures.
    /// </summary>
    /// <remarks>
    /// The caches are the reason this base class exists. Four attribute-name dictionaries and one
    /// MemoryCache in Xrm.Utils.Core.Common are process-wide statics, so a fixture that seeds
    /// metadata for <c>account</c> would silently satisfy the next fixture's lookup and hide a
    /// missing arrangement. <see cref="SharedStaticCaches.Reset"/> runs before every test.
    /// </remarks>
    public abstract class ShuffleTestBase
    {
        /// <summary>The scripted service every fixture arranges against.</summary>
        protected ScriptedOrganizationService Service { get; private set; }

        /// <summary>The container handed to the code under test.</summary>
        protected TestExecutionContainer Container { get; private set; }

        /// <summary>Both output channels — the log and the ShuffleEvent stream.</summary>
        protected ShuffleEventRecorder Recorder { get; private set; }

        /// <summary>Fails the whole fixture early if the cache reset has stopped finding its fields.</summary>
        [OneTimeSetUp]
        public void AssertCachesAreReachable()
        {
            SharedStaticCaches.AssertResolved();
        }

        /// <summary>Fresh doubles and empty caches before every test.</summary>
        [SetUp]
        public void SetUpShuffleTest()
        {
            SharedStaticCaches.Reset();
            Service = new ScriptedOrganizationService();
            var logger = new RecordingLogger();
            Container = new TestExecutionContainer(Service, logger);
            Recorder = new ShuffleEventRecorder(logger);
            OnSetUp();
        }

        /// <summary>Override for per-fixture arrangement that needs the doubles in place.</summary>
        protected virtual void OnSetUp()
        {
        }

        /// <summary>Builds a Shuffler over the test container, with its events already captured.</summary>
        protected Shuffler NewShuffler(bool stopOnError = false)
        {
            var shuffler = Shuffler.CreateForTest(Container, stopOnError);
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

        /// <summary>Deterministic guids, so a failure message names the same record every run.</summary>
        protected static Guid Id(int seed)
        {
            return new Guid(seed, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 });
        }

        /// <summary>Everything both channels saw, for pasting into a failure message.</summary>
        protected string DumpAll()
        {
            return Recorder.Dump() + Environment.NewLine + "Requests: " + string.Join(", ", Service.RequestNames);
        }
    }
}
