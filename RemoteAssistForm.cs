using System;
using System.Windows.Forms;

namespace ScreenDash
{
    public partial class RemoteAssistForm : Form
    {
        public RemoteAssistForm()
        {
            InitializeComponent();
            ApplyLocalization();
        }

        private void ApplyLocalization()
        {
            // Define o texto dos controles usando o LocalizationManager
            this.Text = LocalizationManager.GetString("RemoteAssist_Title");
            lblTitle.Text = LocalizationManager.GetString("RemoteAssist_Title");
            lblAccessCode.Text = LocalizationManager.GetString("RemoteAssist_EnterAccessCode");
            btnConnect.Text = LocalizationManager.GetString("RemoteAssist_Connect");
            btnDisconnect.Text = LocalizationManager.GetString("RemoteAssist_Disconnect");

            string disconnectedStatus = LocalizationManager.GetString("Status_Disconnected");
            lblStatus.Text = string.Format(LocalizationManager.GetString("StatusFormat"), disconnectedStatus);
        }
    }
}
