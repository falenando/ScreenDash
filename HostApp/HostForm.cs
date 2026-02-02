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
            ApplyLocalization();
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
            if (!CanStartListenerWithoutPrompt(Port, out var reason))
            {
                AppendLog("Listener not started: " + reason);
                UpdateStatus("Not ready to connect", System.Drawing.Color.Red);
                _isListening = false;
                return;
            }

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
            UpdateRemoteControlIndicator();

            try
            {
                var streamToken = _cts?.Token ?? System.Threading.CancellationToken.None;
                var inputTask = Task.Run(() => ReceiveInputLoopAsync(socket, streamToken), streamToken);
                var streamer = new RemoteCore.Implementations.FrameStreamer(_capturer, _encoder, _logger);
                await streamer.StreamToAsync(socket, streamToken);
                await inputTask;
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
                UpdateRemoteControlIndicator();
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

        private async Task ReceiveInputLoopAsync(Socket socket, System.Threading.CancellationToken token)
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
                            HandleInputCommand(line);
                    }
                }
            }
            catch
            {
                // ignore input loop errors
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
