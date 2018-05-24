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
    public static class NetPeerManager
    {
        /// <summary>
        /// Peer List (thread safe)
        /// </summary>
        public static ConcurrentDictionary<NetPeer, Socket> Peers = new ConcurrentDictionary<NetPeer, Socket>();

        public static Thread NetworkUpdateThread = null;
        public static Object InitThreadLock = new Object();

        /// <summary>
        /// Wait for network thread exit
        /// </summary>
        public static void WaitForExit()
        {
            NetworkUpdateThread?.Join();
        }

        /// <summary>
        /// Concurrent add peer task
        /// </summary>
        /// <param name="peer"></param>
        public static void AddPeer(NetPeer peer)
        {
            if (!Peers.TryAdd(peer, peer.Socket))
            {
                Task.Run(() => AddPeerTask(peer));
            }
        }

        /// <summary>
        /// Concurrent remove peer task
        /// </summary>
        /// <param name="peer"></param>
        public static void RemovePeer(NetPeer peer)
        {
            if (Peers.TryRemove(peer, out _))
            {
                Task.Run(() => RemovePeerTask(peer));
            }
        }

        // pattern is, if you can't do it now, do it later.


        /// <summary>
        /// Add peer task, will add when succedes
        /// </summary>
        /// <param name="peer"></param>
        private static async Task AddPeerTask(NetPeer peer)
        {
            // return immediately
            if (Peers == null) return;

            // wait for add to work
            while (!Peers.TryAdd(peer, peer.Socket))
            {
                await Task.Delay(1);

                // wait until add succedes
            }
        }


        /// <summary>
        /// Remove task, will add when succedes
        /// </summary>
        /// <param name="peer"></param>
        private static async Task RemovePeerTask(NetPeer peer)
        {
            // return immediately
            if (Peers == null) return;

            // wait for remove to work
            while (!Peers.TryRemove(peer, out _))
            {
                await Task.Delay(1);

                // wait until add succedes
            }
        }

        /// <summary>
        /// Start the network thread
        /// </summary>
        public static void StartNetworkThread()
        {
            lock (InitThreadLock)
            {
                // thread shutdown behaviour is, it will shut down when netpeer's have been shut down, if it suddenly gets a new netpeer to work with
                // it initialises a new thread for it to use
                // if the thread has died, or the thread hasn't been started, overwrite it.
                if (NetworkUpdateThread == null || NetworkUpdateThread.IsAlive == false)
                {
                    // extra alloc, just in case it's a dead thread
                    NetworkUpdateThread = null;

                    // start network thread
                    NetworkUpdateThread = new Thread(NetworkLoop)
                    {
                        Name = "Lidgren.Network Thread"
                    };

                    //m_networkThread.IsBackground = true;
                    NetworkUpdateThread.Start();
                }
            }
        }

        /// <summary>
        /// Singular network update thread
        /// This must be static, because otherwise people might try to use peer in a local thread context which is incorrect.
        /// </summary>
        private static void NetworkLoop()
        {
            bool running = true;
            bool hadElements = false;
            while (running)
            {
                // if our peer manager had elements and they're now gone, time to shutdown the thread, if we have no peers left
                if (hadElements && running)
                {
                    if (Peers.Count == 0)
                    {
                        running = false;
                        return; // time to exit, no peers exist anymore.
                    }
                }

                foreach (var kvp in Peers)
                {
                    hadElements = true;
                    var peer = kvp.Key;
                    if (peer.Status == NetPeerStatus.Running)
                    {
                        peer.PeerUpdate();
                    }

                    if (peer.Status == NetPeerStatus.ShutdownRequested)
                    {
                        try
                        {
                            // shutdown peer
                            peer.ExecutePeerShutdown();
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                            throw;
                        }
                    }
                }

                // no sockets to poll prep for exit condition
                if (Peers.Count == 0) continue;

                // Socket.Select is faster than running Socket.Poll foreach socket.
                var sockets = Peers.Values.ToList();
                Socket.Select(sockets, null, null, 1000);

                // only update selected sockets
                foreach (var socket in sockets)
                {
                    // retrieve socket peer to process data on it, as data is pending on it's connection
                    var pair = Peers.SingleOrDefault(kvp => kvp.Value == socket);

                    // potential place to pass to socket Task?
                    pair.Key.SocketDataHandler();
                }

            }
        }
    }
}