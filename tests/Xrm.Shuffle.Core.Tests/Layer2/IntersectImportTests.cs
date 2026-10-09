namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer2
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Xml.Serialization;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using FakeXrmEasy;
    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.Messages;
    using NUnit.Framework;

    /// <summary>
    /// An intersect block associates the two records each row names. The table on each side is
    /// worked out from the intersect column - and for a relationship from a table to itself those
    /// columns are &lt;table&gt;idone and &lt;table&gt;idtwo, not &lt;table&gt;id.
    /// </summary>
    [TestFixture]
    public class IntersectImportTests : FakeOrgTestBase
    {
        private static Types.DataBlock IntersectBlock(string intersect, string column1, string column2)
        {
            var xml =
                "<ShuffleDefinition><Blocks>" +
                "<DataBlock Name=\"Links\" Entity=\"" + intersect + "\" Type=\"Intersect\">" +
                "<Export><Attributes><Attribute Name=\"" + column1 + "\" /><Attribute Name=\"" + column2 + "\" /></Attributes></Export>" +
                "<Import />" +
                "</DataBlock></Blocks></ShuffleDefinition>";
            using (var reader = new StringReader(xml))
            {
                var definition = (Types.ShuffleDefinition)new XmlSerializer(typeof(Types.ShuffleDefinition)).Deserialize(reader);
                return (Types.DataBlock)definition.Blocks.Items[0];
            }
        }

        /// <summary>One intersect row as the data file holds it: the two ids as bare Guids.</summary>
        private static EntityCollection Rows(string intersect, string column1, string column2, params Tuple<Guid, Guid>[] pairs)
        {
            var rows = new EntityCollection { EntityName = intersect };
            foreach (var pair in pairs)
            {
                var row = new Entity(intersect);
                row[column1] = pair.Item1;
                row[column2] = pair.Item2;
                rows.Entities.Add(row);
            }
            return rows;
        }

        private AssociateRequest[] Associations() =>
            Service.Requests.OfType<AssociateRequest>().ToArray();

        [Test]
        public void A_relationship_from_a_table_to_itself_associates_the_two_records()
        {
            Online().WithEntity(Record("new_category", Id(1), "Gold"), Record("new_category", Id(2), "Silver"), Record("new_category", Id(3), "Bronze"));
            Org.Faked.AddRelationship("new_category_incompatible", new XrmFakedRelationship(
                "new_category_incompatible", "new_categoryidone", "new_categoryidtwo", "new_category", "new_category"));

            var outcome = NewShuffler().TestImportDataBlock(
                IntersectBlock("new_category_incompatible", "new_categoryidone", "new_categoryidtwo"),
                Rows("new_category_incompatible", "new_categoryidone", "new_categoryidtwo",
                    Tuple.Create(Id(1), Id(2)), Tuple.Create(Id(1), Id(3))));

            Assert.That(outcome.Failed, Is.EqualTo(0), DumpAll());
            Assert.That(outcome.Created, Is.EqualTo(2), DumpAll());
            var associations = Associations();
            Assert.That(associations.Select(a => a.Target.LogicalName), Is.All.EqualTo("new_category"), DumpAll());
            Assert.That(associations.SelectMany(a => a.RelatedEntities).Select(r => r.LogicalName), Is.All.EqualTo("new_category"), DumpAll());
            Assert.That(associations.Select(a => a.Relationship.PrimaryEntityRole), Is.All.EqualTo(Microsoft.Xrm.Sdk.EntityRole.Referencing),
                "Dataverse rejects an associate on a reflexive relationship that does not say which side the target is on");
            Assert.That(associations.Select(a => a.Target.Id), Is.EqualTo(new[] { Id(1), Id(1) }));
            Assert.That(associations.Select(a => a.RelatedEntities.Single().Id), Is.EqualTo(new[] { Id(2), Id(3) }));
        }

        /// <summary>The usual case, two different tables with &lt;table&gt;id columns, is unchanged.</summary>
        [Test]
        public void A_relationship_between_two_tables_associates_them_without_a_role()
        {
            Online().WithEntity(Record("account", Id(1), "Contoso"), Record("contact", Id(2), "Pat"));
            Org.Faked.AddRelationship("new_account_contact", new XrmFakedRelationship(
                "new_account_contact", "accountid", "contactid", "account", "contact"));

            var outcome = NewShuffler().TestImportDataBlock(
                IntersectBlock("new_account_contact", "accountid", "contactid"),
                Rows("new_account_contact", "accountid", "contactid", Tuple.Create(Id(1), Id(2))));

            Assert.That(outcome.Failed, Is.EqualTo(0), DumpAll());
            Assert.That(outcome.Created, Is.EqualTo(1), DumpAll());
            var association = Associations().Single();
            Assert.That(association.Target.LogicalName, Is.EqualTo("account"));
            Assert.That(association.RelatedEntities.Single().LogicalName, Is.EqualTo("contact"));
            Assert.That(association.Relationship.PrimaryEntityRole, Is.Null);
        }
    }
}
