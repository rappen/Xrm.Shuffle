namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer2
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// Exporting a data block through <see cref="Shuffler.QuickExport(Xrm.Utils.Core.Common.Interfaces.IExecutionContainer, XmlDocument, Types.SerializationType, char, EventHandler{ShuffleEventArgs})"/>,
    /// the entry point the Runner and the pipeline task use: which records are selected and which
    /// columns come with them.
    /// </summary>
    [TestFixture]
    public class DataExportTests : FakeOrgTestBase
    {
        private static Entity Account(int seed, string name, int state = 0, string number = null)
        {
            var account = Seeded("account", Id(seed), name);
            account["statecode"] = new OptionSetValue(state);
            if (number != null)
            {
                account["accountnumber"] = number;
            }
            return account;
        }

        private static Entity Contact(int seed, string name, Guid parentAccount)
        {
            var contact = Seeded("contact", Id(seed), name);
            contact["parentcustomerid"] = new EntityReference("account", parentAccount);
            return contact;
        }

        private static XmlDocument Definition(string blocks)
        {
            var xml = new XmlDocument();
            xml.LoadXml("<ShuffleDefinition><Blocks>" + blocks + "</Blocks></ShuffleDefinition>");
            return xml;
        }

        private static string AccountBlock(string export) =>
            "<DataBlock Name=\"Accounts\" Entity=\"account\"><Export" + export + "</Export></DataBlock>";

        private XmlDocument Export(string blocks)
        {
            return Shuffler.QuickExport(Org.Container, Definition(blocks), Types.SerializationType.Simple, ';', null);
        }

        private static List<XmlElement> Records(XmlDocument data, string block)
        {
            return data.SelectNodes($"/ShuffleData/Block[@Name='{block}']/Entities/Entity").Cast<XmlElement>().ToList();
        }

        private static string Value(XmlElement record, string attribute)
        {
            return (record.SelectSingleNode($"Attribute[@name='{attribute}']") as XmlElement)?.InnerText;
        }

        private static List<string> Names(XmlDocument data, string block) =>
            Records(data, block).Select(r => Value(r, "name")).ToList();

        [Test]
        public void Only_the_listed_columns_are_exported()
        {
            Online().WithEntity(Account(1, "Alpha", number: "A-1"), Account(2, "Beta", number: "B-2"));

            var data = Export(AccountBlock("><Attributes><Attribute Name=\"name\" /></Attributes>"));

            var records = Records(data, "Accounts");
            Assert.That(records.Count, Is.EqualTo(2), data.OuterXml);
            Assert.That(records.All(r => Value(r, "accountnumber") == null), "accountnumber was not listed. " + data.OuterXml);
            Assert.That(Names(data, "Accounts"), Is.EquivalentTo(new[] { "Alpha", "Beta" }));
            Assert.That(records.Select(r => r.GetAttribute("id")), Is.EquivalentTo(new[] { Id(1).ToString(), Id(2).ToString() }), "each record carries its id");
        }

        [Test]
        public void A_wildcard_selects_the_matching_columns()
        {
            Online().WithEntity(Account(1, "Alpha", number: "A-1"));

            var data = Export(AccountBlock("><Attributes><Attribute Name=\"account*\" /></Attributes>"));

            var record = Records(data, "Accounts").Single();
            Assert.That(Value(record, "accountnumber"), Is.EqualTo("A-1"), data.OuterXml);
            Assert.That(Value(record, "name"), Is.Null, "name does not match account*. " + data.OuterXml);
        }

        [Test]
        public void ActiveOnly_skips_inactive_records()
        {
            Online().WithEntity(Account(1, "Active"), Account(2, "Inactive", state: 1));

            var data = Export(AccountBlock(" ActiveOnly=\"true\"><Attributes><Attribute Name=\"name\" /></Attributes>"));

            Assert.That(Names(data, "Accounts"), Is.EqualTo(new[] { "Active" }), data.OuterXml);
        }

        [Test]
        public void A_filter_selects_matching_records()
        {
            Online().WithEntity(Account(1, "Alpha"), Account(2, "Beta"), Account(3, "Gamma"));

            var data = Export(AccountBlock("><Filter Attribute=\"name\" Operator=\"Equal\" Type=\"string\" Value=\"Beta\" /><Attributes><Attribute Name=\"name\" /></Attributes>"));

            Assert.That(Names(data, "Accounts"), Is.EqualTo(new[] { "Beta" }), data.OuterXml);
        }

        [Test]
        public void Sort_orders_the_records()
        {
            Online().WithEntity(Account(1, "Beta"), Account(2, "Alpha"), Account(3, "Gamma"));

            var data = Export(AccountBlock("><Sort Attribute=\"name\" Type=\"Desc\" /><Attributes><Attribute Name=\"name\" /></Attributes>"));

            Assert.That(Names(data, "Accounts"), Is.EqualTo(new[] { "Gamma", "Beta", "Alpha" }), data.OuterXml);
        }

        /// <summary>
        /// A Relation limits a block to records whose lookup points at a record another block
        /// exported - here, only Alpha's contacts, because only Alpha passed the accounts filter.
        /// </summary>
        [Test]
        public void A_relation_limits_the_block_to_records_related_to_another_blocks_records()
        {
            Online().WithEntity(
                Account(1, "Alpha"), Account(2, "Beta"),
                Contact(11, "Alpha contact", Id(1)), Contact(12, "Beta contact", Id(2)));
            Service.WithAttributeType("contact", "parentcustomerid", Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.Customer);

            var data = Export(
                AccountBlock("><Filter Attribute=\"name\" Operator=\"Equal\" Type=\"string\" Value=\"Alpha\" /><Attributes><Attribute Name=\"name\" /></Attributes>") +
                "<DataBlock Name=\"Contacts\" Entity=\"contact\"><Export><Attributes><Attribute Name=\"name\" /><Attribute Name=\"parentcustomerid\" /></Attributes></Export>" +
                "<Relation Block=\"Accounts\" Attribute=\"parentcustomerid\" /></DataBlock>");

            Assert.That(Names(data, "Contacts"), Is.EqualTo(new[] { "Alpha contact" }), data.OuterXml);
        }

        [Test]
        public void FetchXML_mode_uses_the_query_as_given()
        {
            Online().WithEntity(Account(1, "Alpha"), Account(2, "Beta"));
            var fetch = "<fetch><entity name='account'><attribute name='name' /><filter><condition attribute='name' operator='eq' value='Beta' /></filter></entity></fetch>";

            var data = Export("<DataBlock Name=\"Accounts\" Entity=\"account\"><Export><FetchXML>" + System.Security.SecurityElement.Escape(fetch) + "</FetchXML></Export></DataBlock>");

            Assert.That(Names(data, "Accounts"), Is.EqualTo(new[] { "Beta" }), data.OuterXml);
        }

        /// <summary>
        /// Dataverse returns at most 5000 records per query, so the export has to follow the
        /// paging cookie. A single query cut a 5000+ table off silently.
        /// </summary>
        [Test]
        public void An_export_past_the_first_page_of_5000_records_is_complete()
        {
            const int count = RecordingOrganizationService.MaxPageSize + 50;
            Online().WithEntity(Enumerable.Range(0, count).Select(i => Account(1000 + i, "Account " + i)).ToArray());

            var data = Export(AccountBlock("><Attributes><Attribute Name=\"name\" /></Attributes>"));

            Assert.That(Records(data, "Accounts").Count, Is.EqualTo(count));
        }

        [Test]
        public void A_FetchXML_export_past_the_first_page_of_5000_records_is_complete()
        {
            const int count = RecordingOrganizationService.MaxPageSize + 50;
            Online().WithEntity(Enumerable.Range(0, count).Select(i => Account(1000 + i, "Account " + i)).ToArray());
            var fetch = "<fetch><entity name='account'><attribute name='name' /></entity></fetch>";

            var data = Export("<DataBlock Name=\"Accounts\" Entity=\"account\"><Export><FetchXML>" + System.Security.SecurityElement.Escape(fetch) + "</FetchXML></Export></DataBlock>");

            Assert.That(Records(data, "Accounts").Count, Is.EqualTo(count));
        }
    }
}
