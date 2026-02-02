using RemoteCore;
using System;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Globalization;

namespace ViewerApp
{
    public partial class ViewerForm : Form
    {
        private readonly int Port = RemoteCore.Config.GetPortFromFile("viewerconfig.json", 5050);
        private readonly ConnectionLogger _logger = new ConnectionLogger("viewer.log");

        private Socket? _socket;
        private bool _isStreaming = false;
        private long _lastMouseMoveTick;

        public ViewerForm()
        {
            ScreenDash.LocalizationManager.LoadLanguage("viewerconfig.json");
            InitializeComponent();
            ApplyLocalization();

            try
            {
                AppendLog("Detecting local IPv4...");
                var localIp = NetworkHelper.GetLocalIPv4().ToString();
                AppendLog("Local IP: " + localIp);
            }
            catch (Exception ex)
            {
                AppendLog(Localization.Format("ErrorFormat", ex.Message));
            }

            // ensure disconnect button starts as Quit
            btnDisconnect.Text = "Quit";
            btnDisconnect.Click += btnDisconnect_Click;
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

                        // Verify same /24 network: ensure first 3 octets match
                        var targetBytes = targetIp.GetAddressBytes();
                        if (!(parts[0] == targetBytes[0] && parts[1] == targetBytes[1] && parts[2] == targetBytes[2]))
                        {
                            AppendLog("Access code resolves to a different network: " + targetIp);
                            MessageBox.Show(RemoteCore.Localization.Get("DifferentNetworkCode"));
                            return;
                        }

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

                    // Verify explicit IP is on same /24 network as local viewer
                    try
                    {
                        var local = NetworkHelper.GetLocalIPv4();
                        var lp = local.GetAddressBytes();
                        var ep = explicitIp.GetAddressBytes();
                        if (lp.Length == 4 && ep.Length == 4)
                        {
                            if (!(lp[0] == ep[0] && lp[1] == ep[1] && lp[2] == ep[2]))
                            {
                                AppendLog("Explicit IP is in a different network: " + explicitIp);
                                MessageBox.Show(RemoteCore.Localization.Get("DifferentNetworkIP"));
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendLog("Error while validating explicit IP network: " + ex.Message);
                    }

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
            if (_socket == null || !_socket.Connected)
            {
                AppendLog("Quit requested from Disconnect/Quit button.");
                Application.Exit();
                return;
            }

            CloseConnection("Disconnect requested by user. Sending BYE to host...", sendBye: true);
        }

        private async Task ConnectToIpAsync(IPAddress ip)
        {
            AppendLog("Connecting to " + ip + ":" + Port);
            btnDisconnect.Text = "Disconn";

            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var connectTask = _socket.ConnectAsync(ip, Port);
            var timeout = Task.Delay(5000);
            var completed = await Task.WhenAny(connectTask, timeout);
            if (completed == timeout)
            {
                AppendLog("Connect timeout to " + ip);
                MessageBox.Show("Connect timeout");
                _socket?.Close();
                _socket = null;
                btnDisconnect.Text = "Quit";
                return;
            }

            AppendLog("Connected to " + ip);
            using var peer = new TcpPeer(_socket, _logger);

            // request stream
            await peer.SendAsync("REQUEST_STREAM");
            AppendLog("Sent REQUEST_STREAM");

            // start receiving frames
            _isStreaming = true;
            try
            {
                while (_isStreaming && _socket != null && _socket.Connected)
                {
                    // read 8-byte header
                    var headerBuf = new byte[8];
                    var hr = 0;
                    while (hr < 8)
                    {
                        var r = await _socket.ReceiveAsync(headerBuf.AsMemory(hr, 8 - hr), SocketFlags.None);
                        if (r == 0) throw new Exception("Disconnected");
                        hr += r;
                    }

                    var header = Encoding.ASCII.GetString(headerBuf);
                    if (!int.TryParse(header, out var len)) throw new Exception("Invalid header");

                    var buf = new byte[len];
                    var received = 0;
                    while (received < len)
                    {
                        var chunk = await _socket.ReceiveAsync(buf.AsMemory(received, len - received), SocketFlags.None);
                        if (chunk == 0) throw new Exception("Disconnected");
                        received += chunk;
                    }

                    using var ms = new System.IO.MemoryStream(buf);
                    var img = Image.FromStream(ms);
                    ShowRemoteImage(img);
                }
            }
            catch (Exception ex)
            {
                AppendLog("Screen receive ended: " + ex.Message);
            }
            finally
            {
                try { _socket?.Close(); } catch { }
                _socket = null;
                _isStreaming = false;
                btnDisconnect.Text = "Quit";
                // close preview window when stream ends
                try
                {
                    if (_previewForm != null && !_previewForm.IsDisposed)
                    {
                        if (_previewForm.InvokeRequired)
                            _previewForm.Invoke(() => _previewForm.Close());
                        else
                            _previewForm.Close();
                    }
                }
                catch { }
            }
        }

        private Form? _previewForm;
        private PictureBox? _previewBox;

        private void ShowRemoteImage(Image img)
        {
            if (_previewForm == null || _previewForm.IsDisposed)
            {
                _previewForm = new Form();
                _previewForm.StartPosition = FormStartPosition.CenterParent;
                _previewForm.FormBorderStyle = FormBorderStyle.Sizable;
                _previewForm.ClientSize = new Size(1024, 768); // HD-ish window
                _previewForm.KeyPreview = true;
                _previewForm.FormClosed += PreviewForm_FormClosed;
                _previewForm.KeyDown += PreviewForm_KeyDown;
                _previewForm.KeyUp += PreviewForm_KeyUp;
                _previewBox = new PictureBox();
                _previewBox.Dock = DockStyle.Fill;
                _previewBox.SizeMode = PictureBoxSizeMode.Zoom;
                _previewBox.TabStop = true;
                _previewBox.MouseMove += PreviewBox_MouseMove;
                _previewBox.MouseDown += PreviewBox_MouseDown;
                _previewBox.MouseUp += PreviewBox_MouseUp;
                _previewBox.MouseWheel += PreviewBox_MouseWheel;
                _previewBox.MouseEnter += PreviewBox_MouseEnter;
                _previewForm.Controls.Add(_previewBox);
                _previewForm.Show(this);
            }

            if (_previewBox != null)
            {
                if (_previewBox.InvokeRequired)
                    _previewBox.Invoke(() => _previewBox.Image = (Image)img.Clone());
                else
                    _previewBox.Image = (Image)img.Clone();
            }
        }

        private void PreviewForm_FormClosed(object? sender, FormClosedEventArgs e)
        {
            _previewForm = null;
            _previewBox = null;
            CloseConnection("Preview window closed. Disconnecting from host...", sendBye: true);
        }

        private void PreviewForm_KeyDown(object? sender, KeyEventArgs e)
        {
            SendControlCommand($"INPUT|KD|{(int)e.KeyCode}");
        }

        private void PreviewForm_KeyUp(object? sender, KeyEventArgs e)
        {
            SendControlCommand($"INPUT|KU|{(int)e.KeyCode}");
        }

        private void PreviewBox_MouseMove(object? sender, MouseEventArgs e)
        {
            if (Environment.TickCount64 - _lastMouseMoveTick < 30)
                return;

            _lastMouseMoveTick = Environment.TickCount64;
            if (TryGetNormalizedPoint(e.Location, out var x, out var y))
            {
                SendControlCommand($"INPUT|MM|{x.ToString(CultureInfo.InvariantCulture)}|{y.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        private void PreviewBox_MouseDown(object? sender, MouseEventArgs e)
        {
            if (TryGetNormalizedPoint(e.Location, out var x, out var y))
            {
                var button = e.Button switch
                {
                    MouseButtons.Left => "Left",
                    MouseButtons.Right => "Right",
                    MouseButtons.Middle => "Middle",
                    _ => string.Empty
                };

                if (!string.IsNullOrEmpty(button))
                {
                    SendControlCommand($"INPUT|MD|{button}|{x.ToString(CultureInfo.InvariantCulture)}|{y.ToString(CultureInfo.InvariantCulture)}");
                }
            }
        }

        private void PreviewBox_MouseUp(object? sender, MouseEventArgs e)
        {
            if (TryGetNormalizedPoint(e.Location, out var x, out var y))
            {
                var button = e.Button switch
                {
                    MouseButtons.Left => "Left",
                    MouseButtons.Right => "Right",
                    MouseButtons.Middle => "Middle",
                    _ => string.Empty
                };

                if (!string.IsNullOrEmpty(button))
                {
                    SendControlCommand($"INPUT|MU|{button}|{x.ToString(CultureInfo.InvariantCulture)}|{y.ToString(CultureInfo.InvariantCulture)}");
                }
            }
        }

        private void PreviewBox_MouseWheel(object? sender, MouseEventArgs e)
        {
            if (TryGetNormalizedPoint(e.Location, out var x, out var y))
            {
                SendControlCommand($"INPUT|MW|{e.Delta.ToString(CultureInfo.InvariantCulture)}|{x.ToString(CultureInfo.InvariantCulture)}|{y.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        private void PreviewBox_MouseEnter(object? sender, EventArgs e)
        {
            _previewBox?.Focus();
        }

        private bool TryGetNormalizedPoint(Point location, out double x, out double y)
        {
            x = 0;
            y = 0;

            if (_previewBox?.Image == null)
                return false;

            var rect = GetImageDisplayRectangle(_previewBox);
            if (!rect.Contains(location))
                return false;

            x = (location.X - rect.X) / (double)rect.Width;
            y = (location.Y - rect.Y) / (double)rect.Height;
            x = Math.Clamp(x, 0, 1);
            y = Math.Clamp(y, 0, 1);
            return true;
        }

        private static Rectangle GetImageDisplayRectangle(PictureBox pictureBox)
        {
            var image = pictureBox.Image;
            if (image == null)
                return pictureBox.ClientRectangle;

            var boxWidth = pictureBox.ClientSize.Width;
            var boxHeight = pictureBox.ClientSize.Height;
            if (boxWidth == 0 || boxHeight == 0)
                return pictureBox.ClientRectangle;

            var imageRatio = image.Width / (double)image.Height;
            var boxRatio = boxWidth / (double)boxHeight;

            if (imageRatio > boxRatio)
            {
                var width = boxWidth;
                var height = (int)Math.Round(width / imageRatio);
                var y = (boxHeight - height) / 2;
                return new Rectangle(0, y, width, height);
            }

            var h = boxHeight;
            var w = (int)Math.Round(h * imageRatio);
            var xPos = (boxWidth - w) / 2;
            return new Rectangle(xPos, 0, w, h);
        }

        private void SendControlCommand(string command)
        {
            if (_socket == null || !_socket.Connected)
                return;

            var payload = Encoding.UTF8.GetBytes(command + "\n");
            _ = _socket.SendAsync(payload, SocketFlags.None);
        }

        private void CloseConnection(string message, bool sendBye)
        {
            if (_socket == null || !_socket.Connected)
            {
                _isStreaming = false;
                btnDisconnect.Text = "Quit";
                return;
            }

            AppendLog(message);
            if (sendBye)
            {
                try
                {
                    var data = Encoding.UTF8.GetBytes("BYE");
                    _socket.Send(data);
                }
                catch { }
            }

            try { _socket.Shutdown(SocketShutdown.Both); } catch { }
            try { _socket.Close(); } catch { }
            _socket = null;
            _isStreaming = false;
            btnDisconnect.Text = "Quit";
            AppendLog("Disconnected.");
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

        private void ApplyLocalization()
        {
            this.Text = ScreenDash.Resources.Strings.ViewerForm_WindowTitle;
            lblTitle.Text = ScreenDash.Resources.Strings.ViewerForm_Title;
            lblAccessCode.Text = ScreenDash.Resources.Strings.ViewerForm_AccessCode;
            btnConnect.Text = ScreenDash.Resources.Strings.ViewerForm_Connect;
            btnDisconnect.Text = ScreenDash.Resources.Strings.ViewerForm_Disconnect;
            // The status label is dynamic
            // lblStatus.Text = ScreenDash.Resources.Strings.ViewerForm_StatusDisconnected;
        }
    }
}
