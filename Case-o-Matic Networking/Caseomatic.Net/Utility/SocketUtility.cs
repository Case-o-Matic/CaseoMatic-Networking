using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;

namespace Caseomatic.Net
{
    public static class SocketUtility
    {
        public static bool IsConnectionValid(this Socket sock)
        {
            bool pollRead = sock.Poll(1000, SelectMode.SelectRead);
            bool pollError = sock.Poll(1000, SelectMode.SelectError);
            bool availableZero = sock.Available == 0;
            bool initializedDisonnected = !sock.Connected;

            return !(pollRead && pollError && availableZero && initializedDisonnected);
        }

        public static void ConfigureInitialSocket(this Socket sock)
        {
            sock.LingerState = new LingerOption(true, 3);
            sock.NoDelay = true;

            sock.SendBufferSize = 16384;
            sock.SendTimeout = 5000;

            sock.ReceiveBufferSize = 16384;
            sock.ReceiveTimeout = 5000;
        }
    }
}
