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
        private const int Port = 5050;
        private readonly ConnectionLogger _logger = new ConnectionLogger("viewer.log");

        public ViewerForm()
        {
            InitializeComponent();
            try
            {
                AppendLog("Detecting local IPv4...");
                var localIp = NetworkHelper.GetLocalIPv4().ToString();
                AppendLog("Local IP: " + localIp);
                // ensure disconnect button starts as Quit
                btnDisconnect.Text = "Quit";
            }
            catch (Exception ex)
            {
                AppendLog("Initialization error: " + ex.Message);
            }
        }

        private async void btnConnect_Click(object sender, EventArgs e)
        {
            btnConnect.Enabled = false;
            try
            {
                var input = txtAccessCode.Text.Trim().ToUpperInvariant();

                AppendLog("Parsing access code / target input: " + input);

                // Try decode as access code first
                var accessService = new AccessCodeService();
                if (accessService.TryDecode(input, out var lastOctet, out var sessionToken))
                {
                    AppendLog($"Decoded access code -> last octet: {lastOctet}, token: {sessionToken}");

                    // Resolve IP using local network prefix
                    var local = NetworkHelper.GetLocalIPv4();
                    var parts = local.GetAddressBytes();
                    if (parts.Length == 4)
                    {
                        var targetIp = new IPAddress(new byte[] { parts[0], parts[1], parts[2], lastOctet });
                        AppendLog("Resolved target IP: " + targetIp);

                        await ConnectToIpAsync(targetIp);
                    }
                    else
                    {
                        AppendLog("Local IPv4 invalid; cannot resolve target IP.");
                        MessageBox.Show("Local IPv4 invalid; cannot resolve target IP.");
                    }
                }
                else if (IPAddress.TryParse(input, out var explicitIp))
                {
                    AppendLog("Input is a direct IP: " + explicitIp);
                    await ConnectToIpAsync(explicitIp);
                }
                else
                {
                    AppendLog("Invalid input. Provide a 6-char access code or an IP address.");
                    MessageBox.Show("Invalid input. Provide a 6-char access code or an IP address.");
                }
            }
            catch (Exception ex)
            {
                AppendLog("Error: " + ex.Message);
                MessageBox.Show("Error: " + ex.Message);
            }
            finally
            {
                btnConnect.Enabled = true;
            }
        }

        private void btnDisconnect_Click(object? sender, EventArgs e)
        {
            // when not connected this acts as Quit
            AppendLog("Quit requested from Disconnect/Quit button.");
            Application.Exit();
        }

        private async Task ConnectToIpAsync(IPAddress ip)
        {
            AppendLog("Connecting to " + ip + ":" + Port);
            btnDisconnect.Text = "Disconn";
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var connectTask = socket.ConnectAsync(ip, Port);
            var timeout = Task.Delay(5000);
            var completed = await Task.WhenAny(connectTask, timeout);
            if (completed == timeout)
            {
                AppendLog("Connect timeout to " + ip);
                MessageBox.Show("Connect timeout");
                return;
            }

            AppendLog("Connected to " + ip);
            btnDisconnect.Text = "Disconn";
            using var peer = new TcpPeer(socket, _logger);
            await peer.SendAsync("HELLO");
            AppendLog("Sent HELLO");
            var reply = await peer.ReceiveAsync();
            AppendLog("Received: " + reply);
            MessageBox.Show("Reply: " + reply);
            // restore button
            btnDisconnect.Text = "Disconn";
        }

        private void AppendLog(string message)
        {
            try
            {
                _logger.Log(message);
                if (txtLog.InvokeRequired)
                {
                    txtLog.Invoke(() =>
                    {
                        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                    });
                }
                else
                {
                    txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // ignore UI logging errors
            }
        }
    }
}
