namespace Rappen.XTB.Shuffle.Builder.Controls
{
    partial class ShuffleDefinitionControl
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
            this.lblTimeout = new System.Windows.Forms.Label();
            this.txtTimeout = new System.Windows.Forms.TextBox();
            this.chkStopOnError = new System.Windows.Forms.CheckBox();
            this.lblStopOnError = new System.Windows.Forms.Label();
            this.chkBypassSyncLogic = new System.Windows.Forms.CheckBox();
            this.lblBypassSyncLogic = new System.Windows.Forms.Label();
            this.chkBypassAsyncLogic = new System.Windows.Forms.CheckBox();
            this.lblBypassAsyncLogic = new System.Windows.Forms.Label();
            this.chkBypassFlows = new System.Windows.Forms.CheckBox();
            this.lblBypassFlows = new System.Windows.Forms.Label();
            this.components = new System.ComponentModel.Container();
            this.tooltips = new System.Windows.Forms.ToolTip(this.components);
            this.SuspendLayout();
            // 
            // lblTimeout
            // 
            this.lblTimeout.AutoSize = true;
            this.lblTimeout.Location = new System.Drawing.Point(4, 7);
            this.lblTimeout.Name = "lblTimeout";
            this.lblTimeout.Size = new System.Drawing.Size(45, 13);
            this.lblTimeout.TabIndex = 0;
            this.lblTimeout.Text = "Timeout";
            // 
            // txtTimeout
            // 
            this.txtTimeout.Location = new System.Drawing.Point(213, 4);
            this.txtTimeout.Name = "txtTimeout";
            this.txtTimeout.Size = new System.Drawing.Size(100, 20);
            this.txtTimeout.TabIndex = 1;
            this.txtTimeout.Tag = "Timeout";
            // 
            // chkStopOnError
            // 
            this.chkStopOnError.AutoSize = true;
            this.chkStopOnError.Location = new System.Drawing.Point(213, 30);
            this.chkStopOnError.Name = "chkStopOnError";
            this.chkStopOnError.Size = new System.Drawing.Size(15, 14);
            this.chkStopOnError.TabIndex = 2;
            this.chkStopOnError.Tag = "StopOnError";
            this.chkStopOnError.UseVisualStyleBackColor = true;
            // 
            // lblStopOnError
            // 
            this.lblStopOnError.AutoSize = true;
            this.lblStopOnError.Location = new System.Drawing.Point(4, 30);
            this.lblStopOnError.Name = "lblStopOnError";
            this.lblStopOnError.Size = new System.Drawing.Size(68, 13);
            this.lblStopOnError.TabIndex = 3;
            this.lblStopOnError.Text = "Stop on error";
            // 
            // chkBypassSyncLogic
            // 
            this.chkBypassSyncLogic.AutoSize = true;
            this.chkBypassSyncLogic.Location = new System.Drawing.Point(213, 53);
            this.chkBypassSyncLogic.Name = "chkBypassSyncLogic";
            this.chkBypassSyncLogic.Size = new System.Drawing.Size(15, 14);
            this.chkBypassSyncLogic.TabIndex = 4;
            this.chkBypassSyncLogic.Tag = "BypassSyncLogic|false|false";
            this.tooltips.SetToolTip(this.chkBypassSyncLogic, "Import without running synchronous plugins and workflows. Needs the prvBypassCustomBusinessLogic privilege.");
            this.chkBypassSyncLogic.UseVisualStyleBackColor = true;
            // 
            // lblBypassSyncLogic
            // 
            this.lblBypassSyncLogic.AutoSize = true;
            this.lblBypassSyncLogic.Location = new System.Drawing.Point(4, 53);
            this.lblBypassSyncLogic.Name = "lblBypassSyncLogic";
            this.lblBypassSyncLogic.TabIndex = 5;
            this.lblBypassSyncLogic.Text = "Bypass sync logic";
            this.tooltips.SetToolTip(this.lblBypassSyncLogic, "Import without running synchronous plugins and workflows. Needs the prvBypassCustomBusinessLogic privilege.");
            // 
            // chkBypassAsyncLogic
            // 
            this.chkBypassAsyncLogic.AutoSize = true;
            this.chkBypassAsyncLogic.Location = new System.Drawing.Point(213, 76);
            this.chkBypassAsyncLogic.Name = "chkBypassAsyncLogic";
            this.chkBypassAsyncLogic.Size = new System.Drawing.Size(15, 14);
            this.chkBypassAsyncLogic.TabIndex = 6;
            this.chkBypassAsyncLogic.Tag = "BypassAsyncLogic|false|false";
            this.tooltips.SetToolTip(this.chkBypassAsyncLogic, "Import without running asynchronous plugins and workflows. Needs the prvBypassCustomBusinessLogic privilege. Dataverse only.");
            this.chkBypassAsyncLogic.UseVisualStyleBackColor = true;
            // 
            // lblBypassAsyncLogic
            // 
            this.lblBypassAsyncLogic.AutoSize = true;
            this.lblBypassAsyncLogic.Location = new System.Drawing.Point(4, 76);
            this.lblBypassAsyncLogic.Name = "lblBypassAsyncLogic";
            this.lblBypassAsyncLogic.TabIndex = 7;
            this.lblBypassAsyncLogic.Text = "Bypass async logic";
            this.tooltips.SetToolTip(this.lblBypassAsyncLogic, "Import without running asynchronous plugins and workflows. Needs the prvBypassCustomBusinessLogic privilege. Dataverse only.");
            // 
            // chkBypassFlows
            // 
            this.chkBypassFlows.AutoSize = true;
            this.chkBypassFlows.Location = new System.Drawing.Point(213, 99);
            this.chkBypassFlows.Name = "chkBypassFlows";
            this.chkBypassFlows.Size = new System.Drawing.Size(15, 14);
            this.chkBypassFlows.TabIndex = 8;
            this.chkBypassFlows.Tag = "BypassFlows|false|false";
            this.tooltips.SetToolTip(this.chkBypassFlows, "Import without triggering Power Automate flows. Dataverse only.");
            this.chkBypassFlows.UseVisualStyleBackColor = true;
            // 
            // lblBypassFlows
            // 
            this.lblBypassFlows.AutoSize = true;
            this.lblBypassFlows.Location = new System.Drawing.Point(4, 99);
            this.lblBypassFlows.Name = "lblBypassFlows";
            this.lblBypassFlows.TabIndex = 9;
            this.lblBypassFlows.Text = "Bypass flows";
            this.tooltips.SetToolTip(this.lblBypassFlows, "Import without triggering Power Automate flows. Dataverse only.");
            // 
            // ShuffleDefinitionControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.lblBypassFlows);
            this.Controls.Add(this.chkBypassFlows);
            this.Controls.Add(this.lblBypassAsyncLogic);
            this.Controls.Add(this.chkBypassAsyncLogic);
            this.Controls.Add(this.lblBypassSyncLogic);
            this.Controls.Add(this.chkBypassSyncLogic);
            this.Controls.Add(this.lblStopOnError);
            this.Controls.Add(this.chkStopOnError);
            this.Controls.Add(this.txtTimeout);
            this.Controls.Add(this.lblTimeout);
            this.Name = "ShuffleDefinitionControl";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblTimeout;
        private System.Windows.Forms.TextBox txtTimeout;
        private System.Windows.Forms.CheckBox chkStopOnError;
        private System.Windows.Forms.Label lblStopOnError;
        private System.Windows.Forms.CheckBox chkBypassSyncLogic;
        private System.Windows.Forms.Label lblBypassSyncLogic;
        private System.Windows.Forms.CheckBox chkBypassAsyncLogic;
        private System.Windows.Forms.Label lblBypassAsyncLogic;
        private System.Windows.Forms.CheckBox chkBypassFlows;
        private System.Windows.Forms.Label lblBypassFlows;
        private System.Windows.Forms.ToolTip tooltips;
    }
}
