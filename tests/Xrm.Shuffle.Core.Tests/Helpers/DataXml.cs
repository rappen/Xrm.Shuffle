using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;

namespace Cinteros.Crm.Utils.Shuffle.Tests.Helpers
{
    /// <summary>
    /// Builds the <c>&lt;ShuffleData&gt;</c> documents that <c>Shuffler.Deserialize</c> reads.
    /// </summary>
    /// <remarks>
    /// Only the <c>Simple</c> serialization type is produced. That matters: deserializing a Simple
    /// attribute is a pure <c>switch</c> over the type string
    /// (<c>Xrm.Utils.Core.Common/Extensions/EntityExtensions.cs</c>, <c>SetAttribute</c>), so test
    /// data needs no organization service and no metadata call. <c>Explicit</c> and
    /// <c>SimpleWithValue</c> would both reach for metadata, which is why fixtures stay on Simple.
    /// </remarks>
    public class DataXml
    {
        private readonly List<BlockBuilder> blocks = new List<BlockBuilder>();

        /// <summary>Starts a document with one block already open.</summary>
        public static BlockBuilder Block(string name)
        {
            return new DataXml().WithBlock(name);
        }

        /// <summary>Opens another block in this document.</summary>
        public BlockBuilder WithBlock(string name)
        {
            var block = new BlockBuilder(this, name);
            blocks.Add(block);
            return block;
        }

        /// <summary>Renders the document.</summary>
        public XmlDocument Build()
        {
            var xml = new StringBuilder();
            xml.Append("<ShuffleData Type=\"Simple\">");
            foreach (var block in blocks)
            {
                block.AppendTo(xml);
            }
            xml.Append("</ShuffleData>");
            var document = new XmlDocument();
            document.LoadXml(xml.ToString());
            return document;
        }

        /// <summary>One <c>&lt;Block&gt;</c> and the records in it.</summary>
        public class BlockBuilder
        {
            private readonly DataXml owner;
            private readonly string name;
            private readonly List<RecordBuilder> records = new List<RecordBuilder>();

            internal BlockBuilder(DataXml owner, string name)
            {
                this.owner = owner;
                this.name = name;
            }

            /// <summary>Adds a record. A null id emits no <c>id</c> attribute at all.</summary>
            public RecordBuilder Record(string entityLogicalName, Guid? id = null)
            {
                var record = new RecordBuilder(this, entityLogicalName, id);
                records.Add(record);
                return record;
            }

            /// <summary>Opens a sibling block in the same document.</summary>
            public BlockBuilder AndBlock(string blockName)
            {
                return owner.WithBlock(blockName);
            }

            /// <summary>Renders the whole document, not just this block.</summary>
            public XmlDocument Build()
            {
                return owner.Build();
            }

            internal void AppendTo(StringBuilder xml)
            {
                xml.Append("<Block Name=\"").Append(Escape(name)).Append("\"><Entities>");
                foreach (var record in records)
                {
                    record.AppendTo(xml);
                }
                xml.Append("</Entities></Block>");
            }
        }

        /// <summary>One <c>&lt;Entity&gt;</c> and its attributes.</summary>
        public class RecordBuilder
        {
            private readonly BlockBuilder owner;
            private readonly string entityLogicalName;
            private readonly Guid? id;
            private readonly List<string> attributes = new List<string>();

            internal RecordBuilder(BlockBuilder owner, string entityLogicalName, Guid? id)
            {
                this.owner = owner;
                this.entityLogicalName = entityLogicalName;
                this.id = id;
            }

            /// <summary>Adds a string attribute.</summary>
            public RecordBuilder With(string attribute, string value)
            {
                return With(attribute, "String", value);
            }

            /// <summary>Adds an attribute of an explicit type, as the serializer would write it.</summary>
            public RecordBuilder With(string attribute, string type, string value)
            {
                attributes.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "<Attribute name=\"{0}\" type=\"{1}\">{2}</Attribute>",
                    Escape(attribute), Escape(type), Escape(value)));
                return this;
            }

            /// <summary>Adds an integer attribute.</summary>
            public RecordBuilder WithInt(string attribute, int value)
            {
                return With(attribute, "Int32", value.ToString(CultureInfo.InvariantCulture));
            }

            /// <summary>Adds an optionset attribute.</summary>
            public RecordBuilder WithOptionSet(string attribute, int value)
            {
                return With(attribute, "OptionSetValue", value.ToString(CultureInfo.InvariantCulture));
            }

            /// <summary>Adds a lookup. The serializer writes the target in an <c>entity</c> attribute.</summary>
            public RecordBuilder WithReference(string attribute, string targetLogicalName, Guid targetId)
            {
                attributes.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "<Attribute name=\"{0}\" type=\"EntityReference\" entity=\"{1}\">{2}</Attribute>",
                    Escape(attribute), Escape(targetLogicalName), targetId));
                return this;
            }

            /// <summary>Adds another record to the same block.</summary>
            public RecordBuilder AndRecord(string logicalName, Guid? recordId = null)
            {
                return owner.Record(logicalName, recordId);
            }

            /// <summary>Opens a sibling block in the same document.</summary>
            public BlockBuilder AndBlock(string blockName)
            {
                return owner.AndBlock(blockName);
            }

            /// <summary>Renders the whole document.</summary>
            public XmlDocument Build()
            {
                return owner.Build();
            }

            internal void AppendTo(StringBuilder xml)
            {
                xml.Append("<Entity name=\"").Append(Escape(entityLogicalName)).Append("\"");
                if (id.HasValue)
                {
                    xml.Append(" id=\"").Append(id.Value).Append("\"");
                }
                xml.Append(">");
                foreach (var attribute in attributes)
                {
                    xml.Append(attribute);
                }
                xml.Append("</Entity>");
            }
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }
}
