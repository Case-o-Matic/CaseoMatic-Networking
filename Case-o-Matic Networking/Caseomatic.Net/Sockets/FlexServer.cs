using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace Caseomatic.Net
{
    public class FlexServer<TServerPacket, TClientPacket>
        where TServerPacket : class, IServerPacket where TClientPacket : class, IClientPacket
    {
        public delegate void OnReceiveClientPacketHandler(TClientPacket packet, int senderId);
        public event OnReceiveClientPacketHandler OnReceivePacket;

        public delegate void OnConnectHandler(IPacketHandshake handshakePacket, int connectionId, ConnectionApprovalParams approval);
        public event OnConnectHandler OnClientConnect;
        public delegate void OnDisconnectHandler(int connectionId);
        public event OnDisconnectHandler OnClientDisconnect;

        public NetPeerConfiguration Configuration
        {
            get { return server.Configuration; }
        }
        public NetPeerStatistics Statistics
        {
            get { return server.Statistics; }
        }

        public NetPeerStatus Status
        {
            get { return server.Status; }
        }

        private readonly NetServer server;
        private readonly Dictionary<int, NetConnection> connections;
        private readonly Dictionary<NetConnection, int> connectionIds;

        private int connectionIdGenerationValue;
        private NetIncomingMessage incomingMessage;

        public FlexServer(int port, IPAddress multicastAddress)
        {
            var configuration = new NetPeerConfiguration("Server");
            configuration.EnableMessageType(NetIncomingMessageType.ConnectionApproval);
            configuration.Port = port;

            server = new NetServer(configuration);
            connections = new Dictionary<int, NetConnection>();
            connectionIds = new Dictionary<NetConnection, int>();
        }

        public void Host()
        {
            server.Start();
        }

        public void Stop(string reason)
        {
            server.Shutdown(reason);
        }

        public void ApproveClient(int connectionId, TServerPacket answerHandshakePacket = null)
        {
            var netConn = connections[connectionId];
            if (answerHandshakePacket != null)
                netConn.Approve(CreateMessageFromPacket(answerHandshakePacket));
            else
                netConn.Approve();
        }
        public void DenyClient(int connectionId, string reason = "")
        {
            connections[connectionId].Deny(reason);
        }

        public void SendReliablePacket(TServerPacket packet, params int[] recipientIds)
        {
            SendPacket(packet, recipientIds, NetDeliveryMethod.ReliableUnordered, 1);
        }

        public void SendUnreliablePacket(TServerPacket packet, params int[] recipientIds)
        {
            SendPacket(packet, recipientIds, NetDeliveryMethod.Unreliable, 2);
        }

        public void ReceivePackets()
        {
            server.RegisterReceivedCallback(new SendOrPostCallback(OnReceiveMessage));
        }

        public void DisconnectClient(int connectionId, string reason)
        {
            NetConnection connection;
            if (connections.TryGetValue(connectionId, out connection) &&
                connection.Status == NetConnectionStatus.Connected)
            {
                connection.Disconnect(reason);
            }
        }

        private NetOutgoingMessage CreateMessageFromPacket(TServerPacket packet)
        {
            var bytes = PacketConverter.ToBytes(packet);
            var message = server.CreateMessage(bytes.Length);
            message.Write(bytes);

            return message;
        }
        private void SendPacket(TServerPacket packet, int[] recipientIds, NetDeliveryMethod deliveryMethod, int sequenceChannel)
        {
            var recipients = ConnectionIdsToConnectionArray(recipientIds);
            server.SendMessage(CreateMessageFromPacket(packet), recipients, deliveryMethod, sequenceChannel);
        }
        private NetConnection[] ConnectionIdsToConnectionArray(int[] connectionIds)
        {
            var resolvedConnections = new NetConnection[connectionIds.Length];
            for (int i = 0; i < connectionIds.Length; i++)
                resolvedConnections[i] = connections[connectionIds[i]];

            return resolvedConnections;
        }
        private void OnReceiveMessage(object peer)
        {
            incomingMessage = (peer as NetClient).ReadMessage();
            switch (incomingMessage.MessageType)
            {
                case NetIncomingMessageType.StatusChanged:
                    if (OnClientDisconnect != null &&
                        incomingMessage.SenderConnection.Status == NetConnectionStatus.Disconnected)
                    {
                        OnClientDisconnect(connectionIds[incomingMessage.SenderConnection]);
                    }
                    break;

                case NetIncomingMessageType.ConnectionApproval:
                case NetIncomingMessageType.Data:
                    var bytes = incomingMessage.ReadBytes(incomingMessage.LengthBytes);
                    var packet = PacketConverter.ToPacket<TClientPacket>(bytes);

                    // Deny or accept connection
                    if (incomingMessage.MessageType == NetIncomingMessageType.ConnectionApproval)
                    {
                        // Generate a connection-ID which needs to be unique
                        var newConnectionId = unchecked(connectionIdGenerationValue + 1);
                        var connApprovalParams = new ConnectionApprovalParams();

                        if (OnClientConnect != null)
                            OnClientConnect((IPacketHandshake)packet, newConnectionId, connApprovalParams);

                        if (connApprovalParams.approve)
                        {
                            incomingMessage.SenderConnection.Approve(CreateMessageFromPacket(connApprovalParams.approveHandshakePacket));

                            connectionIdGenerationValue = newConnectionId;
                            connections.Add(newConnectionId, incomingMessage.SenderConnection);
                            connectionIds.Add(incomingMessage.SenderConnection, newConnectionId);
                        }
                        else
                        {
                            incomingMessage.SenderConnection.Deny(connApprovalParams.denyReason);
                            Console.WriteLine("Denied handshake of " + incomingMessage.SenderEndPoint + " because: " + connApprovalParams.denyReason);
                        }
                    }
                    else
                    {
                        if (OnReceivePacket != null)
                            OnReceivePacket(packet, connectionIds[incomingMessage.SenderConnection]);
                    }
                    break;

                case NetIncomingMessageType.Error:
                    Console.WriteLine("The incoming message is of type error. This should never happen.");
                    break;

                case NetIncomingMessageType.WarningMessage:
                    Console.WriteLine("The incoming message is a warning: " + incomingMessage.ReadString());
                    break;
                case NetIncomingMessageType.ErrorMessage:
                    Console.WriteLine("The incoming message is an error: " + incomingMessage.ReadString());
                    break;

                default:
                    Console.WriteLine("A message with type \"" + incomingMessage.MessageType + " has been received but is unsupported.");
                    break;
            }

            server.Recycle(incomingMessage);
        }

        public class ConnectionApprovalParams
        {
            public TServerPacket approveHandshakePacket;
            public bool approve;
            public string denyReason;
        }
    }
}
