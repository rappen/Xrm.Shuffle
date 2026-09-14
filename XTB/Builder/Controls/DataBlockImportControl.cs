using System.Collections.Generic;
using System.Windows.Forms;

namespace Rappen.XTB.Shuffle.Builder.Controls
{
    public partial class DataBlockImportControl : ControlBase
    {
        private const string DeferStateAndOwnerHelp =
            "Defer state and owner:\r\n" +
            "Records that carry statecode, statuscode or ownerid cannot be sent in a bulk request, so they " +
            "are imported one at a time. With this option those attributes are stripped off before the " +
            "record is saved and applied afterwards in a second pass, which keeps the records themselves " +
            "on the batched path.\r\n" +
            "Benefit: noticeably faster import of blocks where most or all records are inactive or owned by " +
            "someone other than the importing user. The end result is the same - every record still ends up " +
            "with its intended state and owner.";

        public DataBlockImportControl(Dictionary<string, string> collection, ShuffleBuilder shuffleBuilder)
            : base(collection, shuffleBuilder)
        {
            var overwrite = collection.ContainsKey("Overwrite") ? collection["Overwrite"] : "";
            if (!string.IsNullOrWhiteSpace(overwrite))
            {
                txtOverwrite.Text = overwrite;
                lblDeprecated.Visible = true;
                lblDeprOverwrite.Visible = true;
                txtOverwrite.Visible = true;
                MessageBox.Show("This is currently using deprecated attribute Overwrite.\nPlease specify Save-option instead.");
            }
        }

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
