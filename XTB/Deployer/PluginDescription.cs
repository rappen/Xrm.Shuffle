﻿using Rappen.XTB.Shuffle.XTB;
using System;
using System.ComponentModel.Composition;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace Rappen.XTB.ShuffleDeployer
{
    [Export(typeof(IXrmToolBoxPlugin)),
        ExportMetadata("Name", "Shuffle Deployer"),
        ExportMetadata("Description", "Deploy solutions and datas with the Shuffle. Empower yourself to achieve more."),
        ExportMetadata("SmallImageBase64", "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"),
        ExportMetadata("BigImageBase64", "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"),
        ExportMetadata("BackgroundColor", XTBConst.BGColor),
        ExportMetadata("PrimaryFontColor", XTBConst.PFColor),
        ExportMetadata("SecondaryFontColor", XTBConst.SFColor)]
    public partial class PluginDescription : PluginBase
    {
        #region Public Methods

        public override IXrmToolBoxPluginControl GetControl()
        {
            return new ShuffleDeployer();
        }

        #endregion Public Methods
    }
}