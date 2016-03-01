using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Linq;
using Caseomatic.Net.Utility;

namespace Caseomatic.Net
{
    public class Server<TServerPacket, TClientPacket> where TServerPacket : IServerPacket
        where TClientPacket : IClientPacket
    {
        public delegate void OnReceiveClientPacketHandler(int connectionId, TClientPacket packet);
        public event OnReceiveClientPacketHandler OnReceiveClientPacket;

        public delegate void OnClientConnectionLostHandler(int connectionId);
        public event OnClientConnectionLostHandler OnClientConnectionLost;

        private TcpListener server;
        private Thread acceptSocketsThread;
        private int connectionIdGenerationNumber = 1;
        private readonly ConcurrentStack<int> lostConnectionsBuffer;
        
        protected readonly ConcurrentDictionary<int, ClientConnection> clientConnections;
        protected readonly ConcurrentStack<ClientPacketPair> receivePacketsBuffer;

        private bool isHosting;
        /// <summary>
        /// Indicates if the server is currently hosting or not, accepting incoming connections with an underlying TCP server.
        /// </summary>
        public bool IsHosting
        {
            get { return isHosting; }
        }

        /// <summary>
        /// The IP endpoint used by the TCP server for accepting incoming connections.
        /// </summary>
        public IPEndPoint LocalEndPoint
        {
            get { return (IPEndPoint)server.LocalEndpoint; }
        }

        private ICommunicationModule<TClientPacket, TServerPacket> communicationModule;
        /// <summary>
        /// The communication module that controls the conversion from bytes to packets and vice versa.
        /// </summary>
        public ICommunicationModule<TClientPacket, TServerPacket> CommunicationModule
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

        public Server(int port)
        {
            server = new TcpListener(new IPEndPoint(IPAddress.Any, port));
            clientConnections = new ConcurrentDictionary<int, ClientConnection>();
            lostConnectionsBuffer = new ConcurrentStack<int>();

            communicationModule = new DefaultCommunicationModule<TClientPacket, TServerPacket>();
            receivePacketsBuffer = new ConcurrentStack<ClientPacketPair>();
        }
        ~Server()
        {
            Close();
        }

        /// <summary>
        /// Starts accepting incoming connections with an underlying TCP server.
        /// </summary>
        public void Host()
        {
            if (!isHosting)
            {
                OnHost();
            }
        }

        /// <summary>
        /// Closes the TCP server and drops all connections.
        /// </summary>
        public void Close()
        {
            if (isHosting)
            {
                OnClose();
            }
        }

        /// <summary>
        /// Invokes all OnReceiveClientPacket events of all packets that have been asynchronously received. 
        /// Invokes all OnClientConnectionLost events that have been asynchronously ordered.
        /// </summary>
        public virtual void FireEvents()
        {
            if (!isHosting)
                return;

            if (OnReceiveClientPacket != null)
            {
                var events = receivePacketsBuffer.PopAll();
                foreach (var ev in events)
                    OnReceiveClientPacket(ev.connectionId, ev.packet);
            }

            if (OnClientConnectionLost != null)
            {
                var lostConnections = lostConnectionsBuffer.PopAll();
                for (int i = 0; i < lostConnections.Length; i++)
                    OnClientConnectionLost(lostConnections[i]);
            }
        }

        /// <summary>
        /// Heartbeats a specific connection to a client.
        /// </summary>
        /// <param name="clientConnection">The client connectio that shall be tested.</param>
        public void HeartbeatConnection(ClientConnection clientConnection) // Put async!
        {
            var isClientConnected = clientConnection.socket.IsConnectionValid();
            if (!isClientConnected)
            {
                Console.WriteLine("The client " + clientConnection.connectionId + " shows no heartbeat: Dropping off");
                KickClientConnection(clientConnection.connectionId);
            }
            // Else: Do something if connected properly?
        }

        /// <summary>
        /// Heartbeats all connections that are currently held.
        /// </summary>
        public void HeartbeatConnections()
        {
            var clientConnectionsCopy = new ClientConnection[clientConnections.Count];
            clientConnections.AllValues.CopyTo(clientConnectionsCopy, 0);

            for (int i = 0; i < clientConnectionsCopy.Length; i++)
                HeartbeatConnection(clientConnectionsCopy[i]);
        }

        /// <summary>
        /// Disconnects a client with a specific connection-ID.
        /// </summary>
        /// <param name="connectionId">The connection-ID of the client that shall be disconnected.</param>
        /// <returns>If the disconnecting of the client has worked.</returns>
        public bool DisconnectClient(int connectionId)
        {
            if (clientConnections.ContainsKey(connectionId))
            {
                var clientConnection = clientConnections[connectionId];
                clientConnection.terminate = true;

                clientConnection.socket.Close();
                clientConnections.Remove(connectionId);

                Console.WriteLine("Disconnected the client " + connectionId);
                return true;
            }
            else return false;
        }
        /// <summary>
        /// Disconnects a client based on a reference to its client connection.
        /// </summary>
        /// <param name="clientConnection">The reference to the client connection.</param>
        /// <returns>If the disconnecting of the client has worked.</returns>
        public bool DisconnectClient(ClientConnection clientConnection)
        {
            return DisconnectClient(clientConnection.connectionId);
        }

        /// <summary>
        /// Sends a packet to an array of clients specified by their connection-IDs.
        /// </summary>
        /// <param name="packet">The packet that shall be sent.</param>
        /// <param name="connectionIds">The connection-IDs of the clients this packet shall be sent to.</param>
        public void SendPacket(TServerPacket packet, params int[] connectionIds)
        {
            for (int i = 0; i < connectionIds.Length; i++)
            {
                SendPacket(packet, clientConnections[connectionIds[i]]);
            }
        }

        /// <summary>
        /// Sends a packet to all connected clients.
        /// </summary>
        /// <param name="packet">The packet to be sent.</param>
        public void SendPacket(TServerPacket packet)
        {
            ImmutableArray currentClientConnections = new int[clientConnections.Count];
            clientConnections.AllKeys.CopyTo(currentClientConnections, 0);

            SendPacket(packet, currentClientConnections);
        }

        protected void SendPacket(TServerPacket packet, params ClientConnection[] clientConnections)
        {
            foreach (var clientConnection in clientConnections)
            {
                try
                {
                    var packetBytes = CommunicationModule.ConvertSend(packet);
                    var sentBytes = clientConnection.socket.Send(packetBytes);

                    if (sentBytes == 0)
                    {
                        HeartbeatConnection(clientConnection);
                    }
                }
                catch (SocketException ex)
                {
                    Console.WriteLine("Sending to client " + clientConnection.connectionId + " resulted in a problem: " + ex.SocketErrorCode + "\n" + ex.Message);
                    HeartbeatConnection(clientConnection);
                }
            }
        }

        protected virtual void OnHost()
        {
            server.Start();
            isHosting = true;

            acceptSocketsThread = new Thread(AcceptSocketsLoop);
            acceptSocketsThread.Start();
        }
        protected virtual void OnClose()
        {
            isHosting = false;
            server.Stop();
            
            var connectedConnectionIds = clientConnections.AllValues.ToArray();
            foreach (var connectionId in connectedConnectionIds)
            {
                DisconnectClient(connectionId);
            }
        }

        private void AcceptSocketsLoop()
        {
            while (isHosting)
            {
                if (!server.Pending())
                    continue;

                var socket = server.AcceptSocket();
                RegisterSocket(socket);
            }
        }
        private void RegisterSocket(Socket sock)
        {
            var connectionId = -1;
            try
            {
                sock.ConfigureInitialSocket();

                connectionId = connectionIdGenerationNumber++;
                while (clientConnections.ContainsKey(connectionId)) // Reset the connection-ID until its free
                    connectionId = connectionIdGenerationNumber++;

                var receivePacketsThread = new Thread(ReceivePacketsLoop);
                receivePacketsThread.Name = "Client Connection " + connectionId;

                var clientConnection = new ClientConnection(connectionId, sock, receivePacketsThread);
                clientConnections.Add(connectionId, clientConnection);

                // TODO: Fix security exploit that you can connect to the server without sending a handshake, the play core will still send updates to you
                receivePacketsThread.Start(connectionId); // Think about ThreadPool. But if a thread cannot get spawned now, then never in time?
            }
            catch (Exception ex)
            {
                Console.WriteLine("Initializing the connection attempt from client " + sock.RemoteEndPoint + " resulted in a problem: " + ex.Message);
                KickClientConnection(connectionId);
            }
        }

        private void ReceivePacketsLoop(object connectionIdObj)
        {
            var connectionId = (int)connectionIdObj;

            try
            {
                var clientConnection = clientConnections[connectionId];
                while (isHosting && !clientConnection.terminate)
                {
                    var clientPacket = ReceivePacket(clientConnection);
                    if (clientPacket != null)
                    {
                        receivePacketsBuffer.Push(new ClientPacketPair(connectionId, clientPacket));
                    }
                    // Else: Packet is corrupted
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in the receive loop: " + ex.Message);
                KickClientConnection(connectionId);
            }
        }

        private TClientPacket ReceivePacket(ClientConnection clientConnection)
        {
            try
            {
                var receivedBytes = clientConnection.socket.Receive(clientConnection.packetReceivingBuffer);
                if (receivedBytes == 0)
                {
                    Console.WriteLine("Receiving from client " + clientConnection.connectionId + " resulted in a problem: 0 bytes received");
                    HeartbeatConnection(clientConnection);

                    return default(TClientPacket);
                }
                else
                {
                    return CommunicationModule.ConvertReceive(clientConnection.packetReceivingBuffer);
                }
            }
            catch (SocketException ex) when(ex.SocketErrorCode != SocketError.TimedOut)
            {
                Console.WriteLine("Receiving for client " + clientConnection.connectionId + " resulted in a problem: "
                    + ex.SocketErrorCode + "\n" + ex.Message);
                KickClientConnection(clientConnection.connectionId);

                return default(TClientPacket);
            }
        }

        private void KickClientConnection(int connectionId)
        {
            if (DisconnectClient(connectionId))
            {
                lostConnectionsBuffer.Push(connectionId);
            }
        }
        private void KickClientConnection(ClientConnection clientConnection)
        {
            KickClientConnection(clientConnection.connectionId);
        }

        protected struct ClientPacketPair
        {
            public readonly int connectionId;
            public readonly TClientPacket packet;

            public ClientPacketPair(int connectionId, TClientPacket packet)
            {
                this.connectionId = connectionId;
                this.packet = packet;
            }
        }
    }

    /// <summary>
    /// Represents a client connection containing its thread, socket and connection-ID.
    /// </summary>
    public class ClientConnection
    {
        public readonly int connectionId;

        internal readonly Socket socket;
        internal readonly Thread receivePacketsThread;
        internal readonly byte[] packetReceivingBuffer;
        internal bool terminate; // Set to true when disconnecting/closing

        public ClientConnection(int connectionId, Socket socket, Thread receivePacketsThread)
        {
            this.connectionId = connectionId;
            this.socket = socket;
            this.receivePacketsThread = receivePacketsThread;
            packetReceivingBuffer = new byte[socket.ReceiveBufferSize];
        }
    }

    //public enum ErrorType
    //{
    //    ZeroBytesReceived,
    //    ZeroBytesSent,
    //    NoHeartbeat
    //}
}
