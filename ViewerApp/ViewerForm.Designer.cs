namespace ViewerApp
{
    partial class ViewerForm
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
            btnConnect = new Button();
            txtLog = new TextBox();
            txtAccessCode = new TextBox();
            lblAccessCode = new Label();
            btnDisconnect = new Button();
            panelStatus = new Panel();
            pbStatus = new PictureBox();
            lblStatus = new Label();
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
            lblTitle.Size = new Size(173, 28);
            lblTitle.TabIndex = 0;
            lblTitle.Text = "Viewer (Support)";
            // 
            // btnConnect
            // 
            btnConnect.Location = new Point(14, 113);
            btnConnect.Name = "btnConnect";
            btnConnect.Size = new Size(303, 30);
            btnConnect.TabIndex = 7;
            btnConnect.Text = "Connect";
            btnConnect.UseVisualStyleBackColor = true;
            btnConnect.Click += btnConnect_Click;
            // 
            // txtLog
            // 
            txtLog.Location = new Point(14, 149);
            txtLog.Multiline = true;
            txtLog.Name = "txtLog";
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Size = new Size(303, 200);
            txtLog.TabIndex = 8;
            // 
            // txtAccessCode
            // 
            txtAccessCode.Font = new Font("Consolas", 18F);
            txtAccessCode.Location = new Point(14, 64);
            txtAccessCode.Name = "txtAccessCode";
            txtAccessCode.Size = new Size(303, 43);
            txtAccessCode.TabIndex = 10;
            // 
            // lblAccessCode
            // 
            lblAccessCode.AutoSize = true;
            lblAccessCode.Location = new Point(14, 41);
            lblAccessCode.Name = "lblAccessCode";
            lblAccessCode.Size = new Size(93, 20);
            lblAccessCode.TabIndex = 9;
            lblAccessCode.Text = "Access code:";
            // 
            // btnDisconnect
            // 
            btnDisconnect.Location = new Point(248, 365);
            btnDisconnect.Name = "btnDisconnect";
            btnDisconnect.Size = new Size(70, 30);
            btnDisconnect.TabIndex = 12;
            btnDisconnect.Text = "Disconnect";
            btnDisconnect.UseVisualStyleBackColor = true;
            // 
            // panelStatus
            // 
            panelStatus.Controls.Add(pbStatus);
            panelStatus.Controls.Add(lblStatus);
            panelStatus.Location = new Point(12, 365);
            panelStatus.Name = "panelStatus";
            panelStatus.Size = new Size(220, 40);
            panelStatus.TabIndex = 11;
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
            // ViewerForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(331, 414);
            Controls.Add(btnDisconnect);
            Controls.Add(panelStatus);
            Controls.Add(txtAccessCode);
            Controls.Add(lblAccessCode);
            Controls.Add(txtLog);
            Controls.Add(btnConnect);
            Controls.Add(lblTitle);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Name = "ViewerForm";
            Text = "Viewer";
            panelStatus.ResumeLayout(false);
            panelStatus.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pbStatus).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Button btnConnect;
        private System.Windows.Forms.TextBox txtLog;
        private TextBox txtAccessCode;
        private Label lblAccessCode;
        private Button btnDisconnect;
        private Panel panelStatus;
        private PictureBox pbStatus;
        private Label lblStatus;
    }
}
