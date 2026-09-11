namespace Cinteros.Crm.Utils.Shuffle.Tests.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;

    /// <summary>
    /// Captures the events a <see cref="Shuffler"/> raises, alongside what it logged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The product talks to the outside world through two channels and neither is a superset
    /// of the other, so a fixture that watches only one can miss a regression entirely:
    /// </para>
    /// <list type="bullet">
    /// <item>SendText logs through the container, but only when the formatted message is
    /// longer than one character - so a bare newline reaches the event stream and never the
    /// log.</item>
    /// <item>SendStatus raises an event whose Message is null, carrying only block and record
    /// counters - so progress reporting is invisible to the log.</item>
    /// </list>
    /// <para>
    /// Both channels are therefore recorded, and the assertion helpers name which one they
    /// look at: Logged for the container, Sent for event messages, Raised for events of any
    /// kind including the message-less status ones.
    /// </para>
    /// </remarks>
    public class ShuffleEventRecorder
    {
        private readonly List<ShuffleEventArgs> events = new List<ShuffleEventArgs>();

        public ShuffleEventRecorder(RecordingLogger logger)
        {
            Logger = logger;
        }

        /// <summary>The log side of the pair.</summary>
        public RecordingLogger Logger { get; }

        /// <summary>Every event raised, in order.</summary>
        public IReadOnlyList<ShuffleEventArgs> Events => events;

        /// <summary>Event messages, in order, with the message-less status events dropped.</summary>
        public IReadOnlyList<string> SentMessages =>
            events.Where(e => e.Message != null).Select(e => e.Message).ToList();

        /// <summary>Attaches to a shuffler. Safe to call once per instance.</summary>
        public void Attach(Shuffler shuffler)
        {
            shuffler.RaiseShuffleEvent += OnShuffleEvent;
        }

        public void OnShuffleEvent(object sender, ShuffleEventArgs args)
        {
            events.Add(args);
        }

        #region log channel

        /// <summary>Asserts the container logged something containing <paramref name="fragment"/>.</summary>
        public void AssertLogged(string fragment)
        {
            Assert.That(Logger.Logged(fragment), Is.True,
                "Expected a log message containing \"{0}\". Logged:{1}{2}",
                fragment, Environment.NewLine, Logger.Dump());
        }

        /// <summary>Asserts nothing logged contains <paramref name="fragment"/>.</summary>
        public void AssertNeverLogged(string fragment)
        {
            Assert.That(Logger.Logged(fragment), Is.False,
                "Expected no log message containing \"{0}\", but found {1}. Logged:{2}{3}",
                fragment, Logger.CountLogged(fragment), Environment.NewLine, Logger.Dump());
        }

        /// <summary>Asserts exactly <paramref name="times"/> log messages contain the fragment.</summary>
        public void AssertLoggedTimes(string fragment, int times)
        {
            Assert.That(Logger.CountLogged(fragment), Is.EqualTo(times),
                "Expected \"{0}\" in {1} log messages. Logged:{2}{3}",
                fragment, times, Environment.NewLine, Logger.Dump());
        }

        #endregion

        #region event channel

        /// <summary>Asserts an event message contains <paramref name="fragment"/>.</summary>
        public void AssertSent(string fragment)
        {
            Assert.That(SentMessages.Any(m => m.IndexOf(fragment, StringComparison.Ordinal) >= 0), Is.True,
                "Expected a raised event whose message contains \"{0}\". Sent:{1}{2}",
                fragment, Environment.NewLine, string.Join(Environment.NewLine, SentMessages));
        }

        /// <summary>Asserts no event message contains <paramref name="fragment"/>.</summary>
        public void AssertNeverSent(string fragment)
        {
            Assert.That(SentMessages.Any(m => m.IndexOf(fragment, StringComparison.Ordinal) >= 0), Is.False,
                "Expected no raised event whose message contains \"{0}\". Sent:{1}{2}",
                fragment, Environment.NewLine, string.Join(Environment.NewLine, SentMessages));
        }

        /// <summary>Asserts at least one event was raised matching <paramref name="predicate"/>.</summary>
        public void AssertRaised(Func<ShuffleEventArgs, bool> predicate, string because)
        {
            Assert.That(events.Any(predicate), Is.True,
                "Expected an event: {0}. {1} events were raised, {2} of them with a message.",
                because, events.Count, SentMessages.Count);
        }

        #endregion

        /// <summary>Everything both channels saw - for a failure message worth reading.</summary>
        public string Dump()
        {
            return string.Concat(
                "--- logged ---", Environment.NewLine,
                Logger.Dump(), Environment.NewLine,
                "--- sent ---", Environment.NewLine,
                string.Join(Environment.NewLine, SentMessages));
        }
    }
}
