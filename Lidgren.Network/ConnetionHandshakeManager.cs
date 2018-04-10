using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Security.Cryptography;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
#if !__NOIPENDPOINT__
using NetEndPoint = System.Net.IPEndPoint;

#endif

namespace Lidgren.Network
{
    /// <summary>
    /// Handshake management system
    /// Used to handle the list of handshakes waiting to be processed.
    /// </summary>
    public class ConnectionHandshakeManager
    {
        public ConcurrentDictionary<NetEndPoint, NetConnection> Handshakes =
            new ConcurrentDictionary<IPEndPoint, NetConnection>();

        public void AddHandshake(NetEndPoint endPoint, NetConnection connection)
        {
            if (!Handshakes.TryAdd(endPoint, connection))
            {
                Task.Run(() => AddHandshakeTask(endPoint, connection));
            }
        }

        public void RemoveHandshake(NetEndPoint endPoint)
        {
            if (!Handshakes.TryRemove(endPoint, out _))
            {
                Task.Run(() => RemoveHandshakeTask(endPoint));
            }
        }

        /// <summary>
        /// Add peer task, will add when succedes
        /// </summary>
        /// <param name="peer"></param>
        private async Task AddHandshakeTask(NetEndPoint endPoint, NetConnection connection)
        {
            // return immediately
            if (Handshakes == null) return;

            // wait for add to work
            while (!Handshakes.TryAdd(endPoint, connection))
            {
                await Task.Delay(1);

                // wait until add succedes
            }
        }

        /// <summary>
        /// Add peer task, will add when succedes
        /// </summary>
        /// <param name="peer"></param>
        private async Task RemoveHandshakeTask(NetEndPoint endPoint)
        {
            // return immediately
            if (Handshakes == null) return;

            // wait for add to work
            while (!Handshakes.TryRemove(endPoint, out _))
            {
                await Task.Delay(1);
                if (!Handshakes.ContainsKey(endPoint)) return;
                // wait until add succedes
            }
        }
    }
}
