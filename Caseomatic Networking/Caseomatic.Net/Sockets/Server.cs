using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Linq;

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
        
        protected readonly Dictionary<int, ClientConnection> clientConnections; // ConcurrentDictionary is not available in .NET 3.5

        private bool isHosting;
        public bool IsHosting
        {
            get { return isHosting; }
        }

        public IPEndPoint LocalEndPoint
        {
            get { return (IPEndPoint)server.LocalEndpoint; }
        }

        private ICommunicationModule communicationModule;
        public ICommunicationModule CommunicationModule
        {
            get { return communicationModule; }
            set { communicationModule = value; }
        }

        public Server(int port)
        {
            server = new TcpListener(new IPEndPoint(IPAddress.Any, port));
            clientConnections = new Dictionary<int, ClientConnection>();

            communicationModule = new DefaultCommunicationModule();
        }
        ~Server()
        {
            Close();
        }

        public void Host()
        {
            if (!isHosting)
            {
                OnHost();
            }
        }
        public void Close()
        {
            if (isHosting)
            {
                OnClose();
            }
        }

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
        public void HeartbeatConnections()
        {
            var clientConnectionsCopy = new ClientConnection[clientConnections.Count];
            clientConnections.Values.CopyTo(clientConnectionsCopy, 0);

            for (int i = 0; i < clientConnectionsCopy.Length; i++)
                HeartbeatConnection(clientConnectionsCopy[i]);
        }

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
        public void DisconnectClient(ClientConnection clientConnection)
        {
            DisconnectClient(clientConnection.connectionId);
        }

        public void SendPacket(TServerPacket packet, params int[] connectionIds)
        {
            for (int i = 0; i < connectionIds.Length; i++)
            {
                SendPacket(packet, clientConnections[connectionIds[i]]);
            }
        }
        public void SendPacket(TServerPacket packet)
        {
            var currentClientConnections = new int[clientConnections.Count];
            clientConnections.Keys.CopyTo(currentClientConnections, 0);

            SendPacket(packet, currentClientConnections);
        }

        protected void SendPacket(TServerPacket packet, params ClientConnection[] clientConnections)
        {
            foreach (var clientConnection in clientConnections)
            {
                try
                {
                    var packetBytes = PacketConverter.ToBytes(packet);
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
            
            var connectedConnectionIds = clientConnections.Values.ToArray();
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

                    var onReceiveClientPacket = OnReceiveClientPacket;
                    if (onReceiveClientPacket != null && clientPacket != null)
                    {
                        onReceiveClientPacket(connectionId, clientPacket);
                    }
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
                    var packetBuffer = new byte[receivedBytes]; // Alternative: Dont copy into new array but directly pass the clientConnection.packetReceivingBuffer to the packet converter
                    Buffer.BlockCopy(clientConnection.packetReceivingBuffer, 0, packetBuffer, 0, receivedBytes);

                    return PacketConverter.ToPacket<TClientPacket>(packetBuffer);
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
                if (OnClientConnectionLost != null)
                    OnClientConnectionLost(connectionId);
            }
        }
        private void KickClientConnection(ClientConnection clientConnection)
        {
            KickClientConnection(clientConnection.connectionId);
        }
    }

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

    public enum ErrorType
    {
        ZeroBytesReceived,
        ZeroBytesSent,
        NoHeartbeat
    }
}
