﻿using Caseomatic.Net.Utility;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Linq;
using Caseomatic.Util;

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
        /// Called when the connection to the server is lost, noticed by a heartbeat. 
        /// </summary>
        public event OnConnectionLostHandler OnConnectionLost;

        public delegate void OnConnectionRepairedHandler();
        /// <summary>
        /// Called when the connection is dis- and reconnected to repair it.
        /// </summary>
        public event OnConnectionRepairedHandler OnConnectionRepaired;

        private Socket socket;
        private byte[] packetReceivingBuffer;
        private readonly int port;
        private object commModuleLock;
        private long isConnectionLost, isConnectionRepaired;
        private Ping repairPing;

        private bool IsConnectionLost
        {
            get
            {
                return Interlocked.Read(ref isConnectionLost) == 0;
            }
            set
            {
                Interlocked.Exchange(ref isConnectionLost, value ? 0 : 1);
            }
        }
        private bool IsConnectionRepaired
        {
            get
            {
                return Interlocked.Read(ref isConnectionRepaired) == 0;
            }
            set
            {
                Interlocked.Exchange(ref isConnectionRepaired, value ? 0 : 1);
            }
        }

        private readonly ConcurrentStack<TServerPacket> receivePacketsSynchronizationStack;

        private CommunicationModule<TServerPacket, TClientPacket> communicationModule;
        /// <summary>
        /// The communication module that controls the conversion from bytes to packets and vice versa.
        /// </summary>
        public CommunicationModule<TServerPacket, TClientPacket> CommunicationModule
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

        private long isConnected;
        /// <summary>
        /// Returns if the client is currently connected. Updated by the Connect and Disconnect methods.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                return Interlocked.Read(ref isConnected) == 0;
            }
            set
            {
                Interlocked.Exchange(ref isConnected, value ? 0 : 1);
            }
        }

        /// <summary>
        /// Endpoint of the server this client is connected to.
        /// </summary>
        public IPEndPoint ServerEndPoint
        {
            get { return (IPEndPoint)socket.RemoteEndPoint; }
        }

        private bool activeConnectionRepair = true;
        /// <summary>
        /// Repairs the socket connection automatically if the connection malfunctions but server shows a heartbeat. True by default.
        /// </summary>
        public bool ActiveConnectionRepair
        {
            get { return activeConnectionRepair; }
            set { activeConnectionRepair = value; }
        }

        public Client(int port)
        {
            this.port = port;

            receivePacketsSynchronizationStack = new ConcurrentStack<TServerPacket>();
            communicationModule = new DefaultCommunicationModule<TServerPacket, TClientPacket>();
            commModuleLock = new object();

            repairPing = new Ping();
            repairPing.PingCompleted += OnRepairPingCompleted;
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
            if (!IsConnected)
                OnConnect(serverEndPoint);
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
            if (IsConnected)
                OnDisconnect();
        }

        /// <summary>
        /// Invokes all OnReceivePacket events of all packets that have been asynchronously received. 
        /// Invokes all OnClientConnectionLost events that have been asynchronously ordered.
        /// </summary>
        public virtual void FireEvents()
        {
            if (!IsConnected)
                return;

            if (OnReceivePacket != null)
            {
                var events = receivePacketsSynchronizationStack.PopAll();
                for (int i = 0; i < events.Length; i++)
                    OnReceivePacket(events[i]);
            }

            if (IsConnectionLost && OnConnectionLost != null)
            {
                OnConnectionLost();
                IsConnectionLost = false;
            }
            if (IsConnectionRepaired && OnConnectionRepaired != null)
            {
                OnConnectionRepaired();
                IsConnectionRepaired = false;
            }
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

                IsConnected = true;
                StartReceivePacketLoop();
            }
            catch (SocketException ex)
            {
                Console.WriteLine("Connecting to " + serverEndPoint.ToString() +
                    " resulted in a problem: " + ex.SocketErrorCode + "\n" + ex.Message);

                HeartbeatConnection(true);
            }
        }

        protected virtual void OnDisconnect()
        {
            try
            {
                IsConnected = false;
                socket.Close(); // Or use socket.Disconnect(true) instead of close/null?

                Console.WriteLine("Disconnected from the server");
            }
            catch (SocketException ex)
            {
                Console.WriteLine("Disconnecting resulted in a problem: " + ex.SocketErrorCode);
            }
        }

        /// <summary>
        /// Heartbeats the connection on sending, receiving and its availability.
        /// </summary>
        /// <param name="repairIfBroken">Reconnects to the server if the connection is invalid.</param>
        /// <returns>Returns if the heartbeat has been successful, if repairing is activated, returns if the repair has been successful.</returns>
        public bool HeartbeatConnection(bool repairIfBroken)
        {
            try
            {
                if (IsConnected)
                {
                    Console.WriteLine("Heartbeating server...");
                    bool isReallyConnected = socket.IsConnectionValid();

                    if (!isReallyConnected)
                    {
                        Console.WriteLine("The server shows no heartbeat" + (repairIfBroken ?
                            ", trying to repair connection." : ", disconnecting."));

                        if (repairIfBroken && activeConnectionRepair)
                            RepairConnection();
                        else
                            DisconnectLost();
                    }

                    return isReallyConnected;
                }
                else
                    return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Heartbeating produced an error: " + ex.ToString());
                return false;
            }
        }

        /// <summary>
        /// Sends a packet to the server.
        /// </summary>
        /// <param name="packet">The packet that shall be sent.</param>
        public void SendPacket(TClientPacket packet)
        {
            if (!IsConnected)
            {
                Console.WriteLine("Cannot send a packet if the client is not connected.");
            }
            else
            {
                try
                {
                    byte[] bytes;
                    lock (commModuleLock)
                        bytes = communicationModule.ConvertSend(packet);

                    using (var sockAsyncArgs = new SocketAsyncEventArgs())
                    {
                        sockAsyncArgs.SetBuffer(bytes, 0, bytes.Length);
                        EventHandler<SocketAsyncEventArgs> completedExpr = (object sender, SocketAsyncEventArgs e) =>
                        {
                            if (e.SocketError == SocketError.Success)
                            {
                                Console.WriteLine("Sent a packet to the server: " + packet.GetType().Name);
                            }
                            else
                            {
                                Console.WriteLine("Sending to the server did not work: " + e.SocketError);
                                HeartbeatConnection(true);
                            }
                        };
                        sockAsyncArgs.Completed += completedExpr;

                        var synchronous = socket.SendAsync(sockAsyncArgs);
                        if (synchronous)
                            completedExpr(this, sockAsyncArgs);
                    }

                    //socket.BeginSend(bytes, 0, bytes.Length, SocketFlags.None, (result) =>
                    //{
                    //    var sentBytes = socket.EndSend(result);
                    //    //if (error != SocketError.Success)
                    //    //{
                    //    //    if (sentBytes == 0)
                    //    //    {
                    //    //        Console.WriteLine("Sent 0 bytes to server: Connection dropped on serverside or malfunctioning." +
                    //    //            " (" + error  + ")");
                    //    //        HeartbeatConnection(false);
                    //    //    }
                    //    //    else
                    //    //    {
                    //    //        Console.WriteLine("An error occured while sending: " + error);
                    //    //        HeartbeatConnection(true);
                    //    //    }
                    //    //}
                    //}, null);
                }
                catch (SocketException ex) // TODO: Improve exception-catching
                {
                    Console.WriteLine("Sending to the server resulted in a problem: " + ex.SocketErrorCode +
                        "\n" + ex.Message);
                    HeartbeatConnection(true);
                }
            }
        }
        
        protected void DisconnectLost()
        {
            IsConnectionLost = true;
            Disconnect();
        }

        private void StartReceivePacketLoop()
        {
            if (!IsConnected)
            {
                Console.WriteLine("Tried receiving asynchronously without being connected.");
            }
            else
            {
                try
                {
                    using (var sockAsyncArgs = new SocketAsyncEventArgs())
                    {
                        sockAsyncArgs.Completed += (sender, e) =>
                        {
                            if (e.SocketError == SocketError.Success)
                            {
                                TServerPacket packet;
                                lock (commModuleLock)
                                    packet = communicationModule.ConvertReceive(e.Buffer);

                                receivePacketsSynchronizationStack.Push(packet);
                                Console.WriteLine("Received a packet: " + packet.GetType().Name);
                            }
                            else if (e.SocketError != SocketError.TimedOut || e.SocketError == SocketError.Interrupted)
                            {
                                Console.WriteLine("Receiving from the server did not work: " + e.SocketError);
                                HeartbeatConnection(true);
                            }
                        };

                        socket.ReceiveAsync(sockAsyncArgs);
                    }

                    //socket.BeginReceive(packetReceivingBuffer, 0, packetReceivingBuffer.Length, SocketFlags.None,
                    //    (result) =>
                    //    {
                    //        try
                    //        {
                    //            var receivedBytes = socket.EndReceive(result);

                    //            if (receivedBytes == 0)
                    //            {
                    //                Console.WriteLine("Received 0 bytes from server: Connection dropped. Buffered received-bytes length: " + packetReceivingBuffer.Length);
                    //                DisconnectLost();
                    //            }
                    //            else
                    //            {
                    //                TServerPacket packet;
                    //                lock (commModuleLock)
                    //                    packet = communicationModule.ConvertReceive(packetReceivingBuffer);

                    //                receivePacketsSynchronizationStack.Push(packet);
                    //            }
                    //        }
                    //        catch (Exception ex)
                    //        {
                    //            Console.WriteLine("Processing a received packet produced an exception: " + ex.Message);
                    //        }
                    //        }, null);

                    StartReceivePacketLoop();
                }
                catch (SocketException ex)
                    when(ex.SocketErrorCode != SocketError.Interrupted && ex.SocketErrorCode != SocketError.TimedOut) // TODO: Improve exception-catching
                {
                    Console.WriteLine("Receiving asynchronously produced an exception: " + ex.Message);
                    HeartbeatConnection(true);
                }
                catch(StackOverflowException ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        /// <summary>
        /// Repairs the connection by disconnecting, sending a ping and reconnecting under the same circumstances and heartbeating to check if it worked.
        /// </summary>
        private void RepairConnection()
        {
            var remoteEndPoint = ServerEndPoint;

            Disconnect();
            repairPing.SendAsync(remoteEndPoint.Address, 1000, remoteEndPoint);
        }
        private void OnRepairPingCompleted(object sender, PingCompletedEventArgs e)
        {
            if (e.Reply.Status == IPStatus.Success)
            {
                Connect((IPEndPoint)e.UserState);
                if (HeartbeatConnection(false))
                {
                    Console.WriteLine("Reconnected to the server.");
                    IsConnectionRepaired = true;
                }
                else
                    DisconnectLost();
            }
            else
            {
                Console.WriteLine("IP not pingable. Result: " + e.Reply.Status + ", dropping off.");
                IsConnectionLost = true;
            }
        }
    }
}
