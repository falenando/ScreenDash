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
            BtnCheckUpdates = new Button();
            SuspendLayout();
            // 
            // BtnCheckUpdates
            // 
            BtnCheckUpdates.Location = new Point(181, 91);
            BtnCheckUpdates.Name = "BtnCheckUpdates";
            BtnCheckUpdates.Size = new Size(198, 29);
            BtnCheckUpdates.TabIndex = 0;
            BtnCheckUpdates.Text = "Atualizar";
            BtnCheckUpdates.UseVisualStyleBackColor = true;
            BtnCheckUpdates.Click += btnCheckUpdates_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(BtnCheckUpdates);
            Name = "Form1";
            Text = "Form1";
            ResumeLayout(false);
        }

        #endregion

        private Button BtnCheckUpdates;
    }
}
