using Velopack;
using Velopack.Sources;
using System.Net;
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
        private void btnOpenSupportShare_Click(object? sender, EventArgs e)
        {
            using var f = new SupportShareForm();
            f.ShowDialog(this);
        }

        private void btnOpenRemoteAssist_Click(object? sender, EventArgs e)
        {
            using var f = new RemoteAssistForm();
            f.ShowDialog(this);
        }

        private async void btnCheckUpdates_Click(object sender, EventArgs e)
        {
            btnCheckUpdates.Enabled = false;
            btnCheckUpdates.Text = "Verificando...";

            try
            {
                var source = new GithubSource("https://github.com/falenando/ScreenDash", accessToken: null, prerelease: false);

                var manager = new UpdateManager(source);

                var update = await manager.CheckForUpdatesAsync();

                if (update == null)
                {
                    MessageBox.Show("Nenhuma atualiza��o dispon�vel.");
                    return;
                }

                await manager.DownloadUpdatesAsync(update);
                manager.ApplyUpdatesAndRestart(update);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Erro ao verificar atualiza��es:\n\n" + ex.Message
                );
            }
            finally
            {
                btnCheckUpdates.Enabled = true;
                btnCheckUpdates.Text = "Buscar atualiza��es";
            }
        }        
    }
}
