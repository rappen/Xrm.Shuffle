﻿using System.Collections.Generic;

namespace Rappen.XTB.Shuffle.Builder.Controls
{
    public partial class SolutionBlockImportControl : ControlBase
    {
        public SolutionBlockImportControl(Dictionary<string, string> collection, ShuffleBuilder shuffleBuilder)
            : base(collection, shuffleBuilder) { }

        public override ControlCollection GetControls()
        {
            if (InitializationNeeded(Controls))
            {
                InitializeComponent();
            }
            return Controls;
        }
    }
}
