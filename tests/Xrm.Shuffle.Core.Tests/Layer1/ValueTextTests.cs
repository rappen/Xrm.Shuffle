namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer1
{
    using System;
    using System.Globalization;
    using System.Threading;
    using global::Xrm.Utils.Core.Common.Extensions;
    using global::Xrm.Utils.Core.Common.Misc;
    using NUnit.Framework;

    /// <summary>
    /// ValueText from Xrm.Utils.Core, which writes and reads every value in a data file: the
    /// output must not depend on the machine's culture, and older files written in a
    /// comma-decimal culture must be read correctly or rejected - never misread.
    /// </summary>
    [TestFixture]
    public class ValueTextTests
    {
        private CultureInfo savedCulture;

        [SetUp]
        public void SaveCulture() => savedCulture = Thread.CurrentThread.CurrentCulture;

        [TearDown]
        public void RestoreCulture() => Thread.CurrentThread.CurrentCulture = savedCulture;

        private static void Culture(string name) => Thread.CurrentThread.CurrentCulture = new CultureInfo(name);

        [TestCase("sv-SE")]
        [TestCase("en-US")]
        [TestCase("de-DE")]
        public void Numbers_are_written_the_same_in_every_culture(string culture)
        {
            Culture(culture);

            Assert.That(ValueText.Format(1234.5m), Is.EqualTo("1234.5"), "decimal");
            Assert.That(ValueText.Format(59.33), Is.EqualTo("59.33"), "double");
            Assert.That(ValueText.Format(-42), Is.EqualTo("-42"), "int");
            Assert.That(ValueText.Format(new DateTime(2026, 10, 5, 12, 30, 0, DateTimeKind.Utc)), Is.EqualTo("2026-10-05T12:30:00.0000000Z"), "DateTime");
        }

        [Test]
        public void A_double_round_trips_exactly()
        {
            const double value = 0.1 + 0.2;

            Assert.That(ValueText.ParseDouble(ValueText.Format(value)), Is.EqualTo(value));
        }

        [TestCase("sv-SE")]
        [TestCase("en-US")]
        public void Invariant_numbers_are_read_in_every_culture(string culture)
        {
            Culture(culture);

            Assert.That(ValueText.ParseDecimal("1234.5"), Is.EqualTo(1234.5m));
            Assert.That(ValueText.ParseDouble("59.33"), Is.EqualTo(59.33));
            Assert.That(ValueText.ParseDouble("1E-05"), Is.EqualTo(1e-5));
            Assert.That(ValueText.ParseInt("-42"), Is.EqualTo(-42));
        }

        /// <summary>
        /// Dates in existing data files carry no time zone - 2/25/2013 12:00:00 AM, 2016-09-29
        /// 22:00:00 - and are read as UTC and passed on in the machine's local time. Columns with
        /// date-only or time-zone-independent behaviour depend on that, so it must not change.
        /// </summary>
        [TestCase("2/25/2013 12:00:00 AM", 2013, 2, 25, 0)]
        [TestCase("3/29/2024 11:00:00 PM", 2024, 3, 29, 23)]
        [TestCase("2016-09-29 22:00:00", 2016, 9, 29, 22)]
        [TestCase("2026-10-05T12:00:00.0000000Z", 2026, 10, 5, 12)]
        public void Dates_are_read_as_UTC_and_passed_on_in_local_time(string text, int year, int month, int day, int hour)
        {
            var entity = new Microsoft.Xrm.Sdk.Entity("account");

            entity.SetAttribute(new Helpers.TestExecutionContainer(null), "date", "DateTime", text);

            var value = entity.GetAttributeValue<DateTime>("date");
            Assert.That(value.Kind, Is.EqualTo(DateTimeKind.Local));
            Assert.That(value.ToUniversalTime(), Is.EqualTo(new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Utc)));
        }

        /// <summary>A file exported on a Swedish machine before values were culture-independent.</summary>
        [Test]
        public void An_older_comma_decimal_file_is_read_in_the_culture_that_wrote_it()
        {
            Culture("sv-SE");

            Assert.That(ValueText.ParseDecimal("1234,5"), Is.EqualTo(1234.5m));
        }

        /// <summary>
        /// The case that used to corrupt data: read with thousands separators allowed, 1234,5
        /// became 12345. It now fails with a message naming the value.
        /// </summary>
        [Test]
        public void A_comma_decimal_value_is_rejected_rather_than_misread_where_comma_is_not_decimal()
        {
            Culture("en-US");

            var ex = Assert.Throws<FormatException>(() => ValueText.ParseDecimal("1234,5"));
            Assert.That(ex.Message, Does.Contain("1234,5"));
        }
    }
}
