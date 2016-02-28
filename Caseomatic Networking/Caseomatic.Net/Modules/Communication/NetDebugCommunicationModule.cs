using Caseomatic.Net.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace Caseomatic.Net
{
    public class NetDebugCommunicationModule<TReceivePacket, TSendPacket>
        : ICommunicationModule<TReceivePacket, TSendPacket> where TReceivePacket : IPacket where TSendPacket : IPacket
    {
        private static Random random = new Random(DateTime.Now.Millisecond);
        private readonly ICommunicationModule<TReceivePacket, TSendPacket> underlyingCommModule;

        private NetDebugProperties properties;
        public NetDebugProperties Properties
        {
            get { return properties; }
        }

        private int receivedBytes, sentBytes;
        public int ReceivedBytes
        {
            get { return receivedBytes; }
        }
        public int SentBytes
        {
            get { return sentBytes; }
        }

        private LineGraph serializationGraph, deserializationGraph, sentPacketsSizeGraph, receivedPacketsSizeGraph;
        public LineGraph SerializationGraph
        {
            get { return serializationGraph; }
        }
        public LineGraph DeserializationGraph
        {
            get { return deserializationGraph; }
        }
        public LineGraph SentPacketsSizeGraph
        {
            get { return sentPacketsSizeGraph; }
        }
        public LineGraph ReceivedBytesPacketsSizeGraph
        {
            get { return receivedPacketsSizeGraph; }
        }

        public NetDebugCommunicationModule(ICommunicationModule<TReceivePacket, TSendPacket> underlyingCommModule)
        {
            this.underlyingCommModule = underlyingCommModule;
            properties = new NetDebugProperties();

            serializationGraph = new LineGraph(true);
            deserializationGraph = new LineGraph(true);
            sentPacketsSizeGraph = new LineGraph(true);
            receivedPacketsSizeGraph = new LineGraph(false);
        }

        public TReceivePacket ConvertReceive(byte[] bytes)
        {
            var stopwatch = Stopwatch.StartNew();
            bytes = ApplyReceiveProperties(bytes);
            stopwatch.Stop();

            if (bytes == null)
                return default(TReceivePacket);
            deserializationGraph.Add(stopwatch.ElapsedMilliseconds);

            receivedBytes += bytes.Length;
            receivedPacketsSizeGraph.Add(bytes.Length);

            var packet = underlyingCommModule.ConvertReceive(bytes);
            Log("Received packet of type " + packet.GetType().FullName +
                "\nByte size: " + bytes.Length + ", Deserialization time: " + stopwatch.ElapsedMilliseconds);

            return packet;
        }

        public byte[] ConvertSend(TSendPacket packet)
        {
            packet = ApplySendProperties(packet);

            var stopwatch = Stopwatch.StartNew();
            var bytes = underlyingCommModule.ConvertSend(packet);
            stopwatch.Stop();
            serializationGraph.Add(stopwatch.ElapsedMilliseconds);

            sentBytes += bytes.Length;
            sentPacketsSizeGraph.Add(bytes.Length);

            Log("Sending packet of type " + packet.GetType().FullName +
                "\nByte size: " + bytes.Length + ", Serialization time: " + stopwatch.ElapsedMilliseconds);
            return bytes;
        }

        public void ClearTemp()
        {
            receivedBytes = sentBytes = 0;
        }

        private byte[] ApplyReceiveProperties(byte[] bytes)
        {
            if (properties.simulateLag)
                Thread.Sleep(properties.lagIntensitivity);

            if (properties.simulatePacketDrop)
            {
                var chance = random.Next(0, 100);
                if (chance <= properties.packetDropIntensitivityPercentage)
                    return null;
            }

            return bytes;
        }
        private T ApplySendProperties<T>(T packet) where T : IPacket
        {
            return packet;
        }

        private void Log(string text)
        {
            if (properties.fullLog)
                Console.WriteLine(text);
        }
    }

    public class NetDebugProperties
    {
        public bool fullLog;

        public bool simulateLag;
        public int lagIntensitivity = 100;

        public bool simulateDuplicates;
        public int duplicateIntensitivityPercentage = 50;

        public bool simulatePacketDrop;
        public int packetDropIntensitivityPercentage = 20;
    }
}
