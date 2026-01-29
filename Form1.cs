using Velopack;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreenDash
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private async void btnCheckUpdates_Click(object sender, EventArgs e)
        {
            BtnCheckUpdates.Enabled = false;
            BtnCheckUpdates.Text = "Verificando...";

            var manager = new UpdateManager("https://seu-servidor/releases");

            var update = await manager.CheckForUpdatesAsync();

            if (update == null)
            {
                MessageBox.Show("No updates available.");
                return;
            }

            await manager.DownloadUpdatesAsync(update);

            // AQUI está a correção
            manager.ApplyUpdatesAndRestart(update);

            BtnCheckUpdates.Enabled = true;
            BtnCheckUpdates.Text = "Buscar atualizações";
        }

        
    }
}
