namespace Rappen.XTB.Shuffle.Builder.Controls
{
    partial class ImportMatchControl
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.chkPreRetrieveAll = new System.Windows.Forms.CheckBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // chkPreRetrieveAll
            // 
            this.chkPreRetrieveAll.AutoSize = true;
            this.chkPreRetrieveAll.Location = new System.Drawing.Point(213, 7);
            this.chkPreRetrieveAll.Name = "chkPreRetrieveAll";
            this.chkPreRetrieveAll.Size = new System.Drawing.Size(15, 14);
            this.chkPreRetrieveAll.TabIndex = 2;
            this.chkPreRetrieveAll.Tag = "PreRetrieveAll|false|false";
            this.chkPreRetrieveAll.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(4, 7);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(117, 13);
            this.label1.TabIndex = 3;
            this.label1.Text = "Pre Retrieve all records";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(5, 20);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(94, 13);
            this.label2.TabIndex = 4;
            this.label2.Text = "for faster matching";
            // 
            // ImportMatchControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.chkPreRetrieveAll);
            this.Name = "ImportMatchControl";
            this.Tag = "Match";
            this.Controls.SetChildIndex(this.chkPreRetrieveAll, 0);
            this.Controls.SetChildIndex(this.label1, 0);
            this.Controls.SetChildIndex(this.label2, 0);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.CheckBox chkPreRetrieveAll;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
    }
}
