﻿using Cinteros.Crm.Utils.Shuffle;
using Xrm.Utils.Core.Common.Extensions;
using Xrm.Utils.Core.Common.Interfaces;
using Xrm.Utils.Core.Common.Loggers;
using Xrm.Utils.Core.Common.Misc;
using Ionic.Zip;
using McTools.Xrm.Connection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;
using System.Reflection;

namespace Rappen.XTB.ShuffleDeployer
{
    public partial class ShuffleDeployer : PluginControlBase, IGitHubPlugin, IWorkerHost
    {
        #region Private parts

        private const string aiEndpoint = "https://dc.services.visualstudio.com/v2/track";
        private const string aiKey = "eed73022-2444-45fd-928b-5eebd8fa46a6";    // jonas@rappen.net tenant, XrmToolBox
        private AppInsights ai = new AppInsights(aiEndpoint, aiKey, Assembly.GetExecutingAssembly(), "Shuffle Deployer");
        private const string AppName = "Shuffle Deployer";
        private readonly string TempFolder = Path.GetTempPath() + AppName;
        private string packagefile;
        private Package package;
        private string logfile;
        private string initialConnectionName;
        private string initialPackage;
        private string initialModules;
        private string initialAction;
        private DateTime deployStarted;
        private bool isDeploying = false;

        private bool IsDeploying
        {
            get { return isDeploying; }
            set
            {
                isDeploying = value;
                MethodInvoker mi = delegate
                {
                    btnStartDeploy.Enabled = !value;
                    btnCancelDeploy.Enabled = value;
                    timer.Enabled = value;
                    pictureBox1.Visible = value;
                    lblDeploying.Text = value ? $"Deploying to {ConnectionDetail}" : "<idle>";
                    if (value)
                    {
                        deployStarted = DateTime.Now;
                        btnOpenLog.Enabled = true;
                    }
                };
                if (InvokeRequired) Invoke(mi); else mi();
            }
        }

        public string RepositoryName => "Xrm.Shuffle";

        public string UserName => "rappen";

        #endregion Private parts

        #region Constructor

        public ShuffleDeployer()
        {
            InitializeComponent();
            var configpath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
            if (!Directory.Exists(configpath))
            {
                Directory.CreateDirectory(configpath);
            }
            List<string> args = Environment.GetCommandLineArgs().ToList();

            if (args.Count >= 2 && (args[1].ToLowerInvariant().EndsWith(".cdpkg") || args[1].ToLowerInvariant().EndsWith(".cdzip")))
            {   // Probably started by double clicking a cdpkg file
                initialPackage = args[1];
            }
            else
            {
                initialPackage = ExtractSwitchValue("/pkg:", args);
            }
            initialConnectionName = ExtractSwitchValue("/conn:", args);
            initialModules = ExtractSwitchValue("/mod:", args);
            if (args.Contains("/zipit", StringComparer.InvariantCultureIgnoreCase))
            {
                initialAction = "zipit;";
            }
            if (args.Contains("/silent", StringComparer.InvariantCultureIgnoreCase))
            {
                initialAction += "silent;";
            }
        }

        #endregion Constructor

        #region Event handlers

#pragma warning disable IDE0060 // Remove unused parameter

        private void ShuffleHandler(IExecutionContainer container, object sender, ShuffleEventArgs e)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            AddLogText(container, e.Message, e.ReplaceLastMessage);
            UpdateProgressBars(e.Counters);
        }

        #endregion Event handlers

        #region Form event handlers

        private void ShuffleDeployer_Load(object sender, EventArgs e)
        {
            ai.WriteEvent("Load");
            var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var verinfo = FileVersionInfo.GetVersionInfo(location);
            Text = Text + " " + verinfo.FileVersion;

            if (!string.IsNullOrEmpty(initialPackage))
            {
                OpenAndLoadPackage(initialPackage);
            }
            if (!string.IsNullOrEmpty(initialModules))
            {
                AutoSelectModules(initialModules);
            }
            if (package != null && initialAction != null && initialAction.Split(';').ToList().Contains("zipit"))
            {
                MessageBox.Show("Zipit action called!! - Skipped because of refactoring");
            }
        }

        private void btnPackage_Click(object sender, EventArgs e)
        {
            OpenPackage();
        }

        private void btnDeploy_Click(object sender, EventArgs e)
        {
            StartDeployAsync();
        }

        private void btnCancelDeploy_Click(object sender, EventArgs e)
        {
            throw new NotImplementedException("Need help! How to implement this when deployment is running async??");
        }

        private void lvModules_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (e.Item == null || e.Item.Tag == null)
            {
                return;
            }
            var module = (Module)e.Item.Tag;
            if (!module.Optional && !e.Item.Checked)
            {   // Prevent unchecking not optional modules
                e.Item.Checked = true;
            }
        }

        private void lbBuild_SelectedValueChanged(object sender, EventArgs e)
        {
            if (txtModuleName.Tag != null)
            {
                return;
            }
            txtModuleName.Tag = "Loading";
            if (lbBuild.SelectedItem is Module module)
            {
                txtModuleName.Text = module.Name;
                cmbModuleType.SelectedIndex = 0;
                txtModuleFile.Text = module.File;
                txtModuleDataFile.Text = "";
                SetDataFile();
                if (!string.IsNullOrEmpty(module.DataFile))
                {
                    txtModuleDataFile.Text = module.DataFile;
                }
                chkModuleDefault.Checked = module.Default;
                chkModuleOptional.Checked = module.Optional;
                txtModuleDescr.Text = module.Description;
            }
            else
            {
                txtModuleName.Text = "";
                cmbModuleType.SelectedIndex = -1;
                txtModuleFile.Text = "";
                txtModuleDataFile.Text = "";
                chkModuleDefault.Checked = false;
                chkModuleOptional.Checked = false;
                txtModuleDescr.Text = "";
            }
            cmbModuleType.Enabled = string.IsNullOrEmpty(txtModuleFile.Text);
            txtModuleName.Tag = null;
        }

        private void btnAddModule_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(packagefile))
            {
                SavePackage();
            }
            lbBuild.Items.Add(new Module("undefined"));
            lbBuild.SelectedIndex = lbBuild.Items.Count - 1;
        }

        private void btnDelModule_Click(object sender, EventArgs e)
        {
            if (lbBuild.SelectedItem != null)
            {
                lbBuild.Items.Remove(lbBuild.SelectedItem);
            }
        }

        private void buildModuleInfo_Changed(object sender, EventArgs e)
        {
            if (txtModuleName.Tag != null)
            {
                return;
            }
            txtModuleName.Tag = "Editing";  // Home made recursion prevention
            if (sender == txtModuleFile)
            {
                cmbModuleType.Enabled = string.IsNullOrEmpty(txtModuleFile.Text);
                SetDataFile();
            }
            if (lbBuild.SelectedItem is Module module)
            {
                module.Name = txtModuleName.Text;
                module.Type = ModuleType.ShuffleDefinition;
                module.File = txtModuleFile.Text;
                module.DataFile = txtModuleDataFile.Text;
                module.Optional = chkModuleOptional.Checked;
                module.Default = chkModuleDefault.Checked || !module.Optional;
                module.Description = txtModuleDescr.Text;
                lbBuild.Items[lbBuild.SelectedIndex] = module;  // Force refresh of item
            }
            txtModuleName.Tag = null;
        }

        private void btnSavePackage_Click(object sender, EventArgs e)
        {
            SavePackage();
        }

        private void btnUpModule_Click(object sender, EventArgs e)
        {
            if (lbBuild.SelectedIndex > 0)
            {
                MoveBuildModule(-1);
            }
        }

        private void btnDownModule_Click(object sender, EventArgs e)
        {
            if (lbBuild.SelectedIndex < lbBuild.Items.Count - 1)
            {
                MoveBuildModule(1);
            }
        }

        private void btnOpenDefinition_Click(object sender, EventArgs e)
        {
            SelectDefinitionFile(false);
        }

        private void btnShowDefinition_Click(object sender, EventArgs e)
        {
            ShowFile(txtModuleFile.Text);
        }

        private void btnOpenDataFile_Click(object sender, EventArgs e)
        {
            SelectDefinitionFile(true);
        }

        private void btnShowDataFile_Click(object sender, EventArgs e)
        {
            ShowFile(txtModuleDataFile.Text);
        }

        private void btnZipIt_Click(object sender, EventArgs e)
        {
            ZipIt();
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            var time = DateTime.Now - deployStarted;
            lblTimer.Text = string.Format("{0}:{1:00}", (int)time.TotalMinutes, time.Seconds);
        }

        private void ShuffleDeployer_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.A && e.Shift && e.Control)
            {
                if (!tabControl1.TabPages.Contains(tabPageBuild))
                {
                    tabControl1.TabPages.Add(tabPageBuild);
                }
                else
                {
                    tabControl1.TabPages.Remove(tabPageBuild);
                }
            }
        }

        private void btnOpenWF_Click(object sender, EventArgs e)
        {
            OpenWorkingFolder();
        }

        private void btnOpenLog_Click(object sender, EventArgs e)
        {
            OpenLogFile();
        }

        #endregion Form event handlers

        #region Private methods

        private void OpenPackage()
        {
            var ofd = new OpenFileDialog()
            {
                Filter = $"{AppName}|*.cdzip;*.cdpkg|Deployer Zip (*.cdzip)|*.cdzip|Deployer Package (*.cdpkg)|*.cdpkg|All files (*.*)|*.*",
                Title = AppName + " Package"
            };
            if (ofd.ShowDialog(this) == DialogResult.OK && File.Exists(ofd.FileName))
            {
                OpenAndLoadPackage(ofd.FileName);
            }
        }

        private void OpenAndLoadPackage(string filename)
        {
            if (Path.GetExtension(filename).ToLowerInvariant().Equals(".cdzip"))
            {
                var folder = TempFolder + "\\" + DateTime.Now.ToString("yyyyMMddHHmmss");
                using (var zip = new ZipFile(filename))
                {
                    zip.ExtractAll(folder, ExtractExistingFileAction.OverwriteSilently);
                    filename = zip.Where(f => Path.GetExtension(f.FileName).ToLower() == ".cdpkg").FirstOrDefault().FileName;
                }
                filename = Path.Combine(folder, filename);
            }
            packagefile = filename;
            btnOpenWF.Enabled = File.Exists(packagefile);
            lblPackage.Text = GetDisplayPath(packagefile);
            LoadPackage();
            LoadModules();
            LoadBuild();
            lblBuildModules.Text = File.Exists(packagefile) ? "Modules in: " + GetDisplayPath(packagefile) : "Modules";
        }

        private string GetDisplayPath(string packagefile)
        {
            if (packagefile.StartsWith(TempFolder))
            {
                return packagefile.Replace(TempFolder, "Temp");
            }
            else
            {
                return packagefile;
            }
        }

        private void SavePackage()
        {
            btnZipIt.Enabled = false;
            var sfd = new SaveFileDialog()
            {
                DefaultExt = "*.cdpkg",
                Filter = "Deployer Package (*.cdpkg)|*.cdpkg",
                Title = "Save Deployment Package",
                FileName = Path.GetFileName(packagefile)
            };
            if (sfd.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            packagefile = sfd.FileName;
            btnOpenWF.Enabled = File.Exists(packagefile);
            package = new Package(lbBuild.Items)
            {
                FileOrigin = packagefile,
                FileCreated = DateTime.Now
            };
            XmlSerializerHelper.SerializeToFile(package, packagefile);
            lblBuildModules.Text = File.Exists(packagefile) ? "Modules in: " + packagefile : "Modules";
            btnZipIt.Enabled = true;
        }

        private void LoadPackage()
        {
            XmlSerializer deserializer = new XmlSerializer(typeof(Package));
            TextReader textReader = new StreamReader(packagefile);
            package = (Package)deserializer.Deserialize(textReader);
            textReader.Close();
        }

        private void LoadModules()
        {
            lvModules.Items.Clear();
            if (package == null)
            {
                return;
            }
            foreach (var module in package.Modules)
            {
                var item = lvModules.Items.Add(module.Name);
                item.Tag = module;
                item.SubItems.Add(module.Description);
                item.Checked = module.Default;
                if (!module.Optional)
                {
                    item.ForeColor = Color.DarkGreen;
                }
            }
        }

        private void LoadBuild()
        {
            lbBuild.Items.Clear();
            if (package != null)
            {
                lbBuild.Items.AddRange(package.Modules.ToArray());
            }
        }

        private void MoveBuildModule(int target)
        {
            var mod1 = lbBuild.Items[lbBuild.SelectedIndex];
            var mod2 = lbBuild.Items[lbBuild.SelectedIndex + target];
            lbBuild.Items[lbBuild.SelectedIndex] = mod2;
            lbBuild.Items[lbBuild.SelectedIndex + target] = mod1;
            lbBuild.SelectedIndex += target;
        }

        private void AutoSelectModules(string initialModules)
        {
            throw new NotImplementedException();
        }

        private void StartDeployAsync(bool silent = false)
        {
            if (Service == null)
            {
                MessageBox.Show("Connect to target CRM before deploying.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            List<Module> modules = GetSelectedModules();
            if (modules == null || modules.Count == 0)
            {
                MessageBox.Show("Select modules to deploy.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!silent)
            {
                var modulelist = "";
                foreach (var module in modules)
                {
                    modulelist += "\n  " + module.Name;
                }
                if (MessageBox.Show($"Begin deploy of modules: {modulelist} \nto {ConnectionDetail}", AppName,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
                {
                    return;
                }
            }
            var task = new Task(() =>
            {
                var deployresult = Deploy(modules);
                IsDeploying = false;
                if (string.IsNullOrEmpty(deployresult))
                {
                    MessageBox.Show("Deploy complete", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show($"Deploy stopped with message:\n\n {deployresult}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            });
            IsDeploying = true;
            task.Start();
        }

        private string Deploy(List<Module> selectedModules)
        {
            ai.WriteEvent("Deploying");
            var packagefolder = Path.GetDirectoryName(packagefile);

            logfile = Path.Combine(packagefolder, DateTime.Now.ToString("yyyyMMdd") + "_" + DateTime.Now.ToString("HHmmss") + "_" + Path.GetFileName(packagefile).Replace(".cdpkg", "") + "_" + ConnectionDetail + ".log");
            var container = new CintContainer(Service, logfile);

            var progress = new ShuffleCounter() { Modules = selectedModules.Count };
            var created = 0;
            var updated = 0;
            var skipped = 0;
            var deleted = 0;
            var failed = 0;
            var returnmessage = string.Empty;
            UpdateCounters(0, 0, 0, 0, 0, 0);
            try
            {
                progress.ModuleNo = 0;
                ClearLog();
                ClearProgressBars(false);
                AddLogText(container, $"Deploying package: {packagefile}", false);
                foreach (var module in selectedModules)
                {
                    progress.ModuleNo++;
                    progress.Module = module.ToString();
                    UpdateProgressBars(progress);
                    DeployShuffleDefinition(container, module, packagefolder, ref created, ref updated, ref skipped, ref deleted, ref failed);
                    UpdateCounters(progress.ModuleNo, created, updated, deleted, skipped, failed);
                }
                ClearProgressBars(true);
            }
            catch (Exception ex)
            {
                container.Logger.Log(ex);
                AddLogText(container, "_______________________________________________");
                AddLogText(container, "ERROR: " + ex.Message);
                returnmessage = ex.Message;
                if (container.Logger is FileLogger)
                {
                    AddLogText(container, "See detailed errors in file:");
                    var fileName = (container.Logger as FileLogger).FileName ?? "FileName not available";
                    AddLogText(container, $"  {fileName}");
                }
                else
                {
                    AddLogText(container, "Activate logging to get details about this problem");
                }
            }
            finally
            {
                AddLogText(container, "");
                AddLogText(container, "Deployment completed");
                //SaveLog();

                container.Logger.CloseLog();
            }
            ai.WriteEvent(string.IsNullOrEmpty(returnmessage) ? "Deployed" : "Error");
            return returnmessage;
        }

        private void DeployShuffleDefinition(IExecutionContainer container, Module module, string packagefolder, ref int created, ref int updated, ref int skipped, ref int deleted, ref int failed)
        {
            AddLogText(container, "---");
            AddLogText(container, $"Deploying module: {module}", false);
            var definitionfile = packagefolder + "\\" + module.File;
            AddLogText(container, $"Loading definition: {Path.GetFileName(definitionfile)}", false);
            var definition = new XmlDocument();
            definition.Load(definitionfile);
            var datafile = Path.ChangeExtension(definitionfile, ".data.xml");
            if (!string.IsNullOrWhiteSpace(module.DataFile))
            {
                datafile = GetDefinitionFilePath(module.DataFile);
            }
            container.Logger.Log($"Data File: {datafile}");
            XmlDocument data = null;
            if (File.Exists(datafile))
            {
                AddLogText(container, $"Loading data file: {Path.GetFileName(datafile)}", false);
                data = ShuffleHelper.LoadDataFile(datafile);
            }
            var definitionfolder = Path.GetDirectoryName(definitionfile);

            var result = Shuffler.QuickImport(container, definition, data, (object sender, ShuffleEventArgs e) => { ShuffleHandler(container, sender, e); }, definitionfolder, true);

            AddLogText(container, $"Module {module} totals:");
            AddLogText(container, $"  Created: {result.Item1}");
            AddLogText(container, $"  Updated: {result.Item2}");
            AddLogText(container, $"  Skipped: {result.Item3}");
            AddLogText(container, $"  Deleted: {result.Item4}");
            AddLogText(container, $"  Failed:  {result.Item5}");
            created += result.Item1;
            updated += result.Item2;
            skipped += result.Item3;
            deleted += result.Item4;
            failed += result.Item5;
            if (result.Item5 > 0)
            {   // Fail count > 0
                var shufdef = definition.SelectSingleNode("ShuffleDefinition");
                if (shufdef != null)
                {
                    var stop = XML.GetBoolAttribute(shufdef, "StopOnError", false);
                    if (stop)
                    {
                        throw new Exception("Import failed. See log file for details.");
                    }
                }
            }
        }

        private void ClearProgressBars(bool fill)
        {
            UpdateProgressBars(new ShuffleCounter()
            {
                Module = "",
                Block = "",
                Item = "",
                Modules = fill ? 1 : 0,
                ModuleNo = fill ? 1 : 0,
                Blocks = fill ? 1 : 0,
                BlockNo = fill ? 1 : 0,
                Items = fill ? 1 : 0,
                ItemNo = fill ? 1 : 0
            });
        }

        private void UpdateCounters(int modules, int created, int updated, int deleted, int skipped, int failed)
        {
            MethodInvoker mi = delegate
            {
                lblDeployModules.Text = modules.ToString();
                lblDeployCreated.Text = created.ToString();
                lblDeployUpdated.Text = updated.ToString();
                lblDeployDeleted.Text = deleted.ToString();
                lblDeploySkipped.Text = skipped.ToString();
                lblDeployFailed.Text = failed.ToString();
            };
            if (InvokeRequired) Invoke(mi); else mi();
        }

        private void AddLogText(IExecutionContainer container, string text, bool replace = false)
        {
            if (text == null)
            {
                return;
            }
            if (text == "" || !string.IsNullOrWhiteSpace(text.Trim()))
            {
                if (container.Logger != null)
                {
                    container.Logger.Log(text);
                }
                MethodInvoker mi = delegate
                {
                    if (replace && lbLog.Items.Count > 0)
                    {
                        lbLog.Items[lbLog.Items.Count - 1] = text;
                    }
                    else
                    {
                        lbLog.Items.Add(text);
                    }
                    lbLog.SelectedIndex = lbLog.Items.Count - 1;
                };
                if (InvokeRequired) Invoke(mi); else mi();
            }
        }

        private void ClearLog()
        {
            MethodInvoker mi = delegate { lbLog.Items.Clear(); };
            if (InvokeRequired) Invoke(mi); else mi();
        }

        //private void SaveLog()
        //{
        //    MethodInvoker mi = delegate
        //    {
        //        var packagefolder = Path.GetDirectoryName(packagefile);
        //        var file = "Deploy_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_") + Path.GetFileName(packagefile).Replace(".cdpkg", "") + "_" + ConnectionDetail + ".log";
        //        file = packagefolder + "\\" + file;
        //        System.IO.StreamWriter SaveFile = new System.IO.StreamWriter(file);
        //        foreach (var item in lbLog.Items)
        //        {
        //            SaveFile.WriteLine(item);
        //        }

        //        SaveFile.Close();
        //    };
        //    if (InvokeRequired) Invoke(mi); else mi();
        //}

        private void UpdateProgressBars(ShuffleCounter counters)
        {
            MethodInvoker mi = delegate
            {
                if (counters.Module != null)
                {
                    lblCurrModule.Text = counters.Module;
                }
                if (counters.Block != null)
                {
                    lblCurrBlock.Text = counters.Block;
                }
                if (counters.Item != null)
                {
                    lblCurrItem.Text = counters.Item;
                }
                if (counters.Modules >= 0 && counters.ModuleNo >= 0)
                {
                    pbModule.Maximum = counters.Modules;
                    pbModule.Value = counters.ModuleNo;
                }
                if (counters.Blocks >= 0 && counters.BlockNo >= 0)
                {
                    pbBlock.Maximum = counters.Blocks;
                    pbBlock.Value = counters.BlockNo;
                }
                if (counters.Items >= 0 && counters.ItemNo >= 0)
                {
                    pbRecord.Maximum = counters.Items;
                    pbRecord.Value = counters.ItemNo;
                }
            };
            if (InvokeRequired) Invoke(mi); else mi();
        }

        private List<Module> GetSelectedModules()
        {
            var modules = new List<Module>();
            foreach (ListViewItem item in lvModules.CheckedItems)
            {
                if (item.Tag is Module)
                {
                    modules.Add((Module)item.Tag);
                }
            }
            return modules;
        }

        private void SelectDefinitionFile(bool isDataFile)
        {
            var packdir = Path.GetDirectoryName(packagefile);
            var file = GetDefinitionFilePath(!isDataFile || string.IsNullOrWhiteSpace(txtModuleDataFile.Text) ? txtModuleFile.Text : txtModuleDataFile.Text);
            var dir = !string.IsNullOrWhiteSpace(file) ? file : packdir;
            var ofd = new OpenFileDialog()
            {
                DefaultExt = "*.xml",
                Filter = "XML file|*.xml",
                Title = "Select Data File",
                InitialDirectory = dir,
                FileName = file
            };
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                if (File.Exists(packagefile))
                {
                    file = MakeRelativePath(packagefile, ofd.FileName);
                }
                else
                {
                    file = Path.GetFileName(ofd.FileName);
                }
                if (!isDataFile)
                {
                    txtModuleFile.Text = file;
                }
                else
                {
                    txtModuleDataFile.Text = file;
                }
            }
        }

        private void SetDataFile()
        {
            var def = GetDefinitionFilePath(txtModuleFile.Text);
            if (File.Exists(def))
            {
                if (ShuffleHelper.DataFileRequired(def))
                {
                    var data = GetDefinitionFilePath(txtModuleDataFile.Text);
                    if (!File.Exists(data))
                    {
                        txtModuleDataFile.Text = Path.ChangeExtension(txtModuleFile.Text, ".data.xml");
                    }
                    txtModuleDataFile.Enabled = true;
                }
                else
                {
                    txtModuleDataFile.Text = "";
                    txtModuleDataFile.Enabled = false;
                }
            }
            btnOpenDataFile.Enabled = txtModuleDataFile.Enabled;
            btnShowDataFile.Enabled = txtModuleDataFile.Enabled;
        }

        private void ShowFile(string file)
        {
            var defpath = GetDefinitionFilePath(file);
            if (File.Exists(defpath))
            {
                System.Diagnostics.Process.Start(defpath);
            }
            else
            {
                MessageBox.Show("File \"" + defpath + "\" does not exist");
            }
        }

        private string GetDefinitionFilePath(string definitionrelativepath)
        {
            if (string.IsNullOrWhiteSpace(definitionrelativepath))
            {
                return "";
            }
            var packdir = packagefile != null ? Path.GetDirectoryName(packagefile) : ".";
            var defpath = Path.Combine(packdir, definitionrelativepath);
            return defpath;
        }

        private void ZipIt(IExecutionContainer container = null, bool defaultFile = false, bool silent = false)
        {
            var zipfile = string.Empty;
            if (defaultFile)
            {
                zipfile = Path.ChangeExtension(packagefile, ".cdzip");
            }
            else
            {
                var sfd = new SaveFileDialog()
                {
                    DefaultExt = "*.cdzip",
                    Filter = "Innofactor CRM Deployer Zip (*.cdzip)|*.cdzip",
                    Title = "Zip It!",
                    FileName = Path.GetFileName(packagefile).Replace(".cdpkg", ".cdzip")
                };
                if (sfd.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
                zipfile = sfd.FileName;
            }
            var zipfolder = Path.GetDirectoryName(zipfile);
            var ziplogfile = DateTime.Now.ToString("HHmmss") + "_ZipIt_" + Path.GetFileName(zipfile).Replace(".cdzip", "");
            var logfile = Path.Combine(zipfolder,
                                    DateTime.Now.ToString("yyyyMMdd") + "_" +
                                    DateTime.Now.ToString("HHmmss") + "_" +
                                    Path.ChangeExtension(Path.GetFileName(zipfile), ".log"));
            var log = new FileLogger(logfile);
            try
            {
                var tmpPackageFile = Path.ChangeExtension(zipfile, ".cdpkg");
                var tmpPackageFileExisted = File.Exists(tmpPackageFile);
                log.Log("PackageFile: {0} (existed: {1})", tmpPackageFile, tmpPackageFileExisted);
                package = new Package(lbBuild.Items)
                {
                    FileOrigin = $"{zipfile} ({Path.GetFileName(tmpPackageFile)})",
                    FileCreated = DateTime.Now
                };
                log.Log("Serializing package file");
                XmlSerializerHelper.SerializeToFile(package, tmpPackageFile);
                var packagefolder = Path.GetDirectoryName(packagefile);
                log.Log("Looking for content files in: {0}", packagefolder);
                var files = new List<string>();
                files.Add(tmpPackageFile);

                foreach (var module in package.Modules)
                {
                    log.StartSection(module.ToString());
                    log.Log("Investigating definition: {0}", module.File);
                    var deffile = GetDefinitionFilePath(module.File);
                    if (!File.Exists(deffile))
                    {
                        log.Log("Missing definition file: {0}", deffile);
                        MessageBox.Show("Definition file missing:\n" + deffile + "\n\nCorrect the path and try again.", "Zip It!",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    log.Log("Found definition file: {0}", deffile);
                    var modulefiles = new List<string>();
                    if (!string.IsNullOrEmpty(module.DataFile))
                    {
                        var datafile = GetDefinitionFilePath(module.DataFile);
                        log.Log("Adding data file: {0}", datafile);
                        modulefiles.Add(datafile);
                    }
                    else if (module.Type == ModuleType.ShuffleDefinition)
                    {
                        modulefiles = ShuffleHelper.GetReferencedFiles(container, deffile, packagefolder);
                    }
                    foreach (var modfile in modulefiles)
                    {
                        if (!File.Exists(modfile))
                        {
                            log.Log("Missing module file: {0}", modfile);
                            MessageBox.Show("File missing:\n" + modfile + "\n\nRequired by:\n" + deffile + "\n\nCorrect this and try again.", "Zip It!",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }
                    modulefiles.Add(deffile);
                    log.Log("Adding {0} module files to package", modulefiles.Count);
                    files.AddRange(modulefiles.Where(f => !files.Contains(f)));
                    module.File = Path.GetFileName(module.File);    // In the zip all files will be in the same folder, so no relative paths necessary
                    log.EndSection();
                }
                if (File.Exists(zipfile))
                {
                    log.Log("Deleting existing cdzip file: {0}", zipfile);
                    File.Delete(zipfile);
                }
                log.Log("Zipping package to: {0}", zipfile);
                using (ZipFile zip = new ZipFile(zipfile))
                {
                    zip.AddFiles(files, false, "");
                    zip.Save();
                }
                if (!tmpPackageFileExisted)
                {   // Remove temporary package file if it did not exist
                    log.Log("Deleting temp package file: {0}", tmpPackageFile);
                    File.Delete(tmpPackageFile);
                }
                if (!silent)
                {
                    MessageBox.Show($"Packed {files.Count} files into {Path.GetFileName(zipfile)} !", "ZipIt!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                log.Log("Package successully zipped with {0} files in: {1}", files.Count, zipfile);
            }
            finally
            {
                log.CloseLog();
            }
        }

        private void OpenWorkingFolder()
        {
            if (!string.IsNullOrEmpty(packagefile))
            {
                var packagefolder = Path.GetDirectoryName(packagefile);
                System.Diagnostics.Process.Start(packagefolder);
            }
        }

        private new void OpenLogFile()
        {
            if (File.Exists(logfile))
            {
                System.Diagnostics.Process.Start(logfile);
            }
            else
            {
                MessageBox.Show($"Cannot open log file:\n{logfile}", "Log file", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion Private methods

        #region Static helpers

        /// <summary>Creates a relative path from one file or folder to another.</summary>
        /// <remarks>http://stackoverflow.com/questions/275689/how-to-get-relative-path-from-absolute-path</remarks>
        /// <param name="fromPath">Contains the directory that defines the start of the relative path.</param>
        /// <param name="toPath">Contains the path that defines the endpoint of the relative path.</param>
        /// <returns>The relative path from the start directory to the end path or <c>toPath</c> if the paths are not related.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="UriFormatException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        ///
        public static string MakeRelativePath(string fromPath, string toPath)
        {
            if (String.IsNullOrEmpty(fromPath)) throw new ArgumentNullException("fromPath");
            if (String.IsNullOrEmpty(toPath)) throw new ArgumentNullException("toPath");

            Uri fromUri = new Uri(fromPath);
            Uri toUri = new Uri(toPath);

            if (fromUri.Scheme != toUri.Scheme) { return toPath; } // path can't be made relative.

            Uri relativeUri = fromUri.MakeRelativeUri(toUri);
            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            if (toUri.Scheme.ToUpperInvariant() == "FILE")
            {
                relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }

            return relativePath;
        }

        private static string ExtractSwitchValue(string key, List<string> args)
        {
            var name = string.Empty;

            foreach (var arg in args)
            {
                if (arg.StartsWith(key))
                {
                    name = arg.Substring(key.Length);
                }
            }

            return name;
        }

        #endregion Static helpers
    }
}