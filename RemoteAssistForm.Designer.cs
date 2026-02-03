namespace ScreenDash
{
    partial class RemoteAssistForm
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

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            lblTitle = new Label();
            lblAccessCode = new Label();
            txtAccessCode = new TextBox();
            btnConnect = new Button();
            txtPreview = new TextBox();
            panelStatus = new Panel();
            pbStatus = new PictureBox();
            lblStatus = new Label();
            btnDisconnect = new Button();
            panelStatus.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pbStatus).BeginInit();
            SuspendLayout();
            // 
            // lblTitle
            // 
            lblTitle.AutoSize = true;
            lblTitle.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            lblTitle.Location = new Point(17, 9);
            lblTitle.Name = "lblTitle";
            lblTitle.Size = new Size(146, 28);
            lblTitle.TabIndex = 0;
            lblTitle.Text = "Remote Assist";
            // 
            // lblAccessCode
            // 
            lblAccessCode.AutoSize = true;
            lblAccessCode.Location = new Point(17, 52);
            lblAccessCode.Name = "lblAccessCode";
            lblAccessCode.Size = new Size(237, 20);
            lblAccessCode.TabIndex = 1;
            lblAccessCode.Text = "Enter the access code of the client:";
            // 
            // txtAccessCode
            // 
            txtAccessCode.Font = new Font("Consolas", 18F);
            txtAccessCode.Location = new Point(17, 75);
            txtAccessCode.Name = "txtAccessCode";
            txtAccessCode.Size = new Size(303, 43);
            txtAccessCode.TabIndex = 2;
            // 
            // btnConnect
            // 
            btnConnect.Location = new Point(14, 124);
            btnConnect.Name = "btnConnect";
            btnConnect.Size = new Size(308, 53);
            btnConnect.TabIndex = 5;
            btnConnect.Text = "Connect";
            btnConnect.UseVisualStyleBackColor = true;
            // 
            // txtPreview
            // 
            txtPreview.Location = new Point(14, 183);
            txtPreview.Multiline = true;
            txtPreview.Name = "txtPreview";
            txtPreview.ReadOnly = true;
            txtPreview.ScrollBars = ScrollBars.Vertical;
            txtPreview.Size = new Size(306, 197);
            txtPreview.TabIndex = 7;
            // 
            // panelStatus
            // 
            panelStatus.Controls.Add(pbStatus);
            panelStatus.Controls.Add(lblStatus);
            panelStatus.Location = new Point(14, 395);
            panelStatus.Name = "panelStatus";
            panelStatus.Size = new Size(220, 40);
            panelStatus.TabIndex = 8;
            // 
            // pbStatus
            // 
            pbStatus.BackColor = Color.Red;
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
            lblStatus.Size = new Size(146, 20);
            lblStatus.TabIndex = 1;
            lblStatus.Text = "Status: Disconnected";
            // 
            // btnDisconnect
            // 
            btnDisconnect.Location = new Point(250, 395);
            btnDisconnect.Name = "btnDisconnect";
            btnDisconnect.Size = new Size(70, 30);
            btnDisconnect.TabIndex = 9;
            btnDisconnect.Text = "Disconnect";
            btnDisconnect.UseVisualStyleBackColor = true;
            // 
            // RemoteAssistForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(334, 441);
            Controls.Add(btnDisconnect);
            Controls.Add(panelStatus);
            Controls.Add(txtPreview);
            Controls.Add(btnConnect);
            Controls.Add(txtAccessCode);
            Controls.Add(lblAccessCode);
            Controls.Add(lblTitle);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Name = "RemoteAssistForm";
            Text = "Remote Assist";
            panelStatus.ResumeLayout(false);
            panelStatus.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pbStatus).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblAccessCode;
        private System.Windows.Forms.TextBox txtAccessCode;
        private System.Windows.Forms.Button btnConnect;
        private System.Windows.Forms.TextBox txtPreview;
        private System.Windows.Forms.Panel panelStatus;
        private System.Windows.Forms.PictureBox pbStatus;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Button btnDisconnect;
    }
}
