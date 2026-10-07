namespace Cinteros.Crm.Utils.Shuffle.Tests.Layer2
{
    using System;
    using System.IO;
    using System.IO.Compression;
    using System.Text;
    using Cinteros.Crm.Utils.Shuffle.Tests.Helpers;
    using Cinteros.Crm.Utils.Shuffle.Types;
    using Microsoft.Xrm.Sdk;
    using NUnit.Framework;

    /// <summary>
    /// The decisions a solution block makes before it imports anything: which version the zip
    /// holds, whether the prerequisites are installed, and whether the import is a create, an
    /// update or a skip.
    /// </summary>
    /// <remarks>
    /// The import itself is an ImportSolutionRequest the fake org cannot carry out, so these stop
    /// short of it. The target's installed solutions are seeded as solution rows, which is what
    /// the product queries.
    /// </remarks>
    [TestFixture]
    public class SolutionImportTests : FakeOrgTestBase
    {
        private string workFolder;

        [SetUp]
        public void CreateWorkFolder()
        {
            workFolder = Path.Combine(Path.GetTempPath(), "ShuffleTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workFolder);
        }

        [TearDown]
        public void DeleteWorkFolder()
        {
            if (Directory.Exists(workFolder))
            {
                Directory.Delete(workFolder, true);
            }
        }

        private static Entity Solution(string uniqueName, string version)
        {
            var solution = new Entity("solution", Guid.NewGuid());
            solution["solutionid"] = solution.Id;
            solution["uniquename"] = uniqueName;
            solution["friendlyname"] = uniqueName;
            solution["version"] = version;
            solution["ismanaged"] = false;
            solution["isvisible"] = true;
            return solution;
        }

        private void Installed(params Entity[] solutions)
        {
            Online().WithMetadata("solution", "solutionid", "friendlyname").WithEntity(solutions);
        }

        private string Zip(string solutionXml)
        {
            var file = Path.Combine(workFolder, "solution_" + Guid.NewGuid().ToString("N") + ".zip");
            using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                if (solutionXml != null)
                {
                    var entry = zip.CreateEntry("solution.xml");
                    using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
                    {
                        writer.Write(solutionXml);
                    }
                }
                var other = zip.CreateEntry("customizations.xml");
                using (var writer = new StreamWriter(other.Open(), Encoding.UTF8))
                {
                    writer.Write("<ImportExportXml />");
                }
            }
            return file;
        }

        private static string Manifest(string version) =>
            "<ImportExportXml><SolutionManifest><UniqueName>Contoso</UniqueName><Version>" + version + "</Version></SolutionManifest></ImportExportXml>";

        private static SolutionBlockImport Import(bool overwriteSame = true, bool overwriteNewer = false, params SolutionBlockImportSolution[] prerequisites)
        {
            return new SolutionBlockImport
            {
                OverwriteSameVersion = overwriteSame,
                OverwriteNewerVersion = overwriteNewer,
                PreRequisites = prerequisites.Length > 0 ? prerequisites : null
            };
        }

        private static SolutionBlockImportSolution Requires(string name, SolutionVersionComparers comparer, string version = null) =>
            new SolutionBlockImportSolution { Name = name, Comparer = comparer, Version = version };

        #region Version in the zip

        [Test]
        public void The_version_is_read_from_the_solution_manifest()
        {
            Installed();

            var version = NewShuffler().TestExtractVersionFromSolutionZip(Zip(Manifest("1.2.3.4")), workFolder);

            Assert.That(version, Is.EqualTo(new Version(1, 2, 3, 4)));
            Assert.That(File.Exists(Path.Combine(workFolder, "solution.xml")), Is.False, "the unpacked solution.xml is cleaned up");
        }

        /// <summary>Regression for #24: this was a bare NullReferenceException.</summary>
        [Test]
        public void A_zip_without_solution_xml_throws_FileNotFoundException()
        {
            Installed();

            var ex = Assert.Throws<FileNotFoundException>(() =>
                NewShuffler().TestExtractVersionFromSolutionZip(Zip(null), workFolder));
            Assert.That(ex.Message, Does.Contain("invalid solution file"));
        }

        [Test]
        public void A_manifest_without_a_version_throws_naming_the_missing_element()
        {
            Installed();
            var noVersion = "<ImportExportXml><SolutionManifest><UniqueName>Contoso</UniqueName></SolutionManifest></ImportExportXml>";

            var ex = Assert.Throws<System.Xml.XmlException>(() =>
                NewShuffler().TestExtractVersionFromSolutionZip(Zip(noVersion), workFolder));
            Assert.That(ex.Message, Does.Contain("Version"));
        }

        #endregion Version in the zip

        #region Create, update or skip

        [Test]
        public void A_solution_not_in_the_target_is_a_create()
        {
            Installed(Solution("Other", "1.0.0.0"));

            var result = NewShuffler().TestCheckIfImportRequired(Import(), "Contoso", new Version(1, 0, 0, 0));

            Assert.That(result, Is.EqualTo(SolutionImportConditions.Create));
        }

        [Test]
        public void An_older_version_in_the_target_is_an_update()
        {
            Installed(Solution("Contoso", "1.0.0.0"));

            var result = NewShuffler().TestCheckIfImportRequired(Import(), "Contoso", new Version(1, 1, 0, 0));

            Assert.That(result, Is.EqualTo(SolutionImportConditions.Update));
        }

        [TestCase(true, SolutionImportConditions.Update)]
        [TestCase(false, SolutionImportConditions.Skip)]
        public void The_same_version_follows_OverwriteSameVersion(bool overwriteSame, object expected)
        {
            Installed(Solution("Contoso", "1.0.0.0"));

            var result = NewShuffler().TestCheckIfImportRequired(Import(overwriteSame: overwriteSame), "Contoso", new Version(1, 0, 0, 0));

            Assert.That(result, Is.EqualTo((SolutionImportConditions)expected));
        }

        [TestCase(true, SolutionImportConditions.Update)]
        [TestCase(false, SolutionImportConditions.Skip)]
        public void A_newer_version_in_the_target_follows_OverwriteNewerVersion(bool overwriteNewer, object expected)
        {
            Installed(Solution("Contoso", "2.0.0.0"));

            var result = NewShuffler().TestCheckIfImportRequired(Import(overwriteNewer: overwriteNewer), "Contoso", new Version(1, 0, 0, 0));

            Assert.That(result, Is.EqualTo((SolutionImportConditions)expected));
        }

        #endregion Create, update or skip

        #region Prerequisites

        [TestCase(SolutionVersionComparers.any, null)]
        [TestCase(SolutionVersionComparers.eq, "1.5.0.0")]
        [TestCase(SolutionVersionComparers.ge, "1.4.0.0")]
        [TestCase(SolutionVersionComparers.ge, "1.5.0.0")]
        [TestCase(SolutionVersionComparers.ge, "1.*")]
        public void A_satisfied_prerequisite_passes(SolutionVersionComparers comparer, string version)
        {
            Installed(Solution("Base", "1.5.0.0"));

            Assert.DoesNotThrow(() =>
                NewShuffler().TestValidatePreReqs(Import(prerequisites: Requires("Base", comparer, version)), new Version(9, 0, 0, 0)));
            Assert.That(Org.Logger.Logged("is satisfied"), DumpAll());
        }

        [TestCase(SolutionVersionComparers.eq, "1.4.0.0")]
        [TestCase(SolutionVersionComparers.ge, "1.6.0.0")]
        public void An_unsatisfied_prerequisite_throws(SolutionVersionComparers comparer, string version)
        {
            Installed(Solution("Base", "1.5.0.0"));

            var ex = Assert.Throws<Exception>(() =>
                NewShuffler().TestValidatePreReqs(Import(prerequisites: Requires("Base", comparer, version)), new Version(9, 0, 0, 0)));
            Assert.That(ex.Message, Does.Contain("Prerequisite NOT satisfied (Base"));
        }

        [Test]
        public void A_missing_prerequisite_throws()
        {
            Installed(Solution("Other", "1.0.0.0"));

            Assert.Throws<Exception>(() =>
                NewShuffler().TestValidatePreReqs(Import(prerequisites: Requires("Base", SolutionVersionComparers.any)), new Version(1, 0, 0, 0)));
        }

        /// <summary>eq-this and ge-this compare with the version of the solution being imported.</summary>
        [TestCase(SolutionVersionComparers.eqthis, "2.0.0.0", true)]
        [TestCase(SolutionVersionComparers.eqthis, "2.1.0.0", false)]
        [TestCase(SolutionVersionComparers.gethis, "2.1.0.0", true)]
        [TestCase(SolutionVersionComparers.gethis, "1.9.0.0", false)]
        public void This_comparers_use_the_imported_version(SolutionVersionComparers comparer, string installed, bool satisfied)
        {
            Installed(Solution("Base", installed));
            TestDelegate validate = () =>
                NewShuffler().TestValidatePreReqs(Import(prerequisites: Requires("Base", comparer)), new Version(2, 0, 0, 0));

            if (satisfied)
            {
                Assert.DoesNotThrow(validate);
            }
            else
            {
                Assert.Throws<Exception>(validate);
            }
        }

        /// <summary>
        /// The resolved comparer and version are what get logged; before the log line moved,
        /// every prerequisite was logged as "ge 0.0".
        /// </summary>
        [Test]
        public void The_resolved_prerequisite_is_logged()
        {
            Installed(Solution("Base", "2.0.0.0"));

            NewShuffler().TestValidatePreReqs(Import(prerequisites: Requires("Base", SolutionVersionComparers.gethis)), new Version(2, 0, 0, 0));

            Assert.That(Org.Logger.Logged("Prereq: Base ge 2.0.0.0"), DumpAll());
        }

        [Test]
        public void No_prerequisites_is_not_an_error()
        {
            Installed();

            Assert.DoesNotThrow(() => NewShuffler().TestValidatePreReqs(Import(), new Version(1, 0, 0, 0)));
        }

        #endregion Prerequisites
    }
}
