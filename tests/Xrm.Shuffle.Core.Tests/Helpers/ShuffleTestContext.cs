namespace Cinteros.Crm.Utils.Shuffle.Tests.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using FakeXrmEasy;
    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.Metadata;

    /// <summary>
    /// A fake Dataverse organization, shaped like one of the two estates this feature runs
    /// against, wrapped in the execution container the import code expects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The distinction that matters is capability. <see cref="AsOnline"/> answers the
    /// <c>sdkmessagefilter</c> probe for every entity it is given, so blocks route onto
    /// <c>CreateMultiple</c> and friends; <see cref="AsOnPrem"/> answers nothing, so the same
    /// block falls back to <c>ExecuteMultiple</c>. Those are the two answers the real estates
    /// give - ImransDev reports bulk support, MMSTEST2 does not.
    /// </para>
    /// <para>
    /// The probe query filters <c>primaryobjecttypecode</c> by the entity's <em>logical name</em>
    /// (<c>ShuffleDataImport.cs</c>, <c>IsBulkMessageSupported</c>), although on a real platform
    /// that column holds a numeric entity type code. The seed here matches the product rather
    /// than the platform, deliberately, and a test in <c>CapabilityDetectionTests</c> marks the
    /// spot so a fix cannot land silently.
    /// </para>
    /// <para>
    /// Nothing may be seeded after the container has been handed out: FakeXrmEasy takes its
    /// data in one <c>Initialize</c> call, so a fixture that seeded late would be asserting
    /// against an org that does not contain what it just added.
    /// </para>
    /// </remarks>
    public class ShuffleTestContext
    {
        /// <summary>The messages the product probes for, in the order it tries them.</summary>
        public static readonly string[] BulkMessages = { "CreateMultiple", "UpdateMultiple", "UpsertMultiple", "Upsert" };

        private readonly XrmFakedContext faked = new XrmFakedContext();
        private readonly List<Entity> seed = new List<Entity>();
        private readonly Dictionary<string, EntityMetadata> metadata =
            new Dictionary<string, EntityMetadata>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> extraAttributes =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<KeyValuePair<string, string>> supported =
            new List<KeyValuePair<string, string>>();
        private readonly Dictionary<string, Guid> messageIds =
            new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        private readonly bool bulkByDefault;
        private RecordingOrganizationService service;
        private TestExecutionContainer container;

        private ShuffleTestContext(bool bulkByDefault)
        {
            this.bulkByDefault = bulkByDefault;
            faked.ValidateReferences = false;
        }

        /// <summary>An estate with no bulk messages at all - the MMSTEST2 shape.</summary>
        public static ShuffleTestContext AsOnPrem()
        {
            return new ShuffleTestContext(false);
        }

        /// <summary>An estate where every seeded entity supports every bulk message - the ImransDev shape.</summary>
        public static ShuffleTestContext AsOnline()
        {
            return new ShuffleTestContext(true);
        }

        /// <summary>Seeds rows. Their logical names also get default metadata if none was given.</summary>
        public ShuffleTestContext WithEntity(params Entity[] entities)
        {
            RefuseIfBuilt();
            foreach (var entity in entities)
            {
                seed.Add(entity);
                WithMetadata(entity.LogicalName);
            }
            return this;
        }

        /// <summary>
        /// Declares an entity's metadata: the primary id and name attributes, which are the only
        /// two the import path reads, plus the attribute list the fake org validates queries
        /// against.
        /// </summary>
        /// <remarks>
        /// The attribute list is not optional. Once an entity has metadata, FakeXrmEasy answers
        /// any query naming an attribute that metadata does not declare with "The attribute X
        /// does not exist on this entity" - even when every row carries it. The list is assembled
        /// in <c>EnsureBuilt</c> from the seeded rows plus whatever
        /// <see cref="WithAttributes"/> adds, so an attribute only the source records carry has
        /// to be declared.
        /// </remarks>
        public ShuffleTestContext WithMetadata(string logicalName, string primaryIdAttribute = null, string primaryNameAttribute = "name")
        {
            RefuseIfBuilt();
            if (metadata.ContainsKey(logicalName))
            {
                return this;
            }
            var entity = new EntityMetadata { LogicalName = logicalName };
            SetSealed(entity, "PrimaryIdAttribute", primaryIdAttribute ?? logicalName + "id");
            SetSealed(entity, "PrimaryNameAttribute", primaryNameAttribute);
            metadata[logicalName] = entity;
            if (bulkByDefault)
            {
                SupportsMessage(logicalName, BulkMessages);
            }
            return this;
        }

        /// <summary>
        /// Declares attributes beyond the ones the seeded rows carry - an attribute that only the
        /// source records hold, for one, since a query naming it still has to pass validation.
        /// </summary>
        public ShuffleTestContext WithAttributes(string logicalName, params string[] attributes)
        {
            RefuseIfBuilt();
            WithMetadata(logicalName);
            HashSet<string> declared;
            if (!extraAttributes.TryGetValue(logicalName, out declared))
            {
                declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                extraAttributes[logicalName] = declared;
            }
            foreach (var attribute in attributes)
            {
                declared.Add(attribute);
            }
            return this;
        }

        /// <summary>Answers the capability probe yes for these messages on this entity, and no for the rest.</summary>
        public ShuffleTestContext SupportsMessage(string entityLogicalName, params string[] messages)
        {
            RefuseIfBuilt();
            foreach (var message in messages)
            {
                supported.Add(new KeyValuePair<string, string>(entityLogicalName, message));
            }
            return this;
        }

        /// <summary>The container to hand to the shuffler.</summary>
        public TestExecutionContainer Container
        {
            get { EnsureBuilt(); return container; }
        }

        /// <summary>The service the container wraps, for asserting what was actually sent.</summary>
        public RecordingOrganizationService Service
        {
            get { EnsureBuilt(); return service; }
        }

        /// <summary>The log, for asserting the routing decision the product announced.</summary>
        public RecordingLogger Logger
        {
            get { return Container.Recorder; }
        }

        /// <summary>The fake org itself, for tests that need to reach past the container.</summary>
        public XrmFakedContext Faked
        {
            get { EnsureBuilt(); return faked; }
        }

        /// <summary>Every row of one entity, as the fake org holds it now.</summary>
        public List<Entity> Rows(string logicalName)
        {
            EnsureBuilt();
            return faked.CreateQuery(logicalName).ToList();
        }

        /// <summary>One row, or null. Reads through to the fake org, so it sees writes the test made.</summary>
        public Entity Row(string logicalName, Guid id)
        {
            return Rows(logicalName).FirstOrDefault(e => e.Id == id);
        }

        private void EnsureBuilt()
        {
            if (container != null)
            {
                return;
            }
            var rows = new List<Entity>(seed);
            rows.AddRange(CapabilityRows());
            DeclareAttributes();
            faked.InitializeMetadata(metadata.Values);
            faked.Initialize(rows);
            service = new RecordingOrganizationService(faked);
            container = new TestExecutionContainer(service);
        }

        private void RefuseIfBuilt()
        {
            if (container != null)
            {
                throw new InvalidOperationException(
                    "Seed the fake org before asking for the container - FakeXrmEasy takes its data in one Initialize call.");
            }
        }

        private IEnumerable<Entity> CapabilityRows()
        {
            var rows = new List<Entity>();
            foreach (var pair in supported)
            {
                Guid messageId;
                if (!messageIds.TryGetValue(pair.Value, out messageId))
                {
                    messageId = Guid.NewGuid();
                    messageIds[pair.Value] = messageId;
                    var sdkmessage = new Entity("sdkmessage", messageId);
                    sdkmessage["name"] = pair.Value;
                    rows.Add(sdkmessage);
                }
                var filter = new Entity("sdkmessagefilter", Guid.NewGuid());
                // The logical name, not the numeric type code - see the remarks on this class.
                filter["primaryobjecttypecode"] = pair.Key;
                filter["sdkmessageid"] = new EntityReference("sdkmessage", messageId);
                rows.Add(filter);
            }
            return rows;
        }

        /// <summary>
        /// Gives every declared entity the attribute list its queries will be validated against:
        /// the primary id and name, everything the seeded rows carry, and anything a fixture
        /// declared on top.
        /// </summary>
        private void DeclareAttributes()
        {
            foreach (var pair in metadata)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    pair.Value.PrimaryIdAttribute,
                    pair.Value.PrimaryNameAttribute,
                };
                foreach (var row in seed.Where(e => string.Equals(e.LogicalName, pair.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var key in row.Attributes.Keys)
                    {
                        names.Add(key);
                    }
                }
                HashSet<string> extras;
                if (extraAttributes.TryGetValue(pair.Key, out extras))
                {
                    names.UnionWith(extras);
                }
                var attributes = names
                    .Select(name => (AttributeMetadata)new StringAttributeMetadata { LogicalName = name })
                    .ToArray();
                SetSealed(pair.Value, "Attributes", attributes);
            }
        }

        private static void SetSealed(object target, string property, object value)
        {
            var setter = target.GetType().GetProperty(property).GetSetMethod(true);
            setter.Invoke(target, new[] { value });
        }
    }
}
