namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer3
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Xml;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using Cinteros.Crm.Utils.Shuffle.Types;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// Exports from one org, writes the data file, loads it the way the Runner does and imports
    /// it into an empty org - once per serialization style - and checks every column type
    /// arrives with its value.
    /// </summary>
    [TestFixture]
    public class ExportImportRoundTripTests : FakeOrgTestBase
    {
        private static readonly Guid ContactId = new Guid("50000000-0000-0000-0000-000000000000");
        private static readonly Guid AccountId = new Guid("60000000-0000-0000-0000-000000000000");
        private static readonly DateTime LastUsed = new DateTime(2026, 10, 5, 12, 30, 0, DateTimeKind.Utc);

        private string workFolder;

        [SetUp]
        public void CreateWorkFolder()
        {
            workFolder = Path.Combine(Path.GetTempPath(), "ShuffleTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workFolder);
        }

        [TearDown]
        public void DeleteWorkFolder()
        {
            if (Directory.Exists(workFolder))
            {
                Directory.Delete(workFolder, true);
            }
        }

        private static Entity SourceContact()
        {
            var contact = new Entity("contact", ContactId);
            contact["contactid"] = ContactId;
            contact["name"] = "Pat";
            return contact;
        }

        private static Entity SourceAccount()
        {
            var account = new Entity("account", AccountId);
            account["accountid"] = AccountId;
            account["name"] = "Contoso; \"quoted\" & <tagged>";
            account["numberofemployees"] = 42;
            account["revenue"] = new Money(1234.5m);
            account["exchangerate"] = 1.25m;
            account["address1_latitude"] = 59.33;
            account["donotemail"] = true;
            account["industrycode"] = new OptionSetValue(7);
            account["lastusedincampaign"] = LastUsed;
            account["primarycontactid"] = new EntityReference("contact", ContactId) { Name = "Pat" };
            return account;
        }

        private const string Columns =
            "<Attribute Name=\"name\" /><Attribute Name=\"numberofemployees\" /><Attribute Name=\"revenue\" />" +
            "<Attribute Name=\"exchangerate\" /><Attribute Name=\"address1_latitude\" /><Attribute Name=\"donotemail\" />" +
            "<Attribute Name=\"industrycode\" /><Attribute Name=\"lastusedincampaign\" /><Attribute Name=\"primarycontactid\" />";

        private static XmlDocument Definition()
        {
            var xml = new XmlDocument();
            xml.LoadXml(
                "<ShuffleDefinition><Blocks>" +
                "<DataBlock Name=\"Contacts\" Entity=\"contact\"><Export><Attributes><Attribute Name=\"name\" /></Attributes></Export><Import CreateWithId=\"true\" /></DataBlock>" +
                "<DataBlock Name=\"Accounts\" Entity=\"account\"><Export><Attributes>" + Columns + "</Attributes></Export><Import CreateWithId=\"true\" /></DataBlock>" +
                "</Blocks></ShuffleDefinition>");
            return xml;
        }

        /// <summary>Exports, writes the file and loads it back with the Runner's loader.</summary>
        private XmlDocument ExportToFileAndLoad(SerializationType type)
        {
            Online().WithEntity(SourceContact(), SourceAccount());
            var exported = Shuffler.QuickExport(Org.Container, Definition(), type, ';', null);
            Assert.That(exported, Is.Not.Null, "export returned nothing");
            var file = Path.Combine(workFolder, "data_" + type + ".xml");
            exported.Save(file);
            return ShuffleHelper.LoadDataFile(file);
        }

        private static ShuffleTestContext EmptyTarget()
        {
            return ShuffleTestContext.AsOnline()
                .WithMetadata("contact")
                .WithMetadata("account")
                .WithAttributes("account", "numberofemployees", "revenue", "exchangerate", "address1_latitude", "donotemail", "industrycode", "lastusedincampaign", "primarycontactid");
        }

        [TestCase(SerializationType.Simple)]
        [TestCase(SerializationType.SimpleWithValue)]
        [TestCase(SerializationType.Explicit)]
        [TestCase(SerializationType.Full)]
        [TestCase(SerializationType.Text)]
        public void Every_column_type_survives_export_and_import(SerializationType type)
        {
            var data = ExportToFileAndLoad(type);
            var target = EmptyTarget();

            var result = Shuffler.QuickImport(target.Container, Definition(), data, null);

            Assert.That(result.Item5, Is.EqualTo(0), "failed records. " + target.Logger.Dump());
            var account = target.Rows("account").Single();
            var contact = target.Rows("contact").Single();
            var expected = SourceAccount();

            Assert.That(contact.Id, Is.EqualTo(ContactId), "CreateWithId keeps the id");
            Assert.That(account.Id, Is.EqualTo(AccountId), "CreateWithId keeps the id");
            Assert.That(account["name"], Is.EqualTo(expected["name"]), "string with ; \" & <");
            Assert.That(account["numberofemployees"], Is.EqualTo(42), "int");
            Assert.That(account.GetAttributeValue<Money>("revenue")?.Value, Is.EqualTo(1234.5m), "Money");
            Assert.That(account["exchangerate"], Is.EqualTo(1.25m), "decimal");
            Assert.That(Convert.ToDouble(account["address1_latitude"]), Is.EqualTo(59.33).Within(1e-9), "double");
            Assert.That(account["donotemail"], Is.EqualTo(true), "bool");
            Assert.That(account.GetAttributeValue<OptionSetValue>("industrycode")?.Value, Is.EqualTo(7), "OptionSetValue");
            Assert.That(account.GetAttributeValue<DateTime>("lastusedincampaign").ToUniversalTime(), Is.EqualTo(LastUsed), "DateTime");
            var reference = account.GetAttributeValue<EntityReference>("primarycontactid");
            Assert.That(reference?.LogicalName, Is.EqualTo("contact"), "EntityReference entity");
            Assert.That(reference?.Id, Is.EqualTo(ContactId), "EntityReference id");
        }

        /// <summary>
        /// A file exported on a machine in one culture and imported on a machine in another -
        /// a Swedish developer's export run by an English-culture build agent, say - keeps its
        /// numbers. Both used to follow the machine's culture, so 1234,5 came back as 12345.
        /// </summary>
        [TestCase("sv-SE", "en-US", SerializationType.SimpleWithValue)]
        [TestCase("en-US", "sv-SE", SerializationType.SimpleWithValue)]
        [TestCase("sv-SE", "en-US", SerializationType.Text)]
        [TestCase("de-DE", "en-US", SerializationType.Explicit)]
        public void Numbers_survive_a_change_of_culture(string exportCulture, string importCulture, SerializationType type)
        {
            var saved = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(exportCulture);
                var data = ExportToFileAndLoad(type);
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(importCulture);
                var target = EmptyTarget();

                var result = Shuffler.QuickImport(target.Container, Definition(), data, null);

                Assert.That(result.Item5, Is.EqualTo(0), "failed records. " + target.Logger.Dump());
                var account = target.Rows("account").Single();
                Assert.That(account.GetAttributeValue<Money>("revenue")?.Value, Is.EqualTo(1234.5m), "Money");
                Assert.That(account["exchangerate"], Is.EqualTo(1.25m), "decimal");
                Assert.That(Convert.ToDouble(account["address1_latitude"]), Is.EqualTo(59.33).Within(1e-9), "double");
                Assert.That(account.GetAttributeValue<DateTime>("lastusedincampaign").ToUniversalTime(), Is.EqualTo(LastUsed), "DateTime");
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        /// <summary>SimpleNoId leaves the ids out, so the target gets new ones.</summary>
        [Test]
        public void SimpleNoId_creates_records_with_new_ids()
        {
            var data = ExportToFileAndLoad(SerializationType.SimpleNoId);
            var target = EmptyTarget();
            var definition = Definition();
            foreach (XmlElement import in definition.SelectNodes("//Import"))
            {
                import.RemoveAttribute("CreateWithId");
            }

            var result = Shuffler.QuickImport(target.Container, definition, data, null);

            Assert.That(result.Item5, Is.EqualTo(0), target.Logger.Dump());
            Assert.That(target.Rows("account").Single().Id, Is.Not.EqualTo(AccountId));
            Assert.That(target.Rows("account").Single()["name"], Is.EqualTo(SourceAccount()["name"]));
        }

        /// <summary>The data file lists columns alphabetically, so re-exports give stable diffs.</summary>
        [Test]
        public void Exported_columns_are_in_alphabetical_order()
        {
            Online().WithEntity(SourceContact(), SourceAccount());

            var exported = Shuffler.QuickExport(Org.Container, Definition(), SerializationType.Simple, ';', null);

            var names = exported.SelectNodes("/ShuffleData/Block[@Name='Accounts']/Entities/Entity/Attribute")
                .Cast<XmlElement>().Select(a => a.GetAttribute("name")).ToList();
            Assert.That(names, Is.Not.Empty, exported.OuterXml);
            Assert.That(names, Is.Ordered.Using((IComparer<string>)StringComparer.Ordinal), exported.OuterXml);
        }
    }
}
