using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Caseomatic.Net
{
    internal enum ClientDisconnectReason
    {
        Reconnect,
        LostConnection,
        RemoteShutdown,
        Manual
    }
}
