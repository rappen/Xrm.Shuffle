using System;
using System.Linq;
using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using NUnit.Framework;

namespace Cinteros.Crm.Utils.Shuffle.Tests
{
    /// <summary>
    /// Exercises the test doubles themselves.
    /// </summary>
    /// <remarks>
    /// The doubles encode assumptions about the product — the shape of the capability probe, that
    /// ExecuteMultipleResponseItem is constructible, that a partial class reaches the private
    /// members. If one of those stops holding, these tests are where it shows, rather than as a
    /// confusing failure in a fixture that was testing something else entirely.
    /// </remarks>
    [TestFixture]
    public class TestDoubleTests : ShuffleTestBase
    {
        [Test]
        public void Logger_records_messages_and_section_depth()
        {
            Container.Logger.StartSection("Outer");
            Container.Logger.Log("Block: {0}", "Accounts");
            Container.Logger.EndSection();

            Assert.That(Recorder.Logger.Logged("Block: Accounts"), Is.True, Recorder.Logger.Dump());
            Assert.That(Recorder.Logger.SectionDepth, Is.EqualTo(0));
        }

        [Test]
        public void Scripted_service_records_every_request_in_order()
        {
            Service.OnCreate(entity => Id(7));
            Service.Create(new Entity("account"));
            Service.Update(new Entity("account", Id(7)));
            Service.Delete("account", Id(7));

            Assert.That(Service.RequestNames, Is.EqualTo(new[] { "Create", "Update", "Delete" }));
            Assert.That(Service.Created.Single().LogicalName, Is.EqualTo("account"));
            Assert.That(Service.Deleted.Single().Item2, Is.EqualTo(Id(7)));
        }

        [Test]
        public void Scripted_service_answers_the_capability_probe_only_for_declared_messages()
        {
            Service.SupportsBulkMessage("account", "CreateMultiple");
            var shuffler = NewShuffler();

            Assert.That(shuffler.TestIsCreateMultipleSupported("account"), Is.True);
            Assert.That(shuffler.TestIsUpdateMultipleSupported("account"), Is.False);
            Assert.That(shuffler.TestIsCreateMultipleSupported("contact"), Is.False);
        }

        [Test]
        public void Unscripted_message_throws_naming_what_was_seen()
        {
            var thrown = Assert.Throws<InvalidOperationException>(
                () => Service.Execute(new OrganizationRequest("CreateMultiple")));

            Assert.That(thrown.Message, Does.Contain("CreateMultiple"));
        }

        [Test]
        public void Sequenced_responses_are_handed_out_once_each()
        {
            Service.OnMessageSequence(
                "WhoAmI",
                new OrganizationResponse { ResponseName = "first" },
                new OrganizationResponse { ResponseName = "second" });

            Assert.That(Service.Execute(new OrganizationRequest("WhoAmI")).ResponseName, Is.EqualTo("first"));
            Assert.That(Service.Execute(new OrganizationRequest("WhoAmI")).ResponseName, Is.EqualTo("second"));
            Assert.Throws<InvalidOperationException>(() => Service.Execute(new OrganizationRequest("WhoAmI")));
        }

        [Test]
        public void Omit_produces_a_response_collection_shorter_than_the_request_list()
        {
            var response = new ExecuteMultipleResponseBuilder()
                .CreatedAt(Id(1))
                .Omit()
                .CreatedAt(Id(3))
                .Build();

            var items = (ExecuteMultipleResponseItemCollection)response.Results["Responses"];

            Assert.That(items.Count, Is.EqualTo(2), "Omit must skip the item, not blank it out");
            Assert.That(items.Select(i => i.RequestIndex), Is.EqualTo(new[] { 0, 2 }));
            Assert.That((bool)response.Results["IsFaulted"], Is.False);
        }

        [Test]
        public void A_fault_marks_the_whole_response_faulted()
        {
            var response = new ExecuteMultipleResponseBuilder()
                .Succeeded()
                .Fault("record is busy")
                .Build();

            Assert.That((bool)response.Results["IsFaulted"], Is.True);
        }

        [Test]
        public void Shim_reaches_the_private_state_the_import_sets_up_in_ImportToCRM()
        {
            var shuffler = NewShuffler(stopOnError: true);

            Assert.That(shuffler.TestGuidMap, Is.Not.Null, "guidmap is only assigned inside ImportToCRM");
            Assert.That(shuffler.TestStopOnError, Is.True);
            Assert.That(shuffler.TestStopOnBatchError(3, "account Acme"), Is.True);
            Assert.That(shuffler.TestBatchFailureLabel, Is.EqualTo("003 account Acme"));
        }

        [Test]
        public void Definition_literals_deserialize_into_the_objects_the_import_reads()
        {
            var block = DefinitionXml
                .DataBlock("Accounts", "account")
                .BatchSize(50)
                .MatchOn("name")
                .DeserializeBlock();

            Assert.That(block.Name, Is.EqualTo("Accounts"));
            Assert.That(block.Import.BatchSize, Is.EqualTo(50));
            Assert.That(block.Import.Match.PreRetrieveAll, Is.True);
            Assert.That(block.Import.Match.Attribute.Single().Name, Is.EqualTo("name"));
        }

        [Test]
        public void An_omitted_BatchSize_deserializes_to_the_no_batching_default()
        {
            var block = DefinitionXml.DataBlock("Accounts", "account").Import().DeserializeBlock();

            Assert.That(block.Import.BatchSize, Is.EqualTo(1));
        }

        [Test]
        public void Simple_data_literals_deserialize_without_touching_the_service()
        {
            var data = DataXml
                .Block("Accounts")
                .Record("account", Id(1)).With("name", "Acme").WithInt("numberofemployees", 42)
                .AndRecord("account", Id(2)).With("name", "Globex")
                .Build();

            var blocks = NewShuffler().Deserialize(Container, data);
            var entities = blocks["Accounts"];

            Assert.That(entities.Entities.Count, Is.EqualTo(2));
            Assert.That(entities.Entities[0].Id, Is.EqualTo(Id(1)));
            Assert.That(entities.Entities[0]["name"], Is.EqualTo("Acme"));
            Assert.That(entities.Entities[0]["numberofemployees"], Is.EqualTo(42));
            Assert.That(Service.Requests, Is.Empty, "Simple serialization must need no metadata");
        }
    }
}
