using Caseomatic.Net.Utility;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Linq;

namespace Caseomatic.Net
{
    public class Client<TClientPacket, TServerPacket> where TClientPacket : IClientPacket
        where TServerPacket : IServerPacket
    {
        public delegate void OnReceivePacketHandler(TServerPacket packet);
        /// <summary>
        /// Called when a packet from the server is received.
        /// </summary>
        public event OnReceivePacketHandler OnReceivePacket;

        public delegate void OnConnectionLostHandler();
        /// <summary>
        /// Called when the connection to the server is lost, noticed by an exception or heartbeat.
        /// </summary>
        public event OnConnectionLostHandler OnConnectionLost;

        private Socket socket;
        private Thread receivePacketsThread;

        private byte[] packetReceivingBuffer;
        private object packetReceivingLock;
        private readonly int port;
        private bool isConnectionLost;

        private readonly ConcurrentStack<TServerPacket> receivePacketsSynchronizationStack;

        private ICommunicationModule communicationModule;
        /// <summary>
        /// The communication module that controls the conversion from bytes to packets and vice versa.
        /// </summary>
        public ICommunicationModule CommunicationModule
        {
            get
            {
                return communicationModule;
            }
            set
            {
                communicationModule = value;
            }
        }

        private bool isConnected;
        /// <summary>
        /// Returns if the client is currently connected. Updated by the Connect and Disconnect methods.
        /// </summary>
        public bool IsConnected
        {
            get { return isConnected; }
        }

        public IPEndPoint ServerEndPoint
        {
            get { return (IPEndPoint)socket.RemoteEndPoint; }
        }

        public Client(int port)
        {
            this.port = port;
            packetReceivingLock = new object();

            receivePacketsSynchronizationStack = new ConcurrentStack<TServerPacket>();
            communicationModule = new DefaultCommunicationModule();
        }
        ~Client()
        {
            Disconnect();
        }

        /// <summary>
        /// Connects to an IP endpoint.
        /// </summary>
        /// <param name="serverEndPoint">The IP endpoint that shall be connected to.</param>
        public void Connect(IPEndPoint serverEndPoint)
        {
            lock (packetReceivingLock)
            {
                if (!isConnected)
                    OnConnect(serverEndPoint);
            }
        }
        /// <summary>
        /// Connects to the resolved IP endpoint of a host name and port.
        /// </summary>
        /// <param name="hostName">The host name that shall be resolved.</param>
        /// <param name="port">The port that shall be connected to.</param>
        public void Connect(string hostName, int port)
        {
            var serverIpAddresses = Dns.GetHostAddresses(hostName);
            foreach (var serverIpAddress in serverIpAddresses)
            {
                if (serverIpAddress.AddressFamily == AddressFamily.InterNetwork)
                {
                    var serverIpEndPoint = new IPEndPoint(serverIpAddress, port);
                    Connect(serverIpEndPoint);

                    return;
                }
            }

            Console.WriteLine("Resolving the hostname \"" + hostName+  "\" not possible: No address of type inter-network v4 found.");
        }

        /// <summary>
        /// Disconnects the client from the server.
        /// </summary>
        public void Disconnect()
        {
            lock (receivePacketsThread)
            {
                if (isConnected)
                    OnDisconnect();
            }
        }

        /// <summary>
        /// Invokes all OnReceivePacket events of all packets that have been asynchronously received. 
        /// Invokes all OnClientConnectionLost events that have been asynchronously ordered.
        /// </summary>
        public virtual void FireEvents()
        {
            if (!isConnected)
                return;

            if (OnReceivePacket != null)
            {
                var events = receivePacketsSynchronizationStack.PopAll();
                for (int i = 0; i < events.Length; i++)
                    OnReceivePacket(events[i]);
            }

            if (isConnectionLost && OnConnectionLost != null)
                OnConnectionLost();
        }

        /// <summary>
        /// Heartbeats the connection on sending, receiving and its availability.
        /// </summary>
        /// <param name="repairIfBroken">Reconnects to the server if the connection is invalid.</param>
        /// <returns>Returns if the heartbeat has been successful, if repairing is activated, returns if the repair has been successful.</returns>
        public bool HeartbeatConnection(bool repairIfBroken)
        {
            if (isConnected)
            {
                bool isReallyConnected;
                lock (packetReceivingLock)
                    isReallyConnected = socket.IsConnectionValid();

                if (!isReallyConnected)
                {
                    Console.WriteLine("The server shows no heartbeat" + (repairIfBroken ?
                        ", trying to repair connection" : ", disconnecting"));

                    Disconnect();
                    isConnectionLost = true;

                    if (repairIfBroken)
                        RepairConnection();
                }

                return isReallyConnected;
            }
            else
                return false;
        }

        protected virtual void OnConnect(IPEndPoint serverEndPoint)
        {
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.Bind(new IPEndPoint(IPAddress.Any, port));

                socket.ConfigureInitialSocket();

                socket.Connect(serverEndPoint);
                packetReceivingBuffer = new byte[socket.ReceiveBufferSize];

                isConnected = true;

                receivePacketsThread = new Thread(ReceivePacketsLoop);
                receivePacketsThread.Start();
            }
            catch (SocketException ex)
            {
                Console.WriteLine("Connecting to " + serverEndPoint.ToString() + " resulted in a problem: " + ex.SocketErrorCode +
                    "\n" + ex.Message);
                Disconnect();
            }
        }
        protected virtual void OnDisconnect()
        {
            try
            {
                isConnected = false;
                receivePacketsThread.Join();

                socket.Close(); // Or use socket.Disconnect(true) instead of close/null?
                Console.WriteLine("Disconnected from the server");
            }
            catch (SocketException ex)
            {
                Console.WriteLine("Disconnecting resulted in a problem: " + ex.SocketErrorCode);
            }
        }

        /// <summary>
        /// Sends a packet to the server.
        /// </summary>
        /// <param name="packet">The packet that shall be sent.</param>
        public void SendPacket(TClientPacket packet)
        {
            try
            {
                if (isConnected)
                {
                    byte[] packetBytes;
                    int sentBytes;

                    lock (packetReceivingLock)
                    {
                        packetBytes = CommunicationModule.ConvertSend(packet);
                        sentBytes = socket.Send(packetBytes);
                    }

                    if (sentBytes == 0)
                    {
                        Console.WriteLine("Sending to server resulted in a problem: Peer not reached");
                        HeartbeatConnection(false);
                    }
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine("Sending to the server resulted in a problem: " + ex.SocketErrorCode +
                    "\n" + ex.Message);
                HeartbeatConnection(true);
            }
        }

        #region Requests
        /// <summary>
        /// Sends a packet to the server and synchronously awaits the next answer.
        /// </summary>
        /// <typeparam name="TClientRequest">The type of the packet that is sent.</typeparam>
        /// <typeparam name="TServerAnswer">The type of the packet that shall be received.</typeparam>
        /// <param name="requestPacket">The request packet that is sent.</param>
        /// <returns>The answer packet from the server.</returns>
        public TServerAnswer SendRequest<TClientRequest, TServerAnswer>(TClientRequest requestPacket)
            where TClientRequest : TClientPacket, IPacketRequestable where TServerAnswer : TServerPacket
        {
            if (isConnected)
            {
                SendPacket(requestPacket);
                var answerPacket = ReceivePacket();

                return answerPacket != null ?
                    (TServerAnswer)answerPacket : default(TServerAnswer);
            }
            else
                return default(TServerAnswer);
        }

        /// <summary>
        /// Sends a request and indicates if a proper answer has been received.
        /// </summary>
        /// <typeparam name="TClientRequest">The type of the packet that is sent.</typeparam>
        /// <typeparam name="TServerAnswer">The type of the packet that shall be received.</typeparam>
        /// <param name="requestPacket">The request packet that is sent.</param>
        /// <param name="answerPacket">The answer packet that shall be received.</param>
        /// <returns>States if a proper answer has been received.</returns>
        public bool TrySendRequest<TClientRequest, TServerAnswer>(TClientRequest requestPacket, out TServerAnswer answerPacket)
            where TClientRequest : TClientPacket, IPacketRequestable where TServerAnswer : TServerPacket
        {
            answerPacket = SendRequest<TClientRequest, TServerAnswer>(requestPacket);
            return !answerPacket.Equals(default(TServerAnswer));
        }

        /// <summary>
        /// Sends a request to the server and asynchronously awaits a proper answer from the server.
        /// </summary>
        /// <typeparam name="TClientRequest">The type of the packet that is sent.</typeparam>
        /// <typeparam name="TServerAnswer">The type of the packet that shall be sent.</typeparam>
        /// <param name="requestPacket">The request packet that is sent.</param>
        /// <returns>The answer packet from the server.</returns>
        public TServerAnswer SendRequestAsync<TClientRequest, TServerAnswer>(TClientRequest requestPacket)
            where TClientRequest : TClientPacket, IPacketRequestable where TServerAnswer : TServerPacket
        {
            TServerAnswer serverAnswerPacket = default(TServerAnswer);
            object lockObj = new object();
            var rcvThread = new Thread(() =>
            {
                lock (lockObj)
                    serverAnswerPacket = (TServerAnswer)ReceivePacket();
            });
            rcvThread.Start();

            lock (lockObj)
                return serverAnswerPacket;
        }

        /// <summary>
        /// Combines the TrySendRequest and SendRequestAsync methods.
        /// </summary>
        public bool TrySendRequestAsync<TClientRequest, TServerAnswer>(TClientRequest requestPacket, out TServerAnswer answerPacket) where TClientRequest : TClientPacket, IPacketRequestable
            where TServerAnswer : TServerPacket
        {
            answerPacket = SendRequestAsync<TClientRequest, TServerAnswer>(requestPacket);
            return answerPacket.Equals(default(TServerAnswer));
        }
        #endregion

        private void ReceivePacketsLoop()
        {
            try
            {
                while (isConnected)
                {
                    var serverPacket = ReceivePacket();
                    if (serverPacket != null)
                    {
                        receivePacketsSynchronizationStack.Push(serverPacket);
                    }
                    // Else: Information about corruption already printed in ReceivePacket()
                    //    Console.WriteLine("The received packet is corrupt.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Receiving in the client loop resulted in an exception: " + ex.Message);
                HeartbeatConnection(true);
            }
        }

        /// <summary>
        /// Synchronously receives a packet from the server.
        /// </summary>
        /// <returns>The received packet from the server, or the default type value if the method malfunctioned.</returns>
        private TServerPacket ReceivePacket()
        {
            try
            {
                lock (packetReceivingLock)
                {
                    var receivedBytes = socket.Receive(packetReceivingBuffer);
                    if (receivedBytes != 0)
                    {
                        return CommunicationModule.ConvertReceive<TServerPacket>(packetReceivingBuffer);
                    }
                }

                Console.WriteLine("Receiving from server resulted in a problem: 0 bytes received");
                Disconnect();

                return default(TServerPacket);
            }
            catch (SocketException ex) when(ex.SocketErrorCode != SocketError.TimedOut)
            {
                Console.WriteLine("Receiving from server resulted in a problem: " + ex.SocketErrorCode +
                    "\n" + ex.Message);
                HeartbeatConnection(true);

                return default(TServerPacket);
            }
        }

        /// <summary>
        /// Repairs the connection by disconnecting, sending a ping and reconnecting under the same circumstances, heartbeating to check if it worked.
        /// </summary>
        /// <returns></returns>
        private bool RepairConnection()
        {
            // The endpoint the client is currently connected to
            IPEndPoint remoteEndPoint;
            lock (packetReceivingLock)
            {
                remoteEndPoint = (IPEndPoint)socket.RemoteEndPoint;
            }
            Disconnect();

            var ping = new Ping();
            var pReply = ping.Send(remoteEndPoint.Address, 250);

            if (pReply.Status == IPStatus.Success)
            {
                Console.WriteLine("Reconnecting to server");

                Connect(remoteEndPoint);
                return HeartbeatConnection(false);
            }
            else
            {
                Console.WriteLine("IP not pingable. Result: " + pReply.Status + ", dropping off");
                return false;
            }
        }
    }
}
