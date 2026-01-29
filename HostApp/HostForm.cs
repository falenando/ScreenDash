using RemoteCore;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HostApp
{
    public partial class HostForm : Form
    {
        private const int Port = 5500;
        private readonly ConnectionLogger _logger = new ConnectionLogger("host.log");
        private TcpListener? _listener;

        public HostForm()
        {
            InitializeComponent();
            txtLocalIp.Text = NetworkHelper.GetLocalIPv4().ToString();
        }

        private async void btnStart_Click(object sender, EventArgs e)
        {
            btnStart.Enabled = false;
            try
            {
                var ip = IPAddress.Any;
                _listener = new TcpListener(ip, Port);
                _listener.Start();
                _logger.Log("Listening on port " + Port);

                var socket = await _listener.AcceptSocketAsync();
                _logger.Log("Accepted connection from " + socket.RemoteEndPoint);

                using var peer = new TcpPeer(socket, _logger);
                var msg = await peer.ReceiveAsync();
                _logger.Log("Received: " + msg);

                await peer.SendAsync("WELCOME");
                _logger.Log("Sent welcome reply.");
            }
            catch (Exception ex)
            {
                _logger.Log("Error: " + ex.Message);
                MessageBox.Show("Error: " + ex.Message);
            }
            finally
            {
                btnStart.Enabled = true;
                try { _listener?.Stop(); } catch { }
            }
        }

        private void txtLocalIp_TextChanged(object sender, EventArgs e)
        {

        }
    }
}
