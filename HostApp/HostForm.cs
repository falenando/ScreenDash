using RemoteCore;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using RemoteCore.Implementations;
using System.IO;

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
        private RemoteSupportServiceClient? _serviceClient;
        private bool _useService = false;

        public HostForm()
            : this(new RemoteCore.Implementations.ScreenCapturerDesktopDuplication(), new RemoteCore.Implementations.JpegFrameEncoder(50, 1024), new RemoteCore.ConnectionLogger("host.log"))
        {
        }

        private void TryEnableService()
        {
            _useService = false;
            _serviceClient?.Dispose();
            _serviceClient = RemoteSupportServiceClient.TryConnect(500);
            if (_serviceClient == null)
                return;

            try
            {
                var ok = _serviceClient.HealthCheckAsync().GetAwaiter().GetResult();
                if (ok)
                {
                    _useService = true;
                    AppendLog("RemoteSupport.Service detected. Using service for capture/input.");
                }
                else
                {
                    _serviceClient.Dispose();
                    _serviceClient = null;
                }
            }
            catch
            {
                _serviceClient?.Dispose();
                _serviceClient = null;
            }
        }

        // DI constructor
        public HostForm(RemoteCore.Interfaces.IScreenCapturer capturer, RemoteCore.Interfaces.IFrameEncoder encoder, RemoteCore.ConnectionLogger logger)
        {
            _capturer = capturer ?? throw new ArgumentNullException(nameof(capturer));
            _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            InitializeComponent();
            ApplyLocalization();
            // wire quit button
            btnQuit.Click += btnQuit_Click;
            btnCopyCode.Click += btnCopyCode_Click;

            // generate session token and start listening; access code is set based on local/relay decision
            try
            {
                _sessionToken = GenerateSessionToken();
                AppendLog(string.Format(ScreenDash.Resources.Strings.HostLog_StartingListenerOnPort, Port));
                // start listening automatically
                StartListening();
                btnStart.Text = ScreenDash.Resources.Strings.HostForm_StopSharing;
                // ensure we stop listener when form closes
                this.FormClosing += HostForm_FormClosing;
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(ScreenDash.Resources.Strings.HostLog_InitializationError, ex.Message));
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

        private void StartListening(bool isManualStart = false)
        {
            if (Port <= 0 || Port > 65535)
            {
                var reason = $"Invalid port: {Port}.";
                AppendLog(string.Format(ScreenDash.Resources.Strings.HostLog_ListenerNotStarted, reason));
                UpdateStatus(ScreenDash.Resources.Strings.HostForm_StatusNotReady, System.Drawing.Color.Red);
                _isListening = false;
                return;
            }

            TryEnableService();

            if (!IsTcpPortFree(Port))
            {
                AppendLog($"Port {Port} is already in use. Trying relay server...");
                StartRelayConnection(isManualStart);
                return;
            }

            if (!IsFirewallInboundAllowedForThisAppOrPort(Port))
            {
                var reason = $"Windows Firewall inbound rule not found/enabled for TCP port {Port} (or this app).";
                AppendLog(string.Format(ScreenDash.Resources.Strings.HostLog_ListenerNotStarted, reason));
                AppendLog("Inbound firewall rule is missing; trying relay server...");
                StartRelayConnection(isManualStart);
                return;
            }

            try
            {
                AppendLog(ScreenDash.Resources.Strings.HostLog_DetectingLocalIPv4);
                var ip = NetworkHelper.GetLocalIPv4();
                SetAccessCode(ip, isManualStart);
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(ScreenDash.Resources.Strings.HostLog_InitializationError, ex.Message));
                UpdateStatus(ScreenDash.Resources.Strings.HostForm_StatusNotReady, System.Drawing.Color.Red);
                _isListening = false;
                return;
            }

            // start the listener implementation in background
            _cts = new System.Threading.CancellationTokenSource();
            try
            {
                _listenerImpl = new TcpNetworkListener(Port);
                _listenerImpl.ConnectionAccepted += socket => _ = Task.Run(() => HandleIncomingSocket(socket, skipNetworkCheck: false));
                _listenTask = _listenerImpl.StartAsync(_cts.Token);
                _isListening = true;
                AppendLog(string.Format(ScreenDash.Resources.Strings.HostLog_ListeningOnPort, Port));
                UpdateStatus(ScreenDash.Resources.Strings.HostForm_StatusWaitingForConnection, System.Drawing.Color.Orange);
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(ScreenDash.Resources.Strings.HostLog_FailedToStartListener, ex.Message));
                _isListening = false;
            }
        }

        private void StartRelayConnection(bool isManualStart)
        {
            var relayServer = RemoteCore.Config.GetStringFromFile("hostconfig.json", "RelayServer", string.Empty);
            if (string.IsNullOrWhiteSpace(relayServer))
            {
                AppendLog("Relay server address is not configured in hostconfig.json.");
                UpdateStatus(ScreenDash.Resources.Strings.HostForm_StatusNotReady, System.Drawing.Color.Red);
                _isListening = false;
                return;
            }

            TryEnableService();

            _cts = new System.Threading.CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try
                {
                    var relayIp = await ResolveRelayAddressAsync(relayServer, _cts.Token);
                    AppendLog($"Resolved relay server to {relayIp} for access code.");
                    SetAccessCode(relayIp, isManualStart);
                }
                catch (Exception ex)
                {
                    AppendLog("Relay server address could not be resolved for access code: " + ex.Message);
                }
            });
            _listenTask = Task.Run(() => ConnectToRelayAsync(relayServer, Port, _cts.Token));
            UpdateStatus(ScreenDash.Resources.Strings.HostForm_StatusWaitingForConnection, System.Drawing.Color.Orange);
        }

        private async Task ConnectToRelayAsync(string relayServer, int port, System.Threading.CancellationToken token)
        {
            try
            {
                var relayIp = await ResolveRelayAddressAsync(relayServer, token);
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                var connectTask = socket.ConnectAsync(relayIp, port);
                var timeoutTask = Task.Delay(5000, token);
                var completed = await Task.WhenAny(connectTask, timeoutTask);
                if (completed == timeoutTask)
                {
                    AppendLog($"Relay connection timeout to {relayServer}:{port}.");
                    UpdateStatus(ScreenDash.Resources.Strings.HostForm_StatusNotReady, System.Drawing.Color.Red);
                    _isListening = false;
                    return;
                }

                await connectTask;
                if (token.IsCancellationRequested)
                    return;

                _isListening = true;
                AppendLog($"Connected to relay server {relayServer}:{port}.");

                var handshake = Encoding.ASCII.GetBytes("HOST\n");
                await socket.SendAsync(handshake, SocketFlags.None);

                await HandleIncomingSocket(socket, skipNetworkCheck: true);
            }
            catch (Exception ex)
            {
                AppendLog("Relay connection failed: " + ex.Message);
                UpdateStatus(ScreenDash.Resources.Strings.HostForm_StatusNotReady, System.Drawing.Color.Red);
                _isListening = false;
            }
        }

        private static async Task<IPAddress> ResolveRelayAddressAsync(string relayServer, System.Threading.CancellationToken token)
        {
            if (IPAddress.TryParse(relayServer, out var ip))
                return ip;

            var addresses = await Dns.GetHostAddressesAsync(relayServer, token);
            foreach (var addr in addresses)
            {
                if (addr.AddressFamily == AddressFamily.InterNetwork)
                    return addr;
            }

            throw new InvalidOperationException("Relay server address could not be resolved.");
        }

        private bool CanStartListenerWithoutPrompt(int port, out string reason)
        {
            reason = string.Empty;

            if (port <= 0 || port > 65535)
            {
                reason = $"Invalid port: {port}.";
                return false;
            }

            if (!IsTcpPortFree(port))
            {
                reason = $"Port {port} is already in use.";
                return false;
            }

            // Avoid triggering Windows Firewall prompt/UAC for restricted users:
            // only start listening if firewall already allows inbound traffic for this app/port.
            if (!IsFirewallInboundAllowedForThisAppOrPort(port))
            {
                reason = $"Windows Firewall inbound rule not found/enabled for TCP port {port} (or this app). Ask an administrator to allow it, then try again.";
                return false;
            }

            return true;
        }

        private static bool IsTcpPortFree(int port)
        {
            Socket? probe = null;
            try
            {
                probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                probe.Bind(new IPEndPoint(IPAddress.Any, port));
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            catch
            {
                // Conservative: if we cannot determine, do not start.
                return false;
            }
            finally
            {
                try { probe?.Dispose(); } catch { }
            }
        }

        private static bool IsFirewallInboundAllowedForThisAppOrPort(int port)
        {
            // Prefer a safe, no-prompt approach: inspect existing firewall rules.
            // If we cannot read firewall policy (e.g., access denied), treat as not allowed
            // to ensure we don't start listening and trigger prompts.
            try
            {
                var exePath = GetCurrentExePath();
                return FirewallRuleAllows(exePath, port);
            }
            catch
            {
                return false;
            }
        }

        private static string GetCurrentExePath()
        {
            // Application.ExecutablePath can be null in some hosting scenarios; fall back to process main module.
            try
            {
                var p = Application.ExecutablePath;
                if (!string.IsNullOrWhiteSpace(p))
                    return Path.GetFullPath(p);
            }
            catch { }

            try
            {
                var p = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(p))
                    return Path.GetFullPath(p);
            }
            catch { }

            return string.Empty;
        }

        private static bool FirewallRuleAllows(string exePath, int port)
        {
            // Uses COM API (HNetCfg.FwPolicy2) available on Windows.
            // Do not add any rule here—only detect an existing enabled allow rule.
            var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (policyType == null)
                return false;

            dynamic policy = Activator.CreateInstance(policyType)!;
            dynamic rules = policy.Rules;

            // Constants from NetFwTypeLib
            const int NET_FW_RULE_DIR_IN = 1;
            const int NET_FW_ACTION_ALLOW = 1;
            const int NET_FW_IP_PROTOCOL_TCP = 6;

            var exeFull = string.IsNullOrWhiteSpace(exePath) ? string.Empty : Path.GetFullPath(exePath);

            foreach (dynamic rule in rules)
            {
                try
                {
                    if ((int)rule.Direction != NET_FW_RULE_DIR_IN)
                        continue;
                    if (!(bool)rule.Enabled)
                        continue;
                    if ((int)rule.Action != NET_FW_ACTION_ALLOW)
                        continue;

                    // Some rules have protocol = ANY (256). Only accept TCP or ANY.
                    var proto = (int)rule.Protocol;
                    if (proto != NET_FW_IP_PROTOCOL_TCP && proto != 256)
                        continue;

                    // Accept if rule matches this application, or explicitly opens the port.
                    var ruleApp = string.Empty;
                    try { ruleApp = (string)rule.ApplicationName; } catch { }

                    if (!string.IsNullOrWhiteSpace(ruleApp) && !string.IsNullOrWhiteSpace(exeFull))
                    {
                        var ruleAppFull = Path.GetFullPath(ruleApp);
                        if (string.Equals(ruleAppFull, exeFull, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }

                    var localPorts = string.Empty;
                    try { localPorts = (string)rule.LocalPorts; } catch { }
                    if (PortListContains(localPorts, port))
                        return true;
                }
                catch
                {
                    // ignore bad rules
                }
            }

            return false;
        }

        private static bool PortListContains(string? ports, int port)
        {
            if (string.IsNullOrWhiteSpace(ports))
                return false;

            // Examples: "5050", "5050-5060", "80,443,5050", "Any"
            if (string.Equals(ports.Trim(), "Any", StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var raw in ports.Split(','))
            {
                var part = raw.Trim();
                if (part.Length == 0)
                    continue;

                var dash = part.IndexOf('-');
                if (dash >= 0)
                {
                    var startStr = part.Substring(0, dash).Trim();
                    var endStr = part.Substring(dash + 1).Trim();
                    if (int.TryParse(startStr, out var start) && int.TryParse(endStr, out var end))
                    {
                        if (port >= start && port <= end)
                            return true;
                    }

                    continue;
                }

                if (int.TryParse(part, out var single) && single == port)
                    return true;
            }

            return false;
        }
        private async Task HandleIncomingSocket(Socket socket, bool skipNetworkCheck)
        {
            _currentSocket = socket;

            // enforce only one active connection at a time
            if (_connectionActive)
            {
                AppendLog(string.Format(ScreenDash.Resources.Strings.HostLog_RejectingIncomingConnectionAnotherSession, socket.RemoteEndPoint));
                try { socket.Close(); } catch { }
                return;
            }

            // accept only connections from same /24 network as local
            if (!skipNetworkCheck)
            {
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
                                AppendLog(string.Format(ScreenDash.Resources.Strings.HostLog_RejectedDifferentNetwork, remoteEp.Address));
                                try { socket.Close(); } catch { }
                                return;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppendLog(string.Format(ScreenDash.Resources.Strings.HostLog_ErrorCheckingRemoteEndpoint, ex.Message));
                    try { socket.Close(); } catch { }
                    return;
                }
            }

            AppendLog(string.Format(ScreenDash.Resources.Strings.HostLog_AcceptedConnectionFrom, socket.RemoteEndPoint));
            _connectionActive = true;
            UpdateStatus(string.Format(ScreenDash.Resources.Strings.HostForm_StatusConnected, socket.RemoteEndPoint), System.Drawing.Color.LimeGreen);
            UpdateRemoteControlIndicator();

            try
            {
                var streamToken = _cts?.Token ?? System.Threading.CancellationToken.None;

                if (skipNetworkCheck)
                {
                    // Relay path: start streaming immediately (handshake already forwarded via relay initial payload)
                    AppendLog("Relay connection: starting stream without local handshake wait.");
                    var inputTask = Task.Run(() => ReceiveInputLoopAsync(socket, streamToken, null), streamToken);

                    if (_useService && _serviceClient != null)
                    {
                        await _serviceClient.StartSessionAsync(txtTokenRemoto.Text);
                        await StreamFromServiceAsync(socket, streamToken);
                    }
                    else
                    {
                        var streamer = new RemoteCore.Implementations.FrameStreamer(_capturer, _encoder, _logger);
                        await streamer.StreamToAsync(socket, streamToken, skipHandshake: true);
                    }

                    await inputTask;
                }
                else
                {
                    var handshakeTcs = new TaskCompletionSource<bool>();

                    // Start input loop which will handle the handshake first
                    var inputTask = Task.Run(() => ReceiveInputLoopAsync(socket, streamToken, handshakeTcs), streamToken);

                    // Wait for the REQUEST_STREAM handshake signal from input loop
                    var handshakeTimeout = Task.Delay(10000, streamToken);
                    var completedTask = await Task.WhenAny(handshakeTcs.Task, handshakeTimeout);

                    if (completedTask == handshakeTcs.Task && handshakeTcs.Task.Result)
                    {
                        AppendLog("Stream requested by remote peer.");
                        if (_useService && _serviceClient != null)
                        {
                            await _serviceClient.StartSessionAsync(txtTokenRemoto.Text);
                            await StreamFromServiceAsync(socket, streamToken);
                        }
                        else
                        {
                            var streamer = new RemoteCore.Implementations.FrameStreamer(_capturer, _encoder, _logger);
                            await streamer.StreamToAsync(socket, streamToken, skipHandshake: true);
                        }
                    }
                    else
                    {
                        AppendLog("Handshake timeout or failed. Closing connection.");
                        try { socket.Close(); } catch { }
                    }

                    await inputTask;
                }
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(ScreenDash.Resources.Strings.HostLog_ConnectionHandlingError, ex.Message));
            }
            finally
            {
                if (_useService && _serviceClient != null)
                {
                    try { await _serviceClient.StopSessionAsync(); } catch { }
                }
                _connectionActive = false;
                try { _currentSocket?.Close(); } catch { }
                _currentSocket = null;
                UpdateStatus(ScreenDash.Resources.Strings.HostForm_StatusWaitingForConnection, System.Drawing.Color.Orange);
                UpdateRemoteControlIndicator();
            }
        }

        private async Task StreamFromServiceAsync(Socket socket, System.Threading.CancellationToken token)
        {
            while (socket.Connected && !token.IsCancellationRequested)
            {
                var frame = await _serviceClient!.RequestFrameAsync();
                if (frame == null || frame.Length == 0)
                    break;

                var header = Encoding.ASCII.GetBytes(frame.Length.ToString("D8"));
                await socket.SendAsync(header, SocketFlags.None);
                var sent = 0;
                while (sent < frame.Length)
                {
                    sent += await socket.SendAsync(new ArraySegment<byte>(frame, sent, frame.Length - sent), SocketFlags.None);
                }

                await Task.Delay(200, token).ContinueWith(_ => { });
            }
        }

        private void HostForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            AppendLog(ScreenDash.Resources.Strings.HostLog_FormClosingStoppingListener);
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
            AppendLog(ScreenDash.Resources.Strings.HostLog_QuitRequested);
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

        private void SetAccessCode(IPAddress ip, bool isManualStart)
        {
            AppendLog(ScreenDash.Resources.Strings.HostLog_GeneratingAccessCode);
            var code = _codeService.GenerateCode(ip, _sessionToken);
            var logFormat = isManualStart
                ? ScreenDash.Resources.Strings.HostLog_GeneratedNewAccessCode
                : ScreenDash.Resources.Strings.HostLog_AccessCode;

            if (txtTokenRemoto.InvokeRequired)
            {
                txtTokenRemoto.Invoke(() => txtTokenRemoto.Text = code);
            }
            else
            {
                txtTokenRemoto.Text = code;
            }

            AppendLog(string.Format(logFormat, code));
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
                        var result = MessageBox.Show(ScreenDash.Resources.Strings.HostMsg_ActiveConnectionWillBeTerminated, ScreenDash.Resources.Strings.Common_Confirm, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
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
                                    AppendLog(ScreenDash.Resources.Strings.HostLog_SentByeToRemotePeer);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppendLog(string.Format(ScreenDash.Resources.Strings.HostLog_ErrorSendingByeMessage, ex.Message));
                        }
                    }

                    // stop listener and close any active connection
                    try { _cts?.Cancel(); } catch { }
                    try { if (_listenerImpl != null) { _listenerImpl.StopAsync().GetAwaiter().GetResult(); _listenerImpl.Dispose(); _listenerImpl = null; } } catch { }
                    try { _currentSocket?.Close(); } catch { }
                    _isListening = false;
                    _connectionActive = false;
                    UpdateStatus(ScreenDash.Resources.Strings.HostForm_StatusNotReady, System.Drawing.Color.Red);
                    btnStart.Text = ScreenDash.Resources.Strings.HostForm_StartSharing;
                    AppendLog(ScreenDash.Resources.Strings.HostLog_ListenerStopped);
                }
                else
                {
                    // generate new session token and start listening
                    _sessionToken = GenerateSessionToken();
                    StartListening(isManualStart: true);
                    btnStart.Text = ScreenDash.Resources.Strings.HostForm_StopSharing;
                }
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(ScreenDash.Resources.Strings.HostLog_ErrorStartingListener, ex.Message));
                MessageBox.Show(string.Format(ScreenDash.Resources.Strings.Common_ErrorWithMessage, ex.Message));
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

        private void ApplyLocalization()
        {
            this.Text = ScreenDash.Resources.Strings.HostForm_Title;
            lblTitle.Text = ScreenDash.Resources.Strings.HostForm_Title;
            lblYourAccess.Text = ScreenDash.Resources.Strings.HostForm_YourAccessCode;
            btnCopyCode.Text = ScreenDash.Resources.Strings.HostForm_CopyCode;
            // btnStart text is managed dynamically, so we can ignore it here or set an initial state
            // btnStart.Text = ScreenDash.Resources.Strings.HostForm_StartSharing; 
            chkAllowRemoteInput.Text = ScreenDash.Resources.Strings.HostForm_AllowRemoteInput;
            // The status label is also dynamic
            // lblStatus.Text = ScreenDash.Resources.Strings.HostForm_StatusReady;
            btnQuit.Text = ScreenDash.Resources.Strings.HostForm_Quit;
            UpdateRemoteControlIndicator();
        }

        private void UpdateRemoteControlIndicator()
        {
            var isActive = _connectionActive && IsRemoteInputAllowed();
            var text = isActive ? ScreenDash.Resources.Strings.HostForm_RemoteControlActive
                : ScreenDash.Resources.Strings.HostForm_RemoteControlInactive;
            var color = isActive ? System.Drawing.Color.LimeGreen : System.Drawing.Color.Gray;

            if (lblRemoteControlStatus.InvokeRequired)
            {
                lblRemoteControlStatus.Invoke(() =>
                {
                    lblRemoteControlStatus.Text = text;
                    lblRemoteControlStatus.ForeColor = color;
                });
                return;
            }

            lblRemoteControlStatus.Text = text;
            lblRemoteControlStatus.ForeColor = color;
        }

        private async Task ReceiveInputLoopAsync(Socket socket, System.Threading.CancellationToken token, TaskCompletionSource<bool>? handshakeTcs)
        {
            var buffer = new byte[4096];
            var sb = new StringBuilder();

            try
            {
                while (socket.Connected && !token.IsCancellationRequested)
                {
                    var read = await socket.ReceiveAsync(buffer, SocketFlags.None);
                    if (read <= 0)
                        break;

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                    while (true)
                    {
                        var text = sb.ToString();
                        var newlineIndex = text.IndexOf('\n');
                        if (newlineIndex < 0)
                            break;

                        var line = text.Substring(0, newlineIndex).Trim();
                        sb.Remove(0, newlineIndex + 1);
                        if (!string.IsNullOrEmpty(line))
                        {
                            if (handshakeTcs != null && line.Equals("REQUEST_STREAM", StringComparison.OrdinalIgnoreCase))
                            {
                                handshakeTcs.TrySetResult(true);
                                continue;
                            }

                            if (_useService && _serviceClient != null)
                            {
                                if (IsRemoteInputAllowed() && line.StartsWith("INPUT|", StringComparison.OrdinalIgnoreCase))
                                    await _serviceClient.SendInputAsync(line);
                            }
                            else
                            {
                                HandleInputCommand(line);
                            }
                        }
                    }
                }
            }
            catch
            {
                handshakeTcs?.TrySetResult(false);
                // ignore input loop errors
            }
            finally
            {
                handshakeTcs?.TrySetResult(false);
            }
        }

        private void HandleInputCommand(string line)
        {
            if (!IsRemoteInputAllowed())
                return;

            if (!line.StartsWith("INPUT|", StringComparison.OrdinalIgnoreCase))
                return;

            var parts = line.Split('|');
            if (parts.Length < 2)
                return;

            switch (parts[1])
            {
                case "MM":
                    if (TryParsePoint(parts, 2, out var mx, out var my))
                        MoveMouseToNormalized(mx, my);
                    break;
                case "MD":
                    if (parts.Length >= 5 && TryParsePoint(parts, 3, out var mdx, out var mdy))
                        SendMouseButton(parts[2], true, mdx, mdy);
                    break;
                case "MU":
                    if (parts.Length >= 5 && TryParsePoint(parts, 3, out var mux, out var muy))
                        SendMouseButton(parts[2], false, mux, muy);
                    break;
                case "MW":
                    if (parts.Length >= 5 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var delta) && TryParsePoint(parts, 3, out var mwx, out var mwy))
                        SendMouseWheel(delta, mwx, mwy);
                    break;
                case "KD":
                    if (parts.Length >= 3 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var keyDown))
                        SendKey((ushort)keyDown, false);
                    break;
                case "KU":
                    if (parts.Length >= 3 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var keyUp))
                        SendKey((ushort)keyUp, true);
                    break;
            }
        }

        private static bool TryParsePoint(string[] parts, int startIndex, out double x, out double y)
        {
            x = 0;
            y = 0;
            if (parts.Length < startIndex + 2)
                return false;

            return double.TryParse(parts[startIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                && double.TryParse(parts[startIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
        }

        private bool IsRemoteInputAllowed()
        {
            if (chkAllowRemoteInput.InvokeRequired)
            {
                return (bool)chkAllowRemoteInput.Invoke(new Func<bool>(() => chkAllowRemoteInput.Checked));
            }

            return chkAllowRemoteInput.Checked;
        }

        private static void MoveMouseToNormalized(double x, double y)
        {
            var bounds = Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
            var px = (int)Math.Round(Math.Clamp(x, 0, 1) * (bounds.Width - 1)) + bounds.Left;
            var py = (int)Math.Round(Math.Clamp(y, 0, 1) * (bounds.Height - 1)) + bounds.Top;
            SetCursorPos(px, py);
        }

        private static void SendMouseButton(string button, bool isDown, double x, double y)
        {
            MoveMouseToNormalized(x, y);

            uint flag = button switch
            {
                "Left" => isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
                "Right" => isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
                "Middle" => isDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
                _ => 0
            };

            if (flag == 0)
                return;

            var input = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dwFlags = flag
                    }
                }
            };

            _ = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private static void SendMouseWheel(int delta, double x, double y)
        {
            MoveMouseToNormalized(x, y);

            var input = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dwFlags = MOUSEEVENTF_WHEEL,
                        mouseData = (uint)delta
                    }
                }
            };

            _ = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private static void SendKey(ushort key, bool keyUp)
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = key,
                        dwFlags = keyUp ? KEYEVENTF_KEYUP : 0
                    }
                }
            };

            _ = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;

            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
    }
}
