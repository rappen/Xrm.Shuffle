namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer1
{
    using System;
    using System.Globalization;
    using System.Threading;
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
