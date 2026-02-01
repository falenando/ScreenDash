using RemoteCore;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using RemoteCore.Implementations;

namespace HostApp
{
    public partial class HostForm : Form
    {
        private readonly int Port = RemoteCore.Config.GetPortFromFile("hostconfig.json", 5050);
        private readonly ConnectionLogger _logger;
        private readonly RemoteCore.Interfaces.IScreenCapturer _capturer;
        private readonly RemoteCore.Interfaces.IFrameEncoder _encoder;
        private TcpNetworkListener? _listenerImpl;
        private System.Threading.CancellationTokenSource? _cts;
        private Task? _listenTask;
        private volatile bool _isListening = false;
        private volatile bool _connectionActive = false;
        private Socket? _currentSocket;
        private ushort _sessionToken;
        private readonly AccessCodeService _codeService = new AccessCodeService();

        public HostForm()
            : this(new RemoteCore.Implementations.ScreenCapturerDesktopDuplication(), new RemoteCore.Implementations.JpegFrameEncoder(50, 1024), new RemoteCore.ConnectionLogger("host.log"))
        {
        }

        // DI constructor
        public HostForm(RemoteCore.Interfaces.IScreenCapturer capturer, RemoteCore.Interfaces.IFrameEncoder encoder, RemoteCore.ConnectionLogger logger)
        {
            _capturer = capturer ?? throw new ArgumentNullException(nameof(capturer));
            _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            InitializeComponent();
            // wire quit button
            btnQuit.Click += btnQuit_Click;
            btnCopyCode.Click += btnCopyCode_Click;

            // generate session token and access code, logging each step to UI and file
            try
            {
                AppendLog("Detecting local IPv4...");
                _sessionToken = GenerateSessionToken();
                var ip = NetworkHelper.GetLocalIPv4();

                AppendLog("Generating access code...");
                var code = _codeService.GenerateCode(ip, _sessionToken);
                txtTokenRemoto.Text = code;
                AppendLog("Access code: " + code);

                AppendLog($"Starting listener on port {Port}...");
                // start listening automatically
                StartListening();
                btnStart.Text = "Stop sharing";
                // ensure we stop listener when form closes
                this.FormClosing += HostForm_FormClosing;
            }
            catch (Exception ex)
            {
                AppendLog("Initialization error: " + ex.Message);
            }
        }

        private void UpdateStatus(string text, System.Drawing.Color color)
        {
            try
            {
                if (pbStatus.InvokeRequired)
                {
                    pbStatus.Invoke(() =>
                    {
                        pbStatus.BackColor = color;
                        lblStatus.Text = text;
                    });
                }
                else
                {
                    pbStatus.BackColor = color;
                    lblStatus.Text = text;
                }
            }
            catch
            {
                // ignore
            }
        }

        private void StartListening()
        {
            // start the listener implementation in background
            _cts = new System.Threading.CancellationTokenSource();
            try
            {
                _listenerImpl = new TcpNetworkListener(Port);
                _listenerImpl.ConnectionAccepted += socket => _ = Task.Run(() => HandleIncomingSocket(socket));
                _listenTask = _listenerImpl.StartAsync(_cts.Token);
                _isListening = true;
                AppendLog("Listening on port " + Port);
                UpdateStatus("Waiting for connection", System.Drawing.Color.Orange);
            }
            catch (Exception ex)
            {
                AppendLog("Failed to start listener: " + ex.Message);
                _isListening = false;
            }
        }
        private async Task HandleIncomingSocket(Socket socket)
        {
            _currentSocket = socket;

            // enforce only one active connection at a time
            if (_connectionActive)
            {
                AppendLog("Rejecting incoming connection because another session is active: " + socket.RemoteEndPoint);
                try { socket.Close(); } catch { }
                return;
            }

            // accept only connections from same /24 network as local
            try
            {
                var remoteEp = socket.RemoteEndPoint as IPEndPoint;
                if (remoteEp != null)
                {
                    var localIp = NetworkHelper.GetLocalIPv4();
                    var localBytes = localIp.GetAddressBytes();
                    var remoteBytes = remoteEp.Address.GetAddressBytes();
                    if (localBytes.Length == 4 && remoteBytes.Length == 4)
                    {
                        // compare first 3 octets (same /24)
                        if (!(localBytes[0] == remoteBytes[0] && localBytes[1] == remoteBytes[1] && localBytes[2] == remoteBytes[2]))
                        {
                            AppendLog("Rejected connection from different network: " + remoteEp.Address);
                            try { socket.Close(); } catch { }
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("Error while checking remote endpoint: " + ex.Message);
                try { socket.Close(); } catch { }
                return;
            }

            AppendLog("Accepted connection from " + socket.RemoteEndPoint);
            _connectionActive = true;
            UpdateStatus("Connected: " + socket.RemoteEndPoint, System.Drawing.Color.LimeGreen);

            try
            {
                var streamer = new RemoteCore.Implementations.FrameStreamer(_capturer, _encoder, _logger);
                await streamer.StreamToAsync(socket, _cts?.Token ?? System.Threading.CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppendLog("Connection handling error: " + ex.Message);
            }
            finally
            {
                _connectionActive = false;
                try { _currentSocket?.Close(); } catch { }
                _currentSocket = null;
                UpdateStatus("Waiting for connection", System.Drawing.Color.Orange);
            }
        }

        private void HostForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            AppendLog("Form closing - stopping listener...");
            try
            {
                _cts?.Cancel();
            }
            catch { }

            try
            {
                if (_listenerImpl != null)
                {
                    try { _listenerImpl.StopAsync().GetAwaiter().GetResult(); } catch { }
                    try { _listenerImpl.Dispose(); } catch { }
                    _listenerImpl = null;
                }
            }
            catch { }

            if (_listenTask != null)
            {
                try
                {
                    // wait up to 2s for clean shutdown
                    _listenTask.Wait(2000);
                }
                catch { }
            }
        }

        private void btnQuit_Click(object? sender, EventArgs e)
        {
            AppendLog("Quit requested by user.");
            Application.Exit();
        }

        private void btnCopyCode_Click(object? sender, EventArgs e)
        {
            try
            {
                var code = txtTokenRemoto.Text ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(code))
                {
                    Clipboard.SetText(code);
                    AppendLog("Copied access code to clipboard.");
                }
                else
                {
                    AppendLog("No access code to copy.");
                }
            }
            catch (Exception ex)
            {
                AppendLog("Copy failed: " + ex.Message);
            }
        }

        private static ushort GenerateSessionToken()
        {
            var bytes = new byte[2];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return (ushort)((bytes[0] << 8) | bytes[1]);
        }

        private async void btnStart_Click(object sender, EventArgs e)
        {
            // start listening manually if user clicks Start
            btnStart.Enabled = false;
            try
            {
                if (_isListening)
                {
                    // Confirm before stopping an active connection
                    if (_connectionActive)
                    {
                        var result = MessageBox.Show("An active connection will be terminated. Do you want to continue?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (result != DialogResult.Yes)
                        {
                            return;
                        }

                        // send BYE message if connection is active
                        try
                        {
                            using (var socket = _currentSocket)
                            {
                                if (socket != null)
                                {
                                    using var peer = new TcpPeer(socket, _logger);
                                    await peer.SendAsync("BYE");
                                    AppendLog("Sent BYE to remote peer.");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppendLog("Error sending BYE message: " + ex.Message);
                        }
                    }

                    // stop listener and close any active connection
                    try { _cts?.Cancel(); } catch { }
                    try { if (_listenerImpl != null) { _listenerImpl.StopAsync().GetAwaiter().GetResult(); _listenerImpl.Dispose(); _listenerImpl = null; } } catch { }
                    try { _currentSocket?.Close(); } catch { }
                    _isListening = false;
                    _connectionActive = false;
                    UpdateStatus("Not ready to connect", System.Drawing.Color.Red);
                    btnStart.Text = "Start sharing";
                    AppendLog("Listener stopped.");
                }
                else
                {
                    // generate new session token and code
                    _sessionToken = GenerateSessionToken();
                    var ip = NetworkHelper.GetLocalIPv4();
                    var code = _codeService.GenerateCode(ip, _sessionToken);
                    txtTokenRemoto.Text = code;
                    AppendLog("Generated new access code: " + code);

                    StartListening();
                    btnStart.Text = "Stop sharing";
                }
            }
            catch (Exception ex)
            {
                AppendLog("Error starting listener: " + ex.Message);
                MessageBox.Show("Error: " + ex.Message);
            }
            finally
            {
                btnStart.Enabled = true;
            }
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
                // ignore logging UI errors
            }
        }

        private void txtLocalIp_TextChanged(object sender, EventArgs e)
        {

        }
    }
}
