namespace ScreenDash
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            btnCheckUpdates = new Button();
            btnOpenSupportShare = new Button();
            btnOpenRemoteAssist = new Button();
            SuspendLayout();
            // 
            // BtnCheckUpdates
            // 
            btnCheckUpdates.Location = new Point(181, 91);
            btnCheckUpdates.Name = "BtnCheckUpdates";
            btnCheckUpdates.Size = new Size(198, 29);
            btnCheckUpdates.TabIndex = 0;
            btnCheckUpdates.Text = "Atualizar";
            btnCheckUpdates.UseVisualStyleBackColor = true;
            btnCheckUpdates.Click += btnCheckUpdates_Click;
            // 
            // btnOpenSupportShare
            // 
            btnOpenSupportShare.Location = new Point(181, 140);
            btnOpenSupportShare.Name = "btnOpenSupportShare";
            btnOpenSupportShare.Size = new Size(198, 29);
            btnOpenSupportShare.TabIndex = 1;
            btnOpenSupportShare.Text = "Open Support Share";
            btnOpenSupportShare.UseVisualStyleBackColor = true;
            btnOpenSupportShare.Click += btnOpenSupportShare_Click;
            // 
            // btnOpenRemoteAssist
            // 
            btnOpenRemoteAssist.Location = new Point(181, 190);
            btnOpenRemoteAssist.Name = "btnOpenRemoteAssist";
            btnOpenRemoteAssist.Size = new Size(198, 29);
            btnOpenRemoteAssist.TabIndex = 2;
            btnOpenRemoteAssist.Text = "Open Remote Assist";
            btnOpenRemoteAssist.UseVisualStyleBackColor = true;
            btnOpenRemoteAssist.Click += btnOpenRemoteAssist_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(btnOpenRemoteAssist);
            Controls.Add(btnOpenSupportShare);
            Controls.Add(btnCheckUpdates);
            Name = "Form1";
            Text = "Form1";
            ResumeLayout(false);
        }

        #endregion

        private Button btnCheckUpdates;
        private Button btnOpenSupportShare;
        private Button btnOpenRemoteAssist;
    }
}
