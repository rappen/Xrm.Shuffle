namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer1
{
    using System;
    using System.Globalization;
    using System.Threading;
    using NUnit.Framework;

    /// <summary>The run time an import or export ends with, written for people to read.</summary>
    [TestFixture]
    public class RunDurationTests
    {
        [TestCase(0, 0, 4.2, "4.2 s")]
        [TestCase(0, 0, 0.06, "0.1 s")]
        [TestCase(0, 1, 58, "1 min 58 s")]
        [TestCase(0, 59, 59, "59 min 59 s")]
        [TestCase(1, 54, 12, "1 h 54 min")]
        [TestCase(26, 5, 0, "26 h 5 min")]
        public void A_run_time_reads_in_the_largest_sensible_units(int hours, int minutes, double seconds, string expected)
        {
            var elapsed = new TimeSpan(hours, minutes, 0) + TimeSpan.FromSeconds(seconds);

            Assert.That(Shuffler.Duration(elapsed), Is.EqualTo(expected));
        }

        [Test]
        public void Seconds_are_written_the_same_in_every_culture()
        {
            var saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("sv-SE");

                Assert.That(Shuffler.Duration(TimeSpan.FromSeconds(4.2)), Is.EqualTo("4.2 s"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }
    }
}
