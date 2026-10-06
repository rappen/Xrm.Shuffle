namespace Cinteros.Crm.Utils.Shuffle
{
    using Microsoft.Xrm.Sdk;
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The target records of a PreRetrieveAll block, looked up by their match values.
    /// </summary>
    /// <remarks>
    /// The import only ever asks for the records matching one source record, so this is all it
    /// depends on. The in-memory implementation is enough for the table sizes PreRetrieveAll is
    /// meant for; a disk-backed one can replace it without touching the import.
    /// </remarks>
    internal interface IPreRetrievedMatches
    {
        /// <summary>Number of target records retrieved.</summary>
        int Count { get; }

        /// <summary>The target records whose match values equal those of <paramref name="source"/>, in retrieval order.</summary>
        EntityCollection Find(Entity source);
    }

    /// <summary>
    /// Indexes the target records once by match key, so each source record costs one lookup
    /// instead of a scan of every target record.
    /// </summary>
    internal sealed class InMemoryPreRetrievedMatches : IPreRetrievedMatches
    {
        private static readonly IReadOnlyList<Entity> None = new Entity[0];

        private readonly Dictionary<string, List<Entity>> index = new Dictionary<string, List<Entity>>(StringComparer.Ordinal);
        private readonly Func<Entity, string> sourceKey;

        /// <param name="targets">The retrieved target records.</param>
        /// <param name="targetKey">Match key of a target record.</param>
        /// <param name="sourceKey">Match key of a source record. It differs from the target key only in how the primary key is read.</param>
        public InMemoryPreRetrievedMatches(IEnumerable<Entity> targets, Func<Entity, string> targetKey, Func<Entity, string> sourceKey)
        {
            this.sourceKey = sourceKey;
            foreach (var target in targets)
            {
                Count++;
                var key = targetKey(target);
                if (!index.TryGetValue(key, out var records))
                {
                    records = new List<Entity>(1);
                    index.Add(key, records);
                }
                records.Add(target);
            }
        }

        public int Count { get; }

        /// <summary>Number of distinct match keys among the target records.</summary>
        public int KeyCount => index.Count;

        public EntityCollection Find(Entity source)
        {
            var result = new EntityCollection();
            result.Entities.AddRange(index.TryGetValue(sourceKey(source), out var records) ? records : None);
            return result;
        }
    }
}
