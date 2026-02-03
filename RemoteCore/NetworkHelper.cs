using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RemoteCore
{
    public static class NetworkHelper
    {
        /// <summary>
        /// Returns the first valid IPv4 address for the local LAN (non-loopback, non-virtual).
        /// Throws InvalidOperationException when no suitable address is found.
        /// </summary>
        public static IPAddress GetLocalIPv4()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;

                    // Skip virtual, loopback, tunnel
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.Description.ToLower().Contains("virtual") ||
                        ni.Description.ToLower().Contains("vpn") ||
                        ni.Description.ToLower().Contains("pseudo") ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                        continue;

                    var ipProps = ni.GetIPProperties();
                    foreach (var uni in ipProps.UnicastAddresses)
                    {
                        if (uni.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            var addr = uni.Address;
                            if (!IPAddress.IsLoopback(addr))
                                return addr;
                        }
                    }
                }

                // Fallback: loop through DNS-host addresses
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip4 = host.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
                if (ip4 != null)
                    return ip4;

                throw new InvalidOperationException("No suitable IPv4 address found for this machine.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to detect local IPv4 address.", ex);
            }
        }
    }
}
