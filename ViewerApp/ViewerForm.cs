using RemoteCore;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ViewerApp
{
    public partial class ViewerForm : Form
    {
        private const int Port = 5500;
        private readonly ConnectionLogger _logger = new ConnectionLogger("viewer.log");

        public ViewerForm()
        {
            InitializeComponent();
        }

        private async void btnConnect_Click(object sender, EventArgs e)
        {
            btnConnect.Enabled = false;
            try
            {
                if (!IPAddress.TryParse(txtAccessCode.Text.Trim(), out var ip))
                {
                    MessageBox.Show("Invalid IP");
                    return;
                }

                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                var connectTask = socket.ConnectAsync(ip, Port);
                var timeout = Task.Delay(5000);
                var completed = await Task.WhenAny(connectTask, timeout);
                if (completed == timeout)
                {
                    _logger.Log("Connect timeout to " + ip);
                    MessageBox.Show("Connect timeout");
                    return;
                }

                _logger.Log("Connected to " + ip);
                using var peer = new TcpPeer(socket, _logger);
                await peer.SendAsync("HELLO");
                _logger.Log("Sent HELLO");
                var reply = await peer.ReceiveAsync();
                _logger.Log("Received: " + reply);
                MessageBox.Show("Reply: " + reply);
            }
            catch (Exception ex)
            {
                _logger.Log("Error: " + ex.Message);
                MessageBox.Show("Error: " + ex.Message);
            }
            finally
            {
                btnConnect.Enabled = true;
            }
        }
    }
}
