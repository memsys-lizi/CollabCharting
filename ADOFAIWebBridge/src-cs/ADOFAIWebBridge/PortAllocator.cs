using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ADOFAIWebBridge
{
    internal static class PortAllocator
    {
        private const int BasePort = 39000;
        private const int PortRange = 2000;

        public static int GetStablePort(string modId)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (byte b in Encoding.UTF8.GetBytes(modId))
                {
                    hash ^= b;
                    hash *= 16777619;
                }

                return BasePort + (int)(hash % PortRange);
            }
        }

        public static int FindAvailable(string host, int preferredPort, int probeCount)
        {
            for (int offset = 0; offset < probeCount; offset++)
            {
                int port = preferredPort + offset;
                if (IsAvailable(host, port))
                {
                    return port;
                }
            }

            throw new WebBridgeException("port_unavailable", $"No available port found near {preferredPort}.");
        }

        private static bool IsAvailable(string host, int port)
        {
            try
            {
                IPAddress address = host == "127.0.0.1" ? IPAddress.Loopback : IPAddress.Parse(host);
                var listener = new TcpListener(address, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
