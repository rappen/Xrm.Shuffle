namespace Cinteros.Crm.Utils.Shuffle.Tests.Regressions
{
    using System;
    using System.Linq;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using NUnit.Framework;

    /// <summary>
    /// What reaches the log when a record identifier happens to look like a format string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every progress line in import and export goes through SendText, which formats the message
    /// with its arguments and then hands the result to two places - the log, and the ShuffleEvent
    /// stream the UI renders. Before this PR it handed the arguments along with it, so the logger
    /// formatted the already-formatted string a second time.
    /// </para>
    /// <para>
    /// That was invisible for as long as no record carried a brace. It is not invisible for a
    /// record named after a template: an identifier holding a placeholder was rewritten with a
    /// value from the same line, and one holding a lone brace threw FormatException out of a log
    /// call, failing an import for a reason that had nothing to do with the data.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class SendTextFormattingTests : ShuffleTestBase
    {
        [Test]
        public void Arguments_are_applied_to_the_format_string()
        {
            NewShuffler().TestSendLine("{0:000} Created: {1}", 1, "Alpha");

            Recorder.AssertLogged("001 Created: Alpha");
        }

        /// <summary>
        /// The regression. The identifier carries a placeholder of its own, and the second format
        /// pass filled it in from this same line - so the log claimed a record name that never
        /// existed in the source file.
        /// </summary>
        [Test]
        public void A_value_containing_a_placeholder_is_logged_verbatim()
        {
            NewShuffler().TestSendLine("{0:000} Created: {1}", 1, "Batch {0} rows");

            Recorder.AssertLogged("001 Created: Batch {0} rows");
            Recorder.AssertNeverLogged("Batch 1 rows");
        }

        /// <summary>
        /// The louder half of the same bug: a lone brace is not a placeholder, so the second pass
        /// threw rather than misreporting. The throw escaped into the per-record catch and the
        /// record was counted as failed.
        /// </summary>
        [Test]
        public void A_value_containing_an_unmatched_brace_does_not_throw()
        {
            var shuffler = NewShuffler();

            Assert.DoesNotThrow(() => shuffler.TestSendLine("{0:000} Created: {1}", 1, "Rate { high"));

            Recorder.AssertLogged("001 Created: Rate { high");
        }

        [Test]
        public void A_value_containing_a_placeholder_reaches_the_event_stream_unchanged()
        {
            NewShuffler().TestSendLine("{0:000} Created: {1}", 1, "Batch {0} rows");

            Recorder.AssertSent("001 Created: Batch {0} rows");
            Recorder.AssertNeverSent("Batch 1 rows");
        }

        [Test]
        public void A_message_with_no_arguments_is_logged_as_written()
        {
            NewShuffler().TestSendLine("Pre-retrieved 12 records for matching");

            Recorder.AssertLogged("Pre-retrieved 12 records for matching");
        }

        /// <summary>
        /// Landmine marker, not a fix. The first format pass still runs when there are no
        /// arguments at all, so an already-interpolated message carrying a brace throws.
        /// </summary>
        /// <remarks>
        /// This is reachable today: the per-record catch in ImportDataBlock reports failures as
        /// an interpolated string built from the record identifier, with no arguments. A record
        /// whose identifier holds a brace therefore throws out of the error report itself. If a
        /// later change makes SendText skip the format pass for an empty argument list, this test
        /// starts failing - and the right answer then is to delete it, not to restore the throw.
        /// </remarks>
        [Test]
        public void An_interpolated_message_carrying_a_brace_still_throws_with_no_arguments()
        {
            var shuffler = NewShuffler();

            Assert.Throws<FormatException>(() => shuffler.TestSendLine("*** Error record: Rate { high ***"));
        }

        /// <summary>
        /// The length guard in SendText drops anything shorter than two characters, and a bare
        /// newline is exactly one - so the blank lines that space the log out never reach it.
        /// </summary>
        [Test]
        public void A_bare_newline_reaches_the_event_stream_but_not_the_log()
        {
            NewShuffler().TestSendBlankLine();

            Assert.That(Recorder.Logger.Messages.Count, Is.EqualTo(0), DumpAll());
            Assert.That(Recorder.Events.Count, Is.EqualTo(1), DumpAll());
        }

        /// <summary>
        /// One call, two events: the text and the newline that terminates it. Worth pinning
        /// because the UI counts events, and a change that merged the two would halve them.
        /// </summary>
        [Test]
        public void SendLine_raises_the_text_and_the_newline_as_two_events()
        {
            NewShuffler().TestSendLine("{0:000} Created: {1}", 1, "Alpha");

            Assert.That(Recorder.Events.Count, Is.EqualTo(2), DumpAll());
            Assert.That(Recorder.Logger.Messages.Count, Is.EqualTo(1), DumpAll());
        }

        [Test]
        public void The_log_and_the_event_stream_agree_on_the_formatted_text()
        {
            NewShuffler().TestSendLine("{0:000} Created: {1}", 1, "Batch {0} rows");

            var logged = Recorder.Logger.Messages.First();
            Assert.That(Recorder.SentMessages, Has.Member(logged), DumpAll());
        }
    }
}
