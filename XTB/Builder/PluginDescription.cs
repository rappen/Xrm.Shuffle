﻿namespace Rappen.XTB.Shuffle.Builder
{
    using Rappen.XTB.Shuffle.XTB;
    using System.ComponentModel.Composition;
    using XrmToolBox.Extensibility;
    using XrmToolBox.Extensibility.Interfaces;

    [Export(typeof(IXrmToolBoxPlugin)),
        ExportMetadata("Name", "Shuffle Builder"),
        ExportMetadata("Description", "Build definition files for the Shuffle"),
        ExportMetadata("SmallImageBase64", "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"),
        ExportMetadata("BigImageBase64", "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"),
        ExportMetadata("BackgroundColor", XTBConst.BGColor),
        ExportMetadata("PrimaryFontColor", XTBConst.PFColor),
        ExportMetadata("SecondaryFontColor", XTBConst.SFColor)]
    public class PluginDescription : PluginBase
    {
        #region Public Methods

        public override IXrmToolBoxPluginControl GetControl()
        {
            return new ShuffleBuilder();
        }

        #endregion Public Methods
    }
}