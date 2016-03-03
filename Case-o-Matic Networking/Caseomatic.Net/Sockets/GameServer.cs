using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Linq;

namespace Caseomatic.Net
{
    public class GameServer<TServerPacket, TClientPacket> : Server<TServerPacket, TClientPacket>
        where TServerPacket : IServerPacket where TClientPacket : IClientPacket
    {
        public event OnReceiveClientPacketHandler OnReceiveClientUdpPacket;

        private readonly IPEndPoint multicastEndPoint;
        private readonly bool activateUdpReceiving;
        private UdpClient udpClient;
        private Thread udpReceiveThread;
        private object udpLockObj;
        private bool isDisposing;

        public GameServer(int port, IPAddress multicastAddress, bool activateUdpReceiving)
            : base(port)
        {
            multicastEndPoint = new IPEndPoint(multicastAddress, 0);
            this.activateUdpReceiving = activateUdpReceiving;
            udpLockObj = new object();
        }
        ~GameServer()
        {
            isDisposing = true;
        }

        /// <summary>
        /// Sends a multicast packet with the underlying UDP client.
        /// </summary>
        /// <param name="packet">The packet you want to send.</param>
        public void SendMulticastPacket(TServerPacket packet)
        {
            lock (udpLockObj)
            {
                var packetBytes = CommunicationModule.ConvertSend(packet);
                udpClient.Send(packetBytes, packetBytes.Length, multicastEndPoint); 
            }
        }

        public override void FireEvents()
        {
            if (OnReceiveClientUdpPacket != null && activateUdpReceiving)
            {
                var clientPacketPairs = receivePacketsBuffer.PopAll();
                foreach (var clientPacketPair in clientPacketPairs)
                    OnReceiveClientUdpPacket(clientPacketPair.connectionId, clientPacketPair.packet);
            }

            base.FireEvents();
        }

        protected override void OnHost()
        {
            udpClient = new UdpClient(multicastEndPoint.Port + 1, AddressFamily.InterNetwork); // Change the port number increment
            udpClient.JoinMulticastGroup(multicastEndPoint.Address);
            // Customize TTL? (default is 32)

            udpReceiveThread = new Thread(ReceiveMulticastPacketsLoop);
            udpReceiveThread.Start();

            base.OnHost();
        }
        protected override void OnClose()
        {
            udpClient.DropMulticastGroup(multicastEndPoint.Address); // Is this really needed?
            udpClient.Close();

            base.OnClose();
        }

        private void ReceiveMulticastPacketsLoop()
        {
            try
            {
                while (!isDisposing)
                {
                    var senderEndPoint = new IPEndPoint(IPAddress.Any, 1);
                    byte[] buffer;

                    lock (udpLockObj)
                        buffer = udpClient.Receive(ref senderEndPoint);

                    var senderConnectionId = clientConnections.FirstOrDefault(conn => conn.Value.socket.RemoteEndPoint == senderEndPoint).Key;

                    if (buffer != null && senderEndPoint == multicastEndPoint
                        && senderConnectionId != 0)
                    {
                        receivePacketsBuffer.Push(new ClientPacketPair(senderConnectionId, CommunicationModule.ConvertReceive(buffer)));
                    }
                    else
                        Console.WriteLine("The multicast packet sender of endpoint " + senderEndPoint.ToString() + " is unknown, dropping packet...");
                }
            }
            catch (SocketException ex)
                when(ex.SocketErrorCode != SocketError.TimedOut && ex.SocketErrorCode != SocketError.Interrupted)
            {
                Console.WriteLine("Receiving on the UDP client has brought an error: " + ex.Message);
            }
        }
    }
}
