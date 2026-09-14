namespace Cinteros.Crm.Utils.Shuffle.Tests.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.ServiceModel;
    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.Messages;

    /// <summary>
    /// Builds an <see cref="ExecuteMultipleResponse"/> item by item, so a fixture can
    /// describe exactly which requests in a batch succeeded, which faulted, and which
    /// the platform did not answer at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ExecuteMultipleResponse has no public API for adding results - its Responses and
    /// IsFaulted properties are read-only projections over the Results bag - so this writes
    /// the two well-known keys directly. ExecuteMultipleResponseItem itself has a public
    /// parameterless constructor and public setters on all four properties, so no reflection
    /// is needed.
    /// </para>
    /// <para>
    /// <see cref="Omit"/> is the reason this class exists. The platform is documented to
    /// return one response item per request, but the product code defends against a short
    /// collection anyway; FakeXrmEasy cannot express that, so the scripted service must.
    /// </para>
    /// </remarks>
    public class ExecuteMultipleResponseBuilder
    {
        private readonly List<ExecuteMultipleResponseItem> items = new List<ExecuteMultipleResponseItem>();
        private int next;

        /// <summary>A response item carrying <paramref name="response"/> for the next request.</summary>
        public ExecuteMultipleResponseBuilder Success(OrganizationResponse response)
        {
            items.Add(new ExecuteMultipleResponseItem
            {
                RequestIndex = next++,
                Response = response
            });
            return this;
        }

        /// <summary>A <see cref="CreateResponse"/> carrying <paramref name="id"/>.</summary>
        public ExecuteMultipleResponseBuilder CreatedAt(Guid id)
        {
            var response = new CreateResponse();
            response.Results["id"] = id;
            return Success(response);
        }

        /// <summary>An empty success - what Update, Upsert and Delete return.</summary>
        public ExecuteMultipleResponseBuilder Succeeded()
        {
            return Success(new OrganizationResponse());
        }

        /// <summary>A fault for the next request, with <paramref name="message"/>.</summary>
        public ExecuteMultipleResponseBuilder Fault(string message)
        {
            return Fault(message, -2147220989);
        }

        /// <summary>A fault for the next request, with an explicit error code.</summary>
        public ExecuteMultipleResponseBuilder Fault(string message, int errorCode)
        {
            items.Add(new ExecuteMultipleResponseItem
            {
                RequestIndex = next++,
                Fault = new OrganizationServiceFault { Message = message, ErrorCode = errorCode }
            });
            return this;
        }

        /// <summary>
        /// Advances the request index without adding an item, leaving a hole the product
        /// code has to survive.
        /// </summary>
        public ExecuteMultipleResponseBuilder Omit()
        {
            next++;
            return this;
        }

        /// <summary>
        /// Adds a fault for an explicit request index, out of order - the flush loops look
        /// items up by RequestIndex rather than by position, and that has to keep holding.
        /// </summary>
        public ExecuteMultipleResponseBuilder FaultAt(int requestIndex, string message)
        {
            items.Add(new ExecuteMultipleResponseItem
            {
                RequestIndex = requestIndex,
                Fault = new OrganizationServiceFault { Message = message, ErrorCode = -2147220989 }
            });
            if (requestIndex >= next)
            {
                next = requestIndex + 1;
            }
            return this;
        }

        /// <summary>Adds a success for an explicit request index, out of order.</summary>
        public ExecuteMultipleResponseBuilder SuccessAt(int requestIndex, OrganizationResponse response)
        {
            items.Add(new ExecuteMultipleResponseItem
            {
                RequestIndex = requestIndex,
                Response = response
            });
            if (requestIndex >= next)
            {
                next = requestIndex + 1;
            }
            return this;
        }

        /// <summary>The assembled response.</summary>
        public ExecuteMultipleResponse Build()
        {
            var collection = new ExecuteMultipleResponseItemCollection();
            collection.AddRange(items);

            var response = new ExecuteMultipleResponse();
            response.Results["Responses"] = collection;
            response.Results["IsFaulted"] = items.Exists(i => i.Fault != null);
            return response;
        }

        /// <summary>A fault the platform raises when a message is not implemented at all.</summary>
        /// <remarks>
        /// 0x80040265 is what the product watches for to decide a bulk message is unavailable
        /// and fall back - see MessageNotImplementedErrorCode in ShuffleDataImport.
        /// </remarks>
        public static FaultException<OrganizationServiceFault> MessageNotImplemented(string messageName)
        {
            return new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault
                {
                    Message = $"The request message '{messageName}' is not implemented.",
                    ErrorCode = unchecked((int)0x80040265)
                });
        }

        /// <summary>An ordinary platform fault, as a thrown exception rather than a batch item.</summary>
        public static FaultException<OrganizationServiceFault> Faulted(string message)
        {
            return new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { Message = message, ErrorCode = -2147220989 },
                message);
        }
    }
}
