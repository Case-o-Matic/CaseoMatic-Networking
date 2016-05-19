using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace Caseomatic.Net
{
    public class FlexClient<TClientPacket, TServerPacket> where TClientPacket : class, IClientPacket where TServerPacket :class, IServerPacket
    {
        public delegate void OnDisConnectHandler();
        public event OnDisConnectHandler OnConnect, OnDisconnect;

        public delegate void OnReceiveServerPacketHandler(TServerPacket packet);
        public event OnReceiveServerPacketHandler OnReceivePacket;

        public delegate void OnLostConnectionHandler();
        public event OnLostConnectionHandler OnLostConnection;

        public NetPeerConfiguration Configuration
        {
            get { return client.Configuration; }
        }

        public NetConnectionStatus ConnectionStatus
        {
            get { return client.ConnectionStatus; }
        }

        private readonly NetClient client;
        private NetIncomingMessage incomingMessage;

        private NetConnection Connection
        {
            get { return client.ServerConnection; }
        }

        public FlexClient(int port)
        {
            var configuration = new NetPeerConfiguration("Client");
            configuration.Port = port;
            
            client = new NetClient(configuration);
        }

        public void Connect<TClientHandshakePacket>(IPEndPoint serverEndPoint, TClientHandshakePacket handshakePacket = null) where TClientHandshakePacket : class, TClientPacket, IPacketHandshake
        {
            var message = CreateMessageFromPacket(handshakePacket);
            client.Connect(serverEndPoint, message);
        }

        public void Disconnect(string reason)
        {
            client.Disconnect(reason);
        }

        public void SendReliablePacket(TClientPacket packet)
        {
            SendPacket(packet, NetDeliveryMethod.ReliableUnordered, 1);
        }

        public void SendUnreliablePacket(TClientPacket packet)
        {
            SendPacket(packet, NetDeliveryMethod.Unreliable, 2);
        }

        public void StartReceiving()
        {
            client.RegisterReceivedCallback(new SendOrPostCallback(OnReceiveMessage));
        }

        private NetOutgoingMessage CreateMessageFromPacket(TClientPacket packet)
        {
            var bytes = PacketConverter.ToBytes(packet);
            var message = client.CreateMessage(bytes.Length);
            message.Write(bytes);

            return message;
        }
        private void SendPacket(TClientPacket packet, NetDeliveryMethod deliveryMethod, int sequenceChannel)
        {
            client.SendMessage(CreateMessageFromPacket(packet), deliveryMethod, sequenceChannel);
        }
        private void Reconnect()
        {
            var serverEndPoint = Connection.RemoteEndPoint; // Does this get nulled after disconnecting?
            Disconnect(ClientDisconnectReason.Reconnect.ToString());

            client.Connect(serverEndPoint);
        }
        private void CheckConnectionHealth(bool repair)
        {
            if (Connection != null)
            {
                if (repair)
                    Reconnect();
                else
                {
                    Disconnect(ClientDisconnectReason.LostConnection.ToString());
                    if (OnLostConnection != null)
                        OnLostConnection();
                }
            }
        }
        private void OnReceiveMessage(object peer)
        {
            incomingMessage = client.ReadMessage();
            switch (incomingMessage.MessageType)
            {
                case NetIncomingMessageType.StatusChanged:
                    if (Connection.Status != NetConnectionStatus.Connected)
                    {
                        Disconnect(ClientDisconnectReason.RemoteShutdown.ToString());
                        if (OnDisconnect != null)
                            OnDisconnect();
                    }
                    else if (Connection.Status == NetConnectionStatus.Connected)
                    {
                        if (OnConnect != null)
                            OnConnect();
                    }
                    break;

                case NetIncomingMessageType.Data:
                    var bytes = incomingMessage.ReadBytes(incomingMessage.LengthBytes);
                    var packet = PacketConverter.ToPacket<TServerPacket>(bytes);

                    if (OnReceivePacket != null)
                        OnReceivePacket(packet);
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

            client.Recycle(incomingMessage);
        }
    }
}
