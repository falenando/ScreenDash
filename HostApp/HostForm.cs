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
        private const int Port = 5050;
        private readonly ConnectionLogger _logger = new ConnectionLogger("host.log");
        private TcpListener? _listener;
        private System.Threading.CancellationTokenSource? _cts;
        private Task? _listenTask;
        private volatile bool _isListening = false;
        private volatile bool _connectionActive = false;
        private Socket? _currentSocket;
        private ushort _sessionToken;
        private readonly AccessCodeService _codeService = new AccessCodeService();

        public HostForm()
        {
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
                AppendLog("Local IP: " + ip);

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
            // start the listener in background
            _cts = new System.Threading.CancellationTokenSource();
            _listenTask = Task.Run(async () => await ListenLoopAsync(_cts.Token));
        }

        private async Task ListenLoopAsync(System.Threading.CancellationToken ct)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, Port);
                _listener.Start();
                _isListening = true;
                AppendLog("Listening on port " + Port);
                UpdateStatus("Waiting for connection", System.Drawing.Color.Orange);

                while (!ct.IsCancellationRequested)
                {
                    Socket? socket = null;
                    try
                    {
                        socket = await _listener.AcceptSocketAsync();
                    }
                    catch (Exception ex)
                    {
                        // likely listener stopped
                        AppendLog("Listener stopped or error: " + ex.Message);
                        break;
                    }

                    if (socket == null)
                        continue;

                    _currentSocket = socket;

                    // enforce only one active connection at a time
                    if (_connectionActive)
                    {
                        AppendLog("Rejecting incoming connection because another session is active: " + socket.RemoteEndPoint);
                        try { socket.Close(); } catch { }
                        continue;
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
                                    continue;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendLog("Error while checking remote endpoint: " + ex.Message);
                        try { socket.Close(); } catch { }
                        continue;
                    }

                    AppendLog("Accepted connection from " + socket.RemoteEndPoint);
                    _connectionActive = true;
                    UpdateStatus("Connected: " + socket.RemoteEndPoint, System.Drawing.Color.LimeGreen);

                    try
                    {
                        using var peer = new TcpPeer(socket, _logger);
                        var msg = await peer.ReceiveAsync();
                        AppendLog("Received: " + msg);

                        // if client asked for screen stream start loop: send frames until disconnected
                        if (msg == "REQUEST_STREAM")
                        {
                            AppendLog("Starting screen stream to " + socket.RemoteEndPoint);
                            // send frames periodically
                            for (int i = 0; i < 500 && socket.Connected; i++)
                            {
                                var jpg = ScreenStreamer.CaptureJpegBytes(quality: 50, maxWidth: 1024);
                                // send length header as 8-digit ASCII
                                var header = System.Text.Encoding.ASCII.GetBytes(jpg.Length.ToString("D8"));
                                await peer.SendAsync(System.Text.Encoding.ASCII.GetString(header));
                                // send raw bytes
                                var sent = 0;
                                while (sent < jpg.Length)
                                {
                                    sent += await socket.SendAsync(new ArraySegment<byte>(jpg, sent, jpg.Length - sent), SocketFlags.None);
                                }
                                await Task.Delay(200);
                            }
                            AppendLog("Finished streaming or client disconnected.");
                        }
                        else
                        {
                            await peer.SendAsync("WELCOME");
                            AppendLog("Sent welcome reply.");
                        }
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
                    }

                    // after handling connection go back to waiting
                    UpdateStatus("Waiting for connection", System.Drawing.Color.Orange);
                }
            }
            finally
            {
                try { _listener?.Stop(); } catch { }
                _isListening = false;
                UpdateStatus("Not ready to connect", System.Drawing.Color.Red);
                AppendLog("Listener terminated.");
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
                _listener?.Stop();
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
                    try { _listener?.Stop(); } catch { }
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
