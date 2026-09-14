namespace Cinteros.Crm.Utils.Shuffle.Tests.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using global::Xrm.Utils.Core.Common.Interfaces;

    /// <summary>
    /// An <see cref="ILoggable"/> that keeps everything in memory.
    /// </summary>
    /// <remarks>
    /// The only concrete container in the product, CintContainer, logs to
    /// C:\Temp\&lt;AssemblyName&gt; through FileLogger - so tests must never use it.
    /// This records instead of writing, and tracks section depth so a fixture can
    /// assert that a section was opened and closed rather than left dangling.
    /// </remarks>
    public class RecordingLogger : ILoggable
    {
        private readonly List<string> messages = new List<string>();
        private readonly List<Exception> exceptions = new List<Exception>();
        private readonly List<string> sections = new List<string>();

        /// <summary>Every message logged, in order, already formatted.</summary>
        public IReadOnlyList<string> Messages => messages;

        /// <summary>Every exception logged, in order.</summary>
        public IReadOnlyList<Exception> Exceptions => exceptions;

        /// <summary>Names of the sections currently open, outermost first.</summary>
        public IReadOnlyList<string> OpenSections => sections;

        /// <summary>How deeply sections are nested right now.</summary>
        public int SectionDepth => sections.Count;

        /// <summary>True once <see cref="CloseLog()"/> has been called.</summary>
        public bool Closed { get; private set; }

        /// <summary>Text passed to <see cref="CloseLog(string)"/>, if any.</summary>
        public string CloseText { get; private set; }

        public void CloseLog()
        {
            Closed = true;
        }

        public void CloseLog(string closetext)
        {
            CloseText = closetext;
            Closed = true;
        }

        public void EndSection()
        {
            // Not an assertion: the product calls EndSection from finally blocks and can
            // outdent past zero on an error path. Swallowing here keeps a fixture's real
            // failure visible instead of masking it with a helper crash.
            if (sections.Count > 0)
            {
                sections.RemoveAt(sections.Count - 1);
            }
        }

        public void Log(string message)
        {
            messages.Add(message);
        }

        public void Log(Exception ex)
        {
            exceptions.Add(ex);
        }

        public void Log(string message, params object[] arg)
        {
            // Matches the product's own formatting: Logger.Log(message, args) goes through
            // string.Format, so a message containing stray braces throws there too.
            messages.Add(arg == null || arg.Length == 0
                ? message
                : string.Format(CultureInfo.InvariantCulture, message, arg));
        }

        public void StartSection(string name = null)
        {
            sections.Add(name);
        }

        /// <summary>True if any logged message contains <paramref name="fragment"/>.</summary>
        public bool Logged(string fragment) =>
            messages.Any(m => m != null && m.IndexOf(fragment, StringComparison.Ordinal) >= 0);

        /// <summary>How many logged messages contain <paramref name="fragment"/>.</summary>
        public int CountLogged(string fragment) =>
            messages.Count(m => m != null && m.IndexOf(fragment, StringComparison.Ordinal) >= 0);

        /// <summary>All messages joined by newlines - for assertion failure output.</summary>
        public string Dump() => string.Join(Environment.NewLine, messages);
    }
}
