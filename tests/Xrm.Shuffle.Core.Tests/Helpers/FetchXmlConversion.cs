namespace Cinteros.Crm.Utils.Shuffle.Tests.Helpers
{
    using Microsoft.Crm.Sdk.Messages;
    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.Query;

    /// <summary>
    /// Answers the one message the product only sends in a Debug build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two log lines in <c>ShuffleDataImport</c> sit inside <c>#if DEBUG</c> and print the match
    /// query as FetchXml, which <c>ConvertToFetchXml</c> obtains by sending a
    /// QueryExpressionToFetchXmlRequest. FakeXrmEasy 1.x has no executor for it and throws, and
    /// the scripted service throws for anything unscripted, so under Debug every fixture that
    /// reaches a matched block died before the match query ran. Release compiled the lines out,
    /// which is why the suite was green there and red in Visual Studio.
    /// </para>
    /// <para>
    /// The product uses the returned string for nothing but the log line, so the answer here is
    /// a token rather than a real conversion - modelling the platform FetchXml writer would add
    /// a lot of surface for no assertion. The request is deliberately not recorded: keeping it
    /// out of the request list is what makes a fixture see the same sequence of calls in both
    /// configurations.
    /// </para>
    /// </remarks>
    internal static class FetchXmlConversion
    {
        /// <summary>The untyped name the request arrives under.</summary>
        internal const string MessageName = "QueryExpressionToFetchXml";

        /// <summary>True when this request is the Debug-only conversion.</summary>
        internal static bool IsConversion(OrganizationRequest request)
        {
            return request != null && request.RequestName == MessageName;
        }

        /// <summary>A response carrying enough FetchXml to log, named after the query entity.</summary>
        internal static OrganizationResponse Answer(OrganizationRequest request)
        {
            var query = request.Parameters.Contains("Query")
                ? request.Parameters["Query"] as QueryExpression
                : null;
            var response = new QueryExpressionToFetchXmlResponse();
            response.Results["FetchXml"] = string.Format(
                @"<fetch><entity name=""{0}"" /></fetch>",
                query == null ? "unknown" : query.EntityName);
            return response;
        }
    }
}
