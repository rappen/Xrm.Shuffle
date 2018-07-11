﻿using Cinteros.Crm.Utils.Common;
using Cinteros.Crm.Utils.Misc;
using Cinteros.Crm.Utils.Shuffle.Types;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace Cinteros.Crm.Utils.Shuffle
{
    public partial class Shuffler
    {
        #region Private Methods

        private void ExportSolutionBlock(SolutionBlock block)
        {
            log.StartSection("ExportSolutionBlock");
            var name = block.Name;
            log.Log("Block: {0}", name);
            var path = block.Path;
            var file = block.File;
            if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(definitionpath))
            {
                path = definitionpath;
                path += path.EndsWith("\\") ? "" : "\\";
            }
            if (string.IsNullOrWhiteSpace(file))
            {
                file = name;
            }
            if (block.Export != null)
            {
                var type = block.Export.Type;
                var setversion = block.Export.SetVersion;
                var publish = block.Export.PublishBeforeExport;
                var targetversion = block.Export.TargetVersion;

                var cdSolution = GetAndVerifySolutionForExport(name);
                var currentversion = new Version(cdSolution.Property("version", "1.0.0.0"));

                SendLine("Solution: {0} {1}", name, currentversion);

                if (!string.IsNullOrWhiteSpace(setversion))
                {
                    SetNewSolutionVersion(setversion, cdSolution, currentversion);
                }

                if (publish)
                {
                    SendLine("Publishing customizations");
                    crmsvc.Execute(new PublishAllXmlRequest());
                }

                var req = new ExportSolutionRequest()
                {
                    SolutionName = name
                };
#if Crm8
                if (!string.IsNullOrWhiteSpace(targetversion))
                {
                    req.TargetVersion = targetversion;
                }
#endif
                if (block.Export.Settings != null)
                {
                    req.ExportAutoNumberingSettings = block.Export.Settings.AutoNumbering;
                    req.ExportCalendarSettings = block.Export.Settings.Calendar;
                    req.ExportCustomizationSettings = block.Export.Settings.Customization;
                    req.ExportEmailTrackingSettings = block.Export.Settings.EmailTracking;
                    req.ExportGeneralSettings = block.Export.Settings.General;
                    req.ExportMarketingSettings = block.Export.Settings.Marketing;
                    req.ExportOutlookSynchronizationSettings = block.Export.Settings.OutlookSync;
                    req.ExportRelationshipRoles = block.Export.Settings.RelationshipRoles;
                    req.ExportIsvConfig = block.Export.Settings.IsvConfig;
                }

                if (type == SolutionTypes.Managed || type == SolutionTypes.Both)
                {
                    var filename = path + file + "_managed.zip";
                    SendLine("Exporting solution to: {0}", filename);
                    req.Managed = true;
                    var exportSolutionResponse = (ExportSolutionResponse)crmsvc.Execute(req);
                    var exportXml = exportSolutionResponse.ExportSolutionFile;
                    File.WriteAllBytes(filename, exportXml);
                }
                if (type == SolutionTypes.Unmanaged || type == SolutionTypes.Both)
                {
                    var filename = path + file + ".zip";
                    SendLine("Exporting solution to: {0}", filename);
                    req.Managed = false;
                    var exportSolutionResponse = (ExportSolutionResponse)crmsvc.Execute(req);
                    var exportXml = exportSolutionResponse.ExportSolutionFile;
                    File.WriteAllBytes(filename, exportXml);
                }
            }
            log.EndSection();
        }

        private CintDynEntity GetAndVerifySolutionForExport(string name)
        {
            var cSolutions = CintDynEntity.RetrieveMultiple(crmsvc, "solution",
                new string[] { "isvisible", "uniquename" },
                new object[] { true, name },
                new ColumnSet("solutionid", "friendlyname", "version", "ismanaged"), log);
            if (cSolutions.Count == 0)
            {
                throw new ArgumentOutOfRangeException("SolutionUniqueName", name, "Cannot find solution");
            }
            if (cSolutions.Count > 1)
            {
                throw new ArgumentOutOfRangeException("SolutionUniqueName", name, "Found " + cSolutions.Count.ToString() + " matching solutions");
            }
            var cdSolution = cSolutions[0];
            return cdSolution;
        }

        /// <summary>Get the current versions for all solutions defined in the definition file</summary>
        /// <remarks>Results will be placed in the public dictionary <c ref="ExistingSolutionVersions">ExistingSolutionVersions</c></remarks>
        public void GetCurrentVersions()
        {
            log.StartSection("GetCurrentVersions");
            ExistingSolutionVersions = new Dictionary<string, Version>();
            var xRoot = CintXML.FindChild(definition, "ShuffleDefinition");
            var xBlocks = CintXML.FindChild(xRoot, "Blocks");
            if (xBlocks != null)
            {
                var solutions = GetExistingSolutions();
                foreach (XmlNode xBlock in xBlocks.ChildNodes)
                {
                    if (xBlock.NodeType == XmlNodeType.Element)
                    {
                        switch (xBlock.Name)
                        {
                            case "DataBlock":
                                break;

                            case "SolutionBlock":
                                var xmlNode = CintXML.FindChild(xBlock, "Export");
                                if (xmlNode != null)
                                {
                                    var name = CintXML.GetAttribute(xBlock, "Name");
                                    log.Log("Getting version for: {0}", name);
                                    foreach (var solution in solutions)
                                    {
                                        if (name.Equals(solution.Property("uniquename", ""), StringComparison.OrdinalIgnoreCase))
                                        {
                                            ExistingSolutionVersions.Add(name, new Version(solution.Property("version", "1.0.0.0")));
                                            log.Log("Version found: {0}", ExistingSolutionVersions[name]);
                                        }
                                    }
                                }
                                break;
                        }
                    }
                }
            }
            log.EndSection();
        }

        private Version IncrementVersion(Version version)
        {
            var verparts = version;
            var newversion = verparts.Major.ToString() + "." + verparts.Minor.ToString() + "." + DateTime.Today.ToString("yyMM") + "." + (verparts.Revision + 1).ToString();
            log.Log("Increasing {0} to {1}", version, newversion);
            return new Version(newversion);
        }

        private void SetNewSolutionVersion(string setversion, CintDynEntity cdSolution, Version currentversion)
        {
            Version newversion;
            if (setversion.Equals("IncrementAll", StringComparison.OrdinalIgnoreCase))
            {
                newversion = new Version("1.0.0.0");
                foreach (var existingversion in ExistingSolutionVersions.Values)
                {
                    if (existingversion > newversion)
                    {
                        newversion = existingversion;
                    }
                }
                newversion = IncrementVersion(newversion);
            }
            else if (setversion.Equals("Increment", StringComparison.OrdinalIgnoreCase))
            {
                newversion = IncrementVersion(currentversion);
            }
            else if (setversion.Equals("Current", StringComparison.OrdinalIgnoreCase))
            {
                newversion = currentversion;
            }
            else
            {
                newversion = new Version(setversion);
            }
            if (!currentversion.Equals(newversion))
            {
                SendLine("Setting version: {0}", newversion);
                var cdSolUpd = cdSolution.Clone(true);
                cdSolUpd.AddProperty("version", newversion.ToString());
                cdSolUpd.Save();
            }
        }

        #endregion Private Methods
    }
}