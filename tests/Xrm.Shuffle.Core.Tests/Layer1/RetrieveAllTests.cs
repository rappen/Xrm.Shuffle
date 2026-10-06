namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer1
{
    using System.Linq;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using global::Xrm.Utils.Core.Common.Extensions;
    using Microsoft.Xrm.Sdk.Query;
    using NUnit.Framework;

    /// <summary>
    /// ContainerExtensions.RetrieveAll from Xrm.Utils.Core, which the PreRetrieveAll snapshot
    /// depends on to get past the 5000-record page.
    /// </summary>
    [TestFixture]
    public class RetrieveAllTests : FakeOrgTestBase
    {
        private void Accounts(int count)
        {
            Online().WithEntity(Enumerable.Range(0, count)
                .Select(i => Seeded("account", Id(1000 + i), "Account " + i))
                .ToArray());
        }

        private static QueryExpression AllAccounts()
        {
            return new QueryExpression("account") { ColumnSet = new ColumnSet("accountid", "name") };
        }

        [Test]
        public void Every_page_is_read()
        {
            const int count = 2 * RecordingOrganizationService.MaxPageSize + 1;
            Accounts(count);

            var all = Org.Container.RetrieveAll(AllAccounts());

            Assert.That(all.Entities.Count, Is.EqualTo(count));
            Assert.That(all.Entities.Select(e => e.Id).Distinct().Count(), Is.EqualTo(count), "no record should be read twice");
            Assert.That(Service.Queries.Count, Is.EqualTo(3));
            Assert.That(all.EntityName, Is.EqualTo("account"));
        }

        [Test]
        public void A_single_page_is_one_request()
        {
            Accounts(3);

            var all = Org.Container.RetrieveAll(AllAccounts());

            Assert.That(all.Entities.Count, Is.EqualTo(3));
            Assert.That(Service.Queries.Count, Is.EqualTo(1));
        }

        /// <summary>TopCount cannot be combined with paging, so such a query is sent as it is.</summary>
        [Test]
        public void A_query_with_TopCount_is_sent_unpaged()
        {
            Accounts(10);
            var query = AllAccounts();
            query.TopCount = 4;

            var all = Org.Container.RetrieveAll(query);

            Assert.That(all.Entities.Count, Is.EqualTo(4));
            Assert.That(Service.Queries.Count, Is.EqualTo(1));
        }
    }
}
