using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Launcher.Helpers;

public static class ServerStatusHelper
{
    private const int DefaultLoginServerPort = 20042;

    // From UdpLibrary.UdpPacketType.ServerStatus
    private const byte UdpPacketTypeServerStatus = 32;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ServerStatus
    {
        [MarshalAs(UnmanagedType.U1)]
        public bool IsOnline;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsLocked;

        public int OnlinePlayers;

        public static ServerStatus Offline = new();
    }

    public static async Task<ServerStatus> GetAsync(string serverAddress, int timeout = 5000)
    {
        var serverPort = DefaultLoginServerPort;
        var portIndex = serverAddress.IndexOf(':');

        if (portIndex > 0)
        {
            int.TryParse(serverAddress.AsSpan(portIndex + 1), out serverPort);
            serverAddress = serverAddress.Substring(0, portIndex);
        }

        if (!string.IsNullOrEmpty(serverAddress) && serverPort != 0)
        {
            using var cts = new CancellationTokenSource();

            cts.CancelAfter(timeout);

            try
            {
                if (!IPAddress.TryParse(serverAddress, out var ipAddress))
                {
                    var addresses = await Dns.GetHostAddressesAsync(serverAddress);

                    if (addresses.Length == 0)
                        return ServerStatus.Offline;

                    ipAddress = addresses[0];
                }

                using var udpClient = new UdpClient();

                udpClient.Connect(ipAddress, serverPort);

                byte[] buf = [0x00, UdpPacketTypeServerStatus];

                if (await udpClient.SendAsync(buf, cts.Token) == buf.Length)
                {
                    var result = await udpClient.ReceiveAsync(cts.Token);

                    if (MemoryMarshal.TryRead<ServerStatus>(result.Buffer, out var serverStatus))
                        return serverStatus;
                }
            }
            catch
            {
            }
        }

        return ServerStatus.Offline;
    }
}