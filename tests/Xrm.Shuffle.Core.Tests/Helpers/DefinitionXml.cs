using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Cinteros.Crm.Utils.Shuffle.Types;

namespace Cinteros.Crm.Utils.Shuffle.Tests.Helpers
{
    /// <summary>
    /// Builds <c>&lt;ShuffleDefinition&gt;</c> documents, and the deserialized
    /// <c>ShuffleDefinition</c> objects the import code actually reads.
    /// </summary>
    /// <remarks>
    /// <c>ShuffleDefinition.xsd</c> declares no <c>targetNamespace</c>, so the literals here carry
    /// no namespace declaration and none should be added — the generated serializer would then
    /// fail to match the root element.
    /// </remarks>
    public class DefinitionXml
    {
        private readonly List<DataBlockBuilder> blocks = new List<DataBlockBuilder>();
        private bool? stopOnError;
        private int? timeout;

        /// <summary>Starts a definition with one data block already open.</summary>
        public static DataBlockBuilder DataBlock(string name, string entityLogicalName)
        {
            return new DefinitionXml().WithDataBlock(name, entityLogicalName);
        }

        /// <summary>Opens another data block in this definition.</summary>
        public DataBlockBuilder WithDataBlock(string name, string entityLogicalName)
        {
            var block = new DataBlockBuilder(this, name, entityLogicalName);
            blocks.Add(block);
            return block;
        }

        /// <summary>Sets the definition-level StopOnError attribute.</summary>
        public DefinitionXml StopOnError(bool value)
        {
            stopOnError = value;
            return this;
        }

        /// <summary>Sets the definition-level Timeout attribute.</summary>
        public DefinitionXml Timeout(int seconds)
        {
            timeout = seconds;
            return this;
        }

        /// <summary>Renders the definition as XML.</summary>
        public XmlDocument Build()
        {
            var xml = new StringBuilder();
            xml.Append("<ShuffleDefinition");
            if (stopOnError.HasValue)
            {
                xml.Append(" StopOnError=\"").Append(stopOnError.Value ? "true" : "false").Append("\"");
            }
            if (timeout.HasValue)
            {
                xml.Append(" Timeout=\"").Append(timeout.Value.ToString(CultureInfo.InvariantCulture)).Append("\"");
            }
            xml.Append("><Blocks>");
            foreach (var block in blocks)
            {
                block.AppendTo(xml);
            }
            xml.Append("</Blocks></ShuffleDefinition>");
            var document = new XmlDocument();
            document.LoadXml(xml.ToString());
            return document;
        }

        /// <summary>Renders the definition and deserializes it the way Shuffler does.</summary>
        public ShuffleDefinition Deserialize()
        {
            var serializer = new XmlSerializer(typeof(ShuffleDefinition));
            using (var reader = new StringReader(Build().OuterXml))
            {
                return (ShuffleDefinition)serializer.Deserialize(reader);
            }
        }

        /// <summary>Renders the definition and returns its single data block.</summary>
        public global::Cinteros.Crm.Utils.Shuffle.Types.DataBlock DeserializeBlock()
        {
            return (global::Cinteros.Crm.Utils.Shuffle.Types.DataBlock)Deserialize().Blocks.Items[0];
        }

        /// <summary>One <c>&lt;DataBlock&gt;</c> and its <c>&lt;Import&gt;</c> settings.</summary>
        public class DataBlockBuilder
        {
            private readonly DefinitionXml owner;
            private readonly string name;
            private readonly string entityLogicalName;
            private readonly List<string> importAttributes = new List<string>();
            private readonly List<string> matchAttributes = new List<string>();
            private bool import;
            private bool match;
            private bool preRetrieveAll;

            internal DataBlockBuilder(DefinitionXml owner, string name, string entityLogicalName)
            {
                this.owner = owner;
                this.name = name;
                this.entityLogicalName = entityLogicalName;
            }

            /// <summary>Adds an <c>&lt;Import&gt;</c> element with no attributes set.</summary>
            public DataBlockBuilder Import()
            {
                import = true;
                return this;
            }

            /// <summary>Sets any Import attribute by name, e.g. <c>Save</c> or <c>Delete</c>.</summary>
            public DataBlockBuilder ImportAttribute(string attribute, string value)
            {
                import = true;
                importAttributes.Add(string.Format(
                    CultureInfo.InvariantCulture, " {0}=\"{1}\"", attribute, value));
                return this;
            }

            /// <summary>Sets BatchSize. Omitting it is what leaves batching off.</summary>
            public DataBlockBuilder BatchSize(int size)
            {
                return ImportAttribute("BatchSize", size.ToString(CultureInfo.InvariantCulture));
            }

            /// <summary>Sets DeferStateAndOwner.</summary>
            public DataBlockBuilder DeferStateAndOwner(bool value = true)
            {
                return ImportAttribute("DeferStateAndOwner", value ? "true" : "false");
            }

            /// <summary>Sets CreateWithId.</summary>
            public DataBlockBuilder CreateWithId(bool value = true)
            {
                return ImportAttribute("CreateWithId", value ? "true" : "false");
            }

            /// <summary>
            /// Adds a match attribute. <paramref name="retrieveAll"/> drives PreRetrieveAll, which is
            /// the switch that lets a block reach any batched path at all.
            /// </summary>
            public DataBlockBuilder MatchOn(string attribute, bool retrieveAll = true)
            {
                import = true;
                match = true;
                preRetrieveAll = preRetrieveAll || retrieveAll;
                matchAttributes.Add(string.Format(
                    CultureInfo.InvariantCulture, "<Attribute Name=\"{0}\" />", attribute));
                return this;
            }

            /// <summary>Opens a sibling data block.</summary>
            public DataBlockBuilder AndDataBlock(string blockName, string blockEntity)
            {
                return owner.WithDataBlock(blockName, blockEntity);
            }

            /// <summary>Renders the whole definition as XML.</summary>
            public XmlDocument Build()
            {
                return owner.Build();
            }

            /// <summary>Renders and deserializes the whole definition.</summary>
            public ShuffleDefinition Deserialize()
            {
                return owner.Deserialize();
            }

            /// <summary>Renders and returns the first data block.</summary>
            public global::Cinteros.Crm.Utils.Shuffle.Types.DataBlock DeserializeBlock()
            {
                return owner.DeserializeBlock();
            }

            internal void AppendTo(StringBuilder xml)
            {
                xml.Append("<DataBlock Name=\"").Append(name)
                   .Append("\" Entity=\"").Append(entityLogicalName).Append("\">");
                if (import)
                {
                    xml.Append("<Import");
                    foreach (var attribute in importAttributes)
                    {
                        xml.Append(attribute);
                    }
                    if (match)
                    {
                        xml.Append(">");
                        xml.Append("<Match PreRetrieveAll=\"")
                           .Append(preRetrieveAll ? "true" : "false").Append("\">");
                        foreach (var attribute in matchAttributes)
                        {
                            xml.Append(attribute);
                        }
                        xml.Append("</Match></Import>");
                    }
                    else
                    {
                        xml.Append(" />");
                    }
                }
                xml.Append("</DataBlock>");
            }
        }
    }
}
