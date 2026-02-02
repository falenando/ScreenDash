namespace HostApp
{
    partial class HostForm
    {
        private System.ComponentModel.IContainer components = null;
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            lblTitle = new Label();
            txtTokenRemoto = new TextBox();
            lblYourAccess = new Label();
            btnCopyCode = new Button();
            btnStart = new Button();
            chkAllowRemoteInput = new CheckBox();
            lblRemoteControlStatus = new Label();
            txtLog = new TextBox();
            panelStatus = new Panel();
            pbStatus = new PictureBox();
            lblStatus = new Label();
            btnQuit = new Button();
            panelStatus.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pbStatus).BeginInit();
            SuspendLayout();
            // 
            // lblTitle
            // 
            lblTitle.AutoSize = true;
            lblTitle.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            lblTitle.Location = new Point(12, 9);
            lblTitle.Name = "lblTitle";
            lblTitle.Size = new Size(147, 28);
            lblTitle.TabIndex = 0;
            lblTitle.Text = "Support Share";
            // 
            // txtTokenRemoto
            // 
            txtTokenRemoto.Font = new Font("Consolas", 18F);
            txtTokenRemoto.Location = new Point(14, 61);
            txtTokenRemoto.Name = "txtTokenRemoto";
            txtTokenRemoto.ReadOnly = true;
            txtTokenRemoto.Size = new Size(306, 43);
            txtTokenRemoto.TabIndex = 1;
            txtTokenRemoto.Text = "123 456 789 XYZ";
            txtTokenRemoto.TextAlign = HorizontalAlignment.Center;
            // 
            // lblYourAccess
            // 
            lblYourAccess.AutoSize = true;
            lblYourAccess.Location = new Point(182, 16);
            lblYourAccess.Name = "lblYourAccess";
            lblYourAccess.Size = new Size(121, 20);
            lblYourAccess.TabIndex = 3;
            lblYourAccess.Text = "Your access code";
            // 
            // btnCopyCode
            // 
            btnCopyCode.Location = new Point(17, 135);
            btnCopyCode.Name = "btnCopyCode";
            btnCopyCode.Size = new Size(142, 30);
            btnCopyCode.TabIndex = 6;
            btnCopyCode.Text = "Copy code";
            btnCopyCode.UseVisualStyleBackColor = true;
            // 
            // btnStart
            // 
            btnStart.Location = new Point(182, 135);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(136, 30);
            btnStart.TabIndex = 7;
            btnStart.Text = "Start sharing";
            btnStart.UseVisualStyleBackColor = true;
            btnStart.Click += btnStart_Click;
            // 
            // chkAllowRemoteInput
            // 
            chkAllowRemoteInput.AutoSize = true;
            chkAllowRemoteInput.Checked = true;
            chkAllowRemoteInput.CheckState = CheckState.Checked;
            chkAllowRemoteInput.Location = new Point(14, 182);
            chkAllowRemoteInput.Name = "chkAllowRemoteInput";
            chkAllowRemoteInput.Size = new Size(159, 24);
            chkAllowRemoteInput.TabIndex = 8;
            chkAllowRemoteInput.Text = "Allow remote input";
            chkAllowRemoteInput.UseVisualStyleBackColor = true;
            // 
            // lblRemoteControlStatus
            // 
            lblRemoteControlStatus.AutoSize = true;
            lblRemoteControlStatus.ForeColor = Color.Gray;
            lblRemoteControlStatus.Location = new Point(14, 206);
            lblRemoteControlStatus.Name = "lblRemoteControlStatus";
            lblRemoteControlStatus.Size = new Size(176, 20);
            lblRemoteControlStatus.TabIndex = 9;
            lblRemoteControlStatus.Text = "Remote control inactive";
            // 
            // txtLog
            // 
            txtLog.Location = new Point(14, 232);
            txtLog.Multiline = true;
            txtLog.Name = "txtLog";
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Size = new Size(306, 143);
            txtLog.TabIndex = 10;
            // 
            // panelStatus
            // 
            panelStatus.Controls.Add(pbStatus);
            panelStatus.Controls.Add(lblStatus);
            panelStatus.Location = new Point(14, 385);
            panelStatus.Name = "panelStatus";
            panelStatus.Size = new Size(220, 40);
            panelStatus.TabIndex = 11;
            // 
            // pbStatus
            // 
            pbStatus.BackColor = Color.LimeGreen;
            pbStatus.Location = new Point(3, 8);
            pbStatus.Name = "pbStatus";
            pbStatus.Size = new Size(16, 16);
            pbStatus.TabIndex = 0;
            pbStatus.TabStop = false;
            // 
            // lblStatus
            // 
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(28, 6);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(171, 20);
            lblStatus.TabIndex = 1;
            lblStatus.Text = "Status: Ready to connect";
            // 
            // btnQuit
            // 
            btnQuit.Location = new Point(250, 390);
            btnQuit.Name = "btnQuit";
            btnQuit.Size = new Size(70, 30);
            btnQuit.TabIndex = 12;
            btnQuit.Text = "Quit";
            btnQuit.UseVisualStyleBackColor = true;
            // 
            // HostForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(334, 441);
            Controls.Add(btnQuit);
            Controls.Add(panelStatus);
            Controls.Add(txtLog);
            Controls.Add(lblRemoteControlStatus);
            Controls.Add(chkAllowRemoteInput);
            Controls.Add(btnStart);
            Controls.Add(btnCopyCode);
            Controls.Add(lblYourAccess);
            Controls.Add(txtTokenRemoto);
            Controls.Add(lblTitle);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Name = "HostForm";
            Text = "Support Share";
            // initial status: not ready
            pbStatus.BackColor = System.Drawing.Color.Red;
            lblStatus.Text = "Not ready to connect";
            panelStatus.ResumeLayout(false);
            panelStatus.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pbStatus).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.TextBox txtTokenRemoto;
        private System.Windows.Forms.Label lblYourAccess;
        private System.Windows.Forms.Button btnCopyCode;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.CheckBox chkAllowRemoteInput;
        private System.Windows.Forms.Label lblRemoteControlStatus;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.Panel panelStatus;
        private System.Windows.Forms.PictureBox pbStatus;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Button btnQuit;
    }
}
