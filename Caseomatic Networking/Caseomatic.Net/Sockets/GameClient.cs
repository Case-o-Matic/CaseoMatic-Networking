using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Caseomatic.Net
{
    public class GameClient<TClientPacket, TServerPacket> : Client<TClientPacket, TServerPacket>
        where TClientPacket : IClientPacket where TServerPacket : IServerPacket
    {
        /// <summary>
        /// Called when a UDP multicast from the server is received.
        /// </summary>
        public event OnReceivePacketHandler OnReceiveUdpPacket;

        private readonly UdpClient udpClient;
        private readonly IPAddress multicastAddress;

        public GameClient(int port, IPAddress multicastAddress)
            : base(port)
        {
            udpClient = new UdpClient(port + 1, AddressFamily.InterNetwork);

            this.multicastAddress = multicastAddress;
            udpClient.JoinMulticastGroup(multicastAddress);
        }
        ~GameClient()
        {
            udpClient.DropMulticastGroup(multicastAddress); // Is this really needed?
            udpClient.Close();
        }

        protected override void OnConnect(IPEndPoint serverEndPoint)
        {
            base.OnConnect(serverEndPoint);
        }
        protected override void OnDisconnect()
        {
            base.OnDisconnect();
        }

        private void ReceiveMulticastPacketsLoop()
        {
            while (IsConnected)
            {
                var senderEndPoint = new IPEndPoint(IPAddress.Any, 1);
                var buffer = udpClient.Receive(ref senderEndPoint);

                if (buffer != null &&
                    senderEndPoint.Address == multicastAddress && // Check if the multicast packet sender is valid
                    OnReceiveUdpPacket != null)
                {
                    OnReceiveUdpPacket(PacketConverter.ToPacket<TServerPacket>(buffer));
                }
            }
        }
    }

    /* Some network interfaces have problems with multicasting, this will solve it maybe?

    NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
    foreach (NetworkInterface adapter in nics)
    {
        IPInterfaceProperties ip_properties = adapter.GetIPProperties();
        if (!adapter.GetIPProperties().MulticastAddresses.Any())
            continue; // most of VPN adapters will be skipped
        if (!adapter.SupportsMulticast)
            continue; // multicast is meaningless for this type of connection
        if (OperationalStatus.Up != adapter.OperationalStatus)
            continue; // this adapter is off or not connected
        IPv4InterfaceProperties p = adapter.GetIPProperties().GetIPv4Properties();
        if (null == p)
            continue; // IPv4 is not configured on this adapter
        my_sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, (int)IPAddress.HostToNetworkOrder(p.Index));
    }
    */
}
