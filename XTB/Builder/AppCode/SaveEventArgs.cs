﻿// PROJECT : MsCrmTools.SiteMapEditor
// This project was developed by Tanguy Touzard
// CODEPLEX: http://xrmtoolbox.codeplex.com
// BLOG: http://mscrmtools.blogspot.com

using System;
using System.Collections.Generic;

namespace Rappen.XTB.Shuffle.Builder.AppCode
{
    public class SaveEventArgs : EventArgs
    {
        public Dictionary<string, string> AttributeCollection { get; set; }
    }
}
