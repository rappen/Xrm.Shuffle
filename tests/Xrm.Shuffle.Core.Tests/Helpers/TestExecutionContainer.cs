namespace Cinteros.Crm.Utils.Shuffle.Tests.Helpers
{
    using System.Dynamic;
    using Microsoft.Xrm.Sdk;
    using global::Xrm.Utils.Core.Common.Interfaces;

    /// <summary>
    /// A minimal <see cref="IExecutionContainer"/>.
    /// </summary>
    /// <remarks>
    /// The interface is three read-only properties, so this is a stub rather than a mock -
    /// there is no behaviour here worth a mocking framework.
    /// </remarks>
    public class TestExecutionContainer : IExecutionContainer
    {
        public TestExecutionContainer(IOrganizationService service)
            : this(service, new RecordingLogger())
        {
        }

        public TestExecutionContainer(IOrganizationService service, RecordingLogger logger)
        {
            Service = service;
            Recorder = logger;
            // The product reads and writes container.Values as a dynamic bag; ExpandoObject
            // is what CintContainer uses too.
            Values = new ExpandoObject();
        }

        public dynamic Values { get; }

        public ILoggable Logger => Recorder;

        public IOrganizationService Service { get; }

        /// <summary>The same object as <see cref="Logger"/>, typed so tests can read it.</summary>
        public RecordingLogger Recorder { get; }
    }
}
