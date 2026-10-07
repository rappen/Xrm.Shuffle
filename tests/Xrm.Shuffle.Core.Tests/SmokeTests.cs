using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Schema;
using Cinteros.Crm.Utils.Shuffle.Types;
using NUnit.Framework;

namespace Cinteros.Crm.Utils.Shuffle.Tests
{
    /// <summary>
    /// Proves the shuffle core compiles into a test assembly without WinForms or
    /// XrmToolBox, that the embedded schemas travel with it, and that the batch size
    /// default agrees across the three places that declare it.
    /// </summary>
    [TestFixture]
    public class SmokeTests
    {
        private const string MinimalDefinition =
            "<ShuffleDefinition>" +
              "<Blocks>" +
                "<DataBlock Name=\"Languages\" Entity=\"cint_language\">" +
                  "<Import CreateWithId=\"true\" />" +
                "</DataBlock>" +
              "</Blocks>" +
            "</ShuffleDefinition>";

        private static XmlDocument Load(string xml)
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            return doc;
        }

        [Test]
        public void The_definition_schemas_are_embedded_in_the_test_assembly()
        {
            // ShuffleHelper resolves them off Assembly.GetExecutingAssembly(), which is
            // this assembly once the shared project is compiled in. If the .projitems
            // ever stops carrying the EmbeddedResource items, validation silently
            // becomes a no-op rather than failing - hence the explicit check.
            var names = Assembly.GetExecutingAssembly().GetManifestResourceNames()
                .Select(n => n.ToLowerInvariant()).ToList();

            Assert.That(names.Any(n => n.EndsWith("shuffledefinition.xsd")), Is.True,
                "ShuffleDefinition.xsd is not embedded: " + string.Join(", ", names));
            Assert.That(names.Any(n => n.EndsWith("queryexpression.xsd")), Is.True,
                "QueryExpression.xsd is not embedded: " + string.Join(", ", names));
        }

        [Test]
        public void A_minimal_definition_validates_against_the_schema()
        {
            Assert.That(() => ShuffleHelper.ValidateDefinitionXml(Load(MinimalDefinition)),
                Throws.Nothing);
        }

        [Test]
        public void An_undeclared_attribute_fails_validation()
        {
            // Guards against the schemas failing to load: ValidateDefinitionXml returns
            // quietly when fewer than two are registered, so a passing "valid" test on
            // its own proves nothing.
            var invalid = MinimalDefinition.Replace("<Import ", "<Import NoSuchAttribute=\"1\" ");

            Assert.That(() => ShuffleHelper.ValidateDefinitionXml(Load(invalid)),
                Throws.InstanceOf<XmlSchemaValidationException>());
        }

        [Test]
        public void Default_batch_size_is_one()
        {
            Assert.That(new DataBlockImport().BatchSize, Is.EqualTo(1));
        }

        [Test]
        public void A_definition_that_does_not_mention_batch_size_gets_one()
        {
            var shuffler = new Shuffler(null) { Definition = Load(MinimalDefinition) };

            Assert.That(shuffler.ShuffleDefinition.Blocks.Items[0], Is.InstanceOf<DataBlock>());
            Assert.That(((DataBlock)shuffler.ShuffleDefinition.Blocks.Items[0]).Import.BatchSize,
                Is.EqualTo(1));
        }

        [Test]
        public void A_definition_that_opts_in_keeps_its_batch_size()
        {
            var opted = MinimalDefinition.Replace("<Import ", "<Import BatchSize=\"100\" ");
            var shuffler = new Shuffler(null) { Definition = Load(opted) };

            Assert.That(((DataBlock)shuffler.ShuffleDefinition.Blocks.Items[0]).Import.BatchSize,
                Is.EqualTo(100));
        }
    }
}
