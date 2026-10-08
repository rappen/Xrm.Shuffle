namespace Cinteros.Crm.Utils.Shuffle
{
    using global::Xrm.Utils.Core.Common.Interfaces;
    using Microsoft.Xrm.Sdk;

    /// <summary>
    /// The import's container with its service swapped for one that bypasses custom logic. The
    /// log and values are shared, so the run reads as one.
    /// </summary>
    public sealed class BypassContainer : IExecutionContainer
    {
        private readonly IExecutionContainer inner;

        public BypassContainer(IExecutionContainer inner, IOrganizationService service)
        {
            this.inner = inner;
            Service = service;
        }

        public dynamic Values => inner.Values;

        public ILoggable Logger => inner.Logger;

        public IOrganizationService Service { get; }
    }
}
