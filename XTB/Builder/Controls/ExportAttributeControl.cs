﻿using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Rappen.XTB.Shuffle.Builder.Controls
{
    public partial class ExportAttributeControl : ControlBase
    {
        string entity;

        public ExportAttributeControl(Dictionary<string, string> collection, ShuffleBuilder shuffleBuilder, string entity)
            : base(collection, shuffleBuilder)
        {
            this.entity = entity;
        }

        public override ControlCollection GetControls()
        {
            if (InitializationNeeded(Controls))
            {
                InitializeComponent();
            }
            return Controls;
        }

        public override void PopulateControls()
        {
            if (shuffleBuilder.Attributes == null || !shuffleBuilder.Attributes.ContainsKey(entity))
            {
                shuffleBuilder.LoadAttributes(entity, PopulateControls);
                return;
            }
            cmbAttribute.Items.Clear();
            cmbAttribute.Items.AddRange(shuffleBuilder.Attributes[entity].OrderBy(e => e.LogicalName).Select(e => e.LogicalName).ToArray());
            if (cmbAttribute.Items.Count > 0)
            {
                cmbAttribute.DropDownStyle = ComboBoxStyle.DropDown;
            }
            else
            {
                cmbAttribute.DropDownStyle = ComboBoxStyle.Simple;
            }
            base.PopulateControls();
        }
    }
}
