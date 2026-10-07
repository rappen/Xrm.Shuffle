namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer1
{
    using System;
    using System.Globalization;
    using System.Threading;
    using global::Xrm.Utils.Core.Common.Extensions;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// Entity.AttributeAsString from Xrm.Utils.Core, which every import log line and record label
    /// goes through.
    /// </summary>
    /// <remarks>
    /// Runs under sv-SE so the culture-sensitive cases are pinned to one culture: the decimal
    /// comma and the non-breaking space as group separator.
    /// </remarks>
    [TestFixture]
    public class AttributeAsStringTests
    {
        private CultureInfo savedCulture;
        private Entity record;

        private static readonly string NumberGroup = new CultureInfo("sv-SE").NumberFormat.NumberGroupSeparator;

        [SetUp]
        public void SwedishRecord()
        {
            savedCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("sv-SE");

            record = new Entity("asyncoperation", new Guid("11111111-1111-1111-1111-111111111111"));
            record["statuscode"] = new OptionSetValue(20);
            record.FormattedValues["statuscode"] = "Pågår";
            record["nolabel"] = new OptionSetValue(7);
            record["amount"] = new Money(1234.5m);
            record.FormattedValues["amount"] = "1 234,50 kr";
            record["created"] = new DateTime(2026, 10, 5, 14, 30, 0);
            record["count"] = 42;
            record["numtext"] = "1234.5";
            record["datetext"] = "2026-10-05";
            record["plaintext"] = "hello";
            record["flag"] = true;
            record["owner"] = new EntityReference("systemuser", new Guid("22222222-2222-2222-2222-222222222222"));
            record["parent"] = new EntityReference("account", Guid.NewGuid()) { Name = "Contoso" };
            record["multi"] = new OptionSetValueCollection { new OptionSetValue(1), new OptionSetValue(3) };
            record["nullvalue"] = null;
            record["alias.statuscode"] = new AliasedValue("asyncoperation", "statuscode", new OptionSetValue(30));
        }

        [TearDown]
        public void RestoreCulture()
        {
            Thread.CurrentThread.CurrentCulture = savedCulture;
        }

        [TestCase("statuscode", null, "Pågår", TestName = "Label from FormattedValues")]
        [TestCase("statuscode", "<value>", "20", TestName = "Raw value with <value>")]
        [TestCase("statuscode", "<value>D3", "020", TestName = "Raw value formatted")]
        [TestCase("statuscode", "Status: {0}", "Status: Pågår", TestName = "Composite format around the label")]
        [TestCase("nolabel", null, "7", TestName = "OptionSetValue without a label")]
        [TestCase("amount", null, "1 234,50 kr", TestName = "Money label")]
        [TestCase("amount", "<value>", "1234,5", TestName = "Money raw value")]
        [TestCase("created", "yyyy-MM-dd", "2026-10-05", TestName = "DateTime format")]
        [TestCase("count", null, "42", TestName = "Int")]
        [TestCase("count", "D5", "00042", TestName = "Int format")]
        [TestCase("datetext", "dd MMM yyyy", "05 okt 2026", TestName = "Text holding a date is formatted")]
        [TestCase("plaintext", "N2", "hello", TestName = "Text that is no number or date is left alone")]
        [TestCase("plaintext", "[{0}]", "[hello]", TestName = "Composite format around text")]
        [TestCase("flag", null, "True", TestName = "Bool")]
        [TestCase("flag", "N2", "True", TestName = "Bool ignores a numeric format")]
        [TestCase("owner", null, "systemuser:22222222-2222-2222-2222-222222222222", TestName = "Unnamed reference is logicalname:id")]
        [TestCase("parent", null, "Contoso", TestName = "Named reference is its name")]
        [TestCase("parent", "-> {0}", "-> Contoso", TestName = "Composite format around a reference")]
        [TestCase("multi", null, "1,3", TestName = "Multi-select is its values")]
        [TestCase("alias.statuscode", null, "30", TestName = "AliasedValue is unwrapped")]
        public void Formats(string attribute, string format, string expected)
        {
            Assert.That(record.AttributeAsString(attribute, "<def>", true, format), Is.EqualTo(expected));
        }

        [Test]
        public void Money_with_a_numeric_format_uses_the_value_not_the_label()
        {
            Assert.That(record.AttributeAsString("amount", null, true, "N2"), Is.EqualTo("1" + NumberGroup + "234,50"));
        }

        [Test]
        public void Text_holding_a_number_is_formatted_as_one()
        {
            Assert.That(record.AttributeAsString("numtext", null, true, "N1"), Is.EqualTo("1" + NumberGroup + "234,5"));
        }

        [TestCase("nullvalue", TestName = "A null value gives the default")]
        [TestCase("missing", TestName = "A missing attribute gives the default when errors are suppressed")]
        public void Default(string attribute)
        {
            Assert.That(record.AttributeAsString(attribute, "<def>", true, "N2"), Is.EqualTo("<def>"));
        }

        [Test]
        public void A_missing_attribute_throws_unless_errors_are_suppressed()
        {
            Assert.Throws<InvalidPluginExecutionException>(() => record.AttributeAsString("missing"));
        }
    }
}
