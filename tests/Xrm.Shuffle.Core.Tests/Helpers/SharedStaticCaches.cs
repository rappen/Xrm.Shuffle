namespace Cinteros.Crm.Utils.Shuffle.Tests.Helpers
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.Caching;

    /// <summary>
    /// Clears the process-wide caches in Xrm.Utils.Core between tests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three static caches outlive a fixture and would otherwise carry answers from one test
    /// into the next: the primary-id and primary-name attribute lookups, which exist twice
    /// (once on the extension methods, once on the fluent API), and the metadata MemoryCache
    /// the container extensions keep with a five-minute sliding expiry. All are private, so
    /// reflection is the only way in; all are readonly, so the MemoryCache is drained
    /// key by key rather than replaced.
    /// </para>
    /// <para>
    /// Without this, a test that seeds a fake org with one entity shape can be answered from
    /// a cache another test populated, and the result depends on execution order. With it,
    /// the order stops mattering.
    /// </para>
    /// </remarks>
    public static class SharedStaticCaches
    {
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private static readonly Lazy<IReadOnlyList<FieldInfo>> AttributeCaches =
            new Lazy<IReadOnlyList<FieldInfo>>(ResolveAttributeCaches);

        private static readonly Lazy<FieldInfo> MetadataCache = new Lazy<FieldInfo>(ResolveMetadataCache);

        /// <summary>Empties every cache. Call from [SetUp].</summary>
        public static void Reset()
        {
            foreach (var field in AttributeCaches.Value)
            {
                var dictionary = (ConcurrentDictionary<string, string>)field.GetValue(null);
                dictionary.Clear();
            }

            var cache = (MemoryCache)MetadataCache.Value.GetValue(null);
            // MemoryCache has no Clear, and the field is readonly so it cannot be replaced.
            // Snapshot the keys first: removing while enumerating the cache itself is not safe.
            foreach (var key in cache.Select(entry => entry.Key).ToList())
            {
                cache.Remove(key);
            }
        }

        /// <summary>
        /// Proves every cache was found. Call from [OneTimeSetUp].
        /// </summary>
        /// <remarks>
        /// Load-bearing: reflection by name fails silently if the submodule renames a type or
        /// field, and a silent failure here turns Reset into a no-op, which shows up much
        /// later as a test that only fails when the suite runs in a particular order.
        /// </remarks>
        public static void AssertResolved()
        {
            if (AttributeCaches.Value.Count != 4)
            {
                throw new InvalidOperationException(
                    "Expected 4 attribute-name caches in Xrm.Utils.Core, found " + AttributeCaches.Value.Count +
                    ". A type or field was renamed, and clearing them has become a no-op.");
            }

            if (MetadataCache.Value == null)
            {
                throw new InvalidOperationException(
                    "The metadata MemoryCache field was not found in ContainerExtensions.");
            }
        }

        private static IReadOnlyList<FieldInfo> ResolveAttributeCaches()
        {
            var names = new[] { "PrimaryIdAttributes", "PrimaryNameAttributes" };
            var types = new[]
            {
                typeof(global::Xrm.Utils.Core.Common.Extensions.EntityExtensions),
                typeof(global::Xrm.Utils.Core.Common.Fluent.Entity.OperationsSet2)
            };

            return types
                .SelectMany(type => names.Select(name => type.GetField(name, PrivateStatic)))
                .Where(field => field != null)
                .ToList();
        }

        private static FieldInfo ResolveMetadataCache()
        {
            return typeof(global::Xrm.Utils.Core.Common.Extensions.ContainerExtensions)
                .GetField("cache", PrivateStatic);
        }
    }
}
