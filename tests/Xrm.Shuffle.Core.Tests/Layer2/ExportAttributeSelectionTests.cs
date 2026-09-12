namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer2
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// Which attributes survive an export that asked for a wildcard column set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A block with a wildcard column reads every attribute back from the platform and then
    /// narrows the result in memory, because a QueryExpression cannot express "cint_*". That
    /// narrowing is SelectAttributes, and it is the only place in export where records are
    /// edited after they are retrieved - what it drops never reaches the file.
    /// </para>
    /// <para>
    /// It was rewritten in this PR. The previous version removed keys from the same collection
    /// it was walking, which shifted the remaining entries down and skipped the one after every
    /// removal, so a run of unwanted attributes came out half-filtered. The two-pass version
    /// collects the keys first and removes afterwards. Several cases below only distinguish the
    /// two when unwanted attributes are adjacent, which is why they are written that way.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class ExportAttributeSelectionTests : FakeOrgTestBase
    {
        /// <summary>A retrieved record, carrying its primary id the way a real retrieve answers.</summary>
        private static Entity Row(Guid id, params string[] attributes)
        {
            var entity = new Entity("account", id);
            entity["accountid"] = id;
            foreach (var attribute in attributes)
            {
                entity[attribute] = attribute + " value";
            }
            return entity;
        }

        private static EntityCollection Exported(params Entity[] entities)
        {
            var collection = new EntityCollection { EntityName = "account" };
            collection.Entities.AddRange(entities);
            return collection;
        }

        /// <summary>
        /// Narrows a retrieved collection the way an export block with a wildcard column does.
        /// </summary>
        private void Select(EntityCollection exported, string[] keep, string[] nulls = null)
        {
            Online().WithMetadata("account");
            Shuffler.TestSelectAttributes(
                Org.Container,
                exported,
                keep.ToList(),
                (nulls ?? new string[0]).ToList());
        }

        private static List<string> Attributes(Entity entity)
        {
            return entity.Attributes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        }

        [Test]
        public void An_attribute_outside_the_list_is_removed()
        {
            var record = Row(Id(1), "name", "telephone1");

            Select(Exported(record), new[] { "name" });

            Assert.That(Attributes(record), Is.EqualTo(new[] { "accountid", "name" }), DumpAll());
        }

        /// <summary>
        /// The regression the rewrite fixes: four unwanted attributes in a row.
        /// </summary>
        /// <remarks>
        /// Removing while iterating dropped every other one, so this record used to come out
        /// still carrying two of the four.
        /// </remarks>
        [Test]
        public void A_run_of_unwanted_attributes_is_removed_entirely()
        {
            var record = Row(Id(1), "name", "junk1", "junk2", "junk3", "junk4");

            Select(Exported(record), new[] { "name" });

            Assert.That(Attributes(record), Is.EqualTo(new[] { "accountid", "name" }), DumpAll());
        }

        [Test]
        public void An_unwanted_attribute_between_two_wanted_ones_is_removed()
        {
            var record = Row(Id(1), "name", "junk1", "description", "junk2");

            Select(Exported(record), new[] { "name", "description" });

            Assert.That(
                Attributes(record),
                Is.EqualTo(new[] { "accountid", "description", "name" }),
                DumpAll());
        }

        [Test]
        public void The_primary_id_survives_a_list_that_does_not_name_it()
        {
            var record = Row(Id(1), "name");

            Select(Exported(record), new[] { "name" });

            Assert.That(record.Contains("accountid"), Is.True, DumpAll());
            Assert.That(record["accountid"], Is.EqualTo(Id(1)), DumpAll());
        }

        [Test]
        public void An_empty_list_strips_everything_but_the_primary_id()
        {
            var record = Row(Id(1), "name", "telephone1", "description");

            Select(Exported(record), new string[0]);

            Assert.That(Attributes(record), Is.EqualTo(new[] { "accountid" }), DumpAll());
        }

        [Test]
        public void A_trailing_wildcard_keeps_every_attribute_with_that_prefix()
        {
            var record = Row(Id(1), "cint_one", "cint_two", "name", "telephone1");

            Select(Exported(record), new[] { "cint_*" });

            Assert.That(
                Attributes(record),
                Is.EqualTo(new[] { "accountid", "cint_one", "cint_two" }),
                DumpAll());
        }

        /// <summary>
        /// Definitions write the wildcard as a SQL percent sign, which IsSqlLikeMatch rewrites
        /// to an asterisk - so both spellings have to select the same columns.
        /// </summary>
        [Test]
        public void A_percent_wildcard_selects_the_same_attributes_as_an_asterisk()
        {
            var record = Row(Id(1), "cint_one", "cint_two", "name");

            Select(Exported(record), new[] { "cint_%" });

            Assert.That(
                Attributes(record),
                Is.EqualTo(new[] { "accountid", "cint_one", "cint_two" }),
                DumpAll());
        }

        [Test]
        public void A_null_attribute_is_added_when_the_record_does_not_carry_it()
        {
            var record = Row(Id(1), "name");

            Select(Exported(record), new[] { "name" }, new[] { "description" });

            Assert.That(record.Contains("description"), Is.True, DumpAll());
            Assert.That(record["description"], Is.Null, DumpAll());
        }

        [Test]
        public void A_null_attribute_the_record_already_carries_keeps_its_value()
        {
            var record = Row(Id(1), "description");

            Select(Exported(record), new[] { "description" }, new[] { "description" });

            Assert.That(record["description"], Is.EqualTo("description value"), DumpAll());
        }

        /// <summary>
        /// Null attributes are applied after the filter, not before, so a column the filter
        /// dropped comes back - emptied. Worth pinning: it is the difference between a file
        /// that omits a column and one that blanks it on import.
        /// </summary>
        [Test]
        public void A_null_attribute_the_filter_removed_comes_back_empty()
        {
            var record = Row(Id(1), "name", "description");

            Select(Exported(record), new[] { "name" }, new[] { "description" });

            Assert.That(record.Contains("description"), Is.True, DumpAll());
            Assert.That(record["description"], Is.Null, DumpAll());
        }

        [Test]
        public void Every_record_in_the_collection_is_filtered()
        {
            var first = Row(Id(1), "name", "junk1", "junk2");
            var second = Row(Id(2), "name", "junk1", "junk2");

            Select(Exported(first, second), new[] { "name" });

            Assert.That(Attributes(first), Is.EqualTo(new[] { "accountid", "name" }), DumpAll());
            Assert.That(Attributes(second), Is.EqualTo(new[] { "accountid", "name" }), DumpAll());
        }
    }
}
