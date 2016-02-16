using Caseomatic.Net.Utility;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

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
        private readonly IPEndPoint multicastEndPoint;
        private ConcurrentStack<TServerPacket> receivePacketsUdpSynchronizationStack;
        private object udpClientReceiveLockObj;
        private byte[] udpClientReceiveBuffer;
        private Thread udpReceiveThread;

        public GameClient(int port, IPAddress multicastAddress)
            : base(port)
        {
            multicastEndPoint = new IPEndPoint(multicastAddress, port + 1);
            udpClient = new UdpClient(multicastEndPoint.Port, AddressFamily.InterNetwork);
            // Customize TTL? (default is 32)

            udpClient.JoinMulticastGroup(multicastAddress);
            receivePacketsUdpSynchronizationStack = new ConcurrentStack<TServerPacket>();
            udpClientReceiveLockObj = new object();
            udpClientReceiveBuffer = new byte[udpClient.Client.ReceiveBufferSize];
        }
        ~GameClient()
        {
            udpClient.DropMulticastGroup(multicastEndPoint.Address);
            udpClient.Close();
        }

        public void SendMulticastPacket(TClientPacket packet)
        {
            lock (udpClientReceiveLockObj)
            {
                if (IsConnected)
                {
                    var bytes = CommunicationModule.ConvertSend(packet);
                    udpClient.Send(bytes, bytes.Length, ServerEndPoint);
                }
            }
        }

        public override void FireEvents()
        {
            if (OnReceiveUdpPacket != null)
            {
                var events = receivePacketsUdpSynchronizationStack.PopAll();
                for (int i = 0; i < events.Length; i++)
                    OnReceiveUdpPacket(events[i]);
            }

            base.FireEvents();
        }

        protected override void OnConnect(IPEndPoint serverEndPoint)
        {
            udpReceiveThread = new Thread(ReceiveMulticastPacketsLoop);
            udpReceiveThread.Start();

            base.OnConnect(serverEndPoint);
        } // Joining the receive-thread to stop it is not done in OnDisconnect as the thread should run out naturally

        private void ReceiveMulticastPacketsLoop()
        {
            try
            {
                while (IsConnected)
                {
                    var senderEndPoint = new IPEndPoint(IPAddress.Any, 1);
                    lock (udpClientReceiveLockObj)
                        udpClientReceiveBuffer = udpClient.Receive(ref senderEndPoint);

                    if (udpClientReceiveBuffer != null &&
                        senderEndPoint == multicastEndPoint)
                    {
                        receivePacketsUdpSynchronizationStack.Push(
                            CommunicationModule.ConvertReceive<TServerPacket>(udpClientReceiveBuffer));
                    }
                }
            }
            catch (SocketException ex) when(ex.SocketErrorCode != SocketError.TimedOut)
            {
                Console.WriteLine("Receiving on the UDP client has brought an error: " + ex.Message);
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
        if (p == null)
            continue; // IPv4 is not configured on this adapter
        my_sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, (int)IPAddress.HostToNetworkOrder(p.Index));
    }
    */
}
