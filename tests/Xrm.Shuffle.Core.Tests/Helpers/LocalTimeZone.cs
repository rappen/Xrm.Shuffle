namespace Cinteros.Crm.Utils.Shuffle.Tests.Helpers
{
    using System;
    using System.Reflection;

    /// <summary>
    /// Runs a test as if the machine were in another time zone, so date bugs that only show
    /// away from UTC also fail on a UTC build agent.
    /// </summary>
    /// <remarks>
    /// .NET Framework has no supported way to change TimeZoneInfo.Local, so this sets the cached
    /// local zone it reads. Dispose clears the cache, and the next use reads the real zone again.
    /// </remarks>
    public sealed class LocalTimeZone : IDisposable
    {
        private LocalTimeZone(TimeZoneInfo zone)
        {
            TimeZoneInfo.ClearCachedData();
            var cachedData = typeof(TimeZoneInfo)
                .GetField("s_cachedData", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
            cachedData.GetType()
                .GetField("m_localTimeZone", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(cachedData, zone);
        }

        /// <summary>Stockholm: UTC+1, UTC+2 in summer - where the date reports came from.</summary>
        public static LocalTimeZone Stockholm() =>
            new LocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"));

        public void Dispose() => TimeZoneInfo.ClearCachedData();
    }
}
