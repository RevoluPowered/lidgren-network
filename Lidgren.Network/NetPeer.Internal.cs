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

    public class ConnectionHandshakeManager
    {
        public ConcurrentDictionary<NetEndPoint, NetConnection> Handshakes = new ConcurrentDictionary<IPEndPoint, NetConnection>();

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

    public partial class NetPeer
    {
        private NetPeerStatus m_status = NetPeerStatus.NotRunning;
        public ConnectionHandshakeManager _handshakeManager = new ConnectionHandshakeManager();
        private Socket m_socket = null;
        internal byte[] m_sendBuffer;
        internal byte[] m_receiveBuffer;
        internal NetIncomingMessage m_readHelperMessage;
        private EndPoint m_senderRemote;
        private object m_initializeLock = new object();
        private uint m_frameCounter;
        private double m_lastHeartbeat;
        private double m_lastSocketBind = float.MinValue;
        private NetUPnP m_upnp;
        internal bool m_needFlushSendQueue;

        internal readonly NetPeerConfiguration m_configuration;
        private readonly NetQueue<NetIncomingMessage> m_releasedIncomingMessages;
        internal readonly NetQueue<NetTuple<NetEndPoint, NetOutgoingMessage>> m_unsentUnconnectedMessages;


        internal readonly NetPeerStatistics m_statistics;
        internal long m_uniqueIdentifier;
        internal bool m_executeFlushSendQueue;

        private AutoResetEvent m_messageReceivedEvent;
        private List<NetTuple<SynchronizationContext, SendOrPostCallback>> m_receiveCallbacks;

        /// <summary>
        /// Gets the socket, if Start() has been called
        /// </summary>
        public Socket Socket
        {
            get { return m_socket; }
        }

        /// <summary>
        /// Call this to register a callback for when a new message arrives
        /// </summary>
        public void RegisterReceivedCallback(SendOrPostCallback callback, SynchronizationContext syncContext = null)
        {
            if (syncContext == null)
                syncContext = SynchronizationContext.Current;
            if (syncContext == null)
                throw new NetException("Need a SynchronizationContext to register callback on correct thread!");
            if (m_receiveCallbacks == null)
                m_receiveCallbacks = new List<NetTuple<SynchronizationContext, SendOrPostCallback>>();
            m_receiveCallbacks.Add(new NetTuple<SynchronizationContext, SendOrPostCallback>(syncContext, callback));
        }

        /// <summary>
        /// Call this to unregister a callback, but remember to do it in the same synchronization context!
        /// </summary>
        public void UnregisterReceivedCallback(SendOrPostCallback callback)
        {
            if (m_receiveCallbacks == null)
                return;

            // remove all callbacks regardless of sync context
            m_receiveCallbacks.RemoveAll(tuple => tuple.Item2.Equals(callback));

            if (m_receiveCallbacks.Count < 1)
                m_receiveCallbacks = null;
        }

        internal void ReleaseMessage(NetIncomingMessage msg)
        {
            NetException.Assert(msg.m_incomingMessageType != NetIncomingMessageType.Error);

            if (msg.m_isFragment)
            {
                HandleReleasedFragment(msg);
                return;
            }

            m_releasedIncomingMessages.Enqueue(msg);

            if (m_messageReceivedEvent != null)
                m_messageReceivedEvent.Set();

            if (m_receiveCallbacks != null)
            {
                foreach (var tuple in m_receiveCallbacks)
                {
                    try
                    {
                        tuple.Item1.Post(tuple.Item2, this);
                    }
                    catch (Exception ex)
                    {
                        LogWarning("Receive callback exception:" + ex);
                    }
                }
            }
        }

        /// <summary>
        /// Bind socket - with rebind parameter for when you need to rebind a socket
        /// </summary>
        /// <param name="rebind"></param>
        private void BindSocket( bool rebind)
        {
            double now = NetTime.Now;
            if (now - m_lastSocketBind < 1.0)
            {
                LogDebug("Suppressed socket rebind; last bound " + (now - m_lastSocketBind) + " seconds ago");
                return; // only allow rebind once every second
            }

            m_lastSocketBind = now;

            if (m_socket == null)
            {
                m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            }
            
            // todo: write unit test which executes rebinding of the sockets on android and ios
            if (rebind)
            {
                m_socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, (int)1);
            }
            
            // Register this peer to our manager
            NetPeerManager.AddPeer(this);

            m_socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, (int) 1);

            m_socket.ReceiveBufferSize = m_configuration.ReceiveBufferSize;
            m_socket.SendBufferSize = m_configuration.SendBufferSize;
            m_socket.Blocking = false;

            var ep = (EndPoint) new NetEndPoint(m_configuration.LocalAddress, rebind ? m_listenPort : m_configuration.Port);
            m_socket.Bind(ep);

            // try catch only works on linux not osx
            try
            {
                // this is not supported in mono / mac or linux yet.
                if (Environment.OSVersion.Platform != PlatformID.Unix)
                {
                    const uint IOC_IN = 0x80000000;
                    const uint IOC_VENDOR = 0x18000000;
                    uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
                    m_socket.IOControl((int) SIO_UDP_CONNRESET, new byte[] {Convert.ToByte(false)}, null);
                }
                else
                {
                    LogDebug("Platform doesn't support SIO_UDP_CONNRESET");
                }
            }
            catch (System.Exception e)
            {
                LogDebug("Platform doesn't support SIO_UDP_CONNRESET");
                // this will be thrown on linux but not mac if it doesn't exist.
                // ignore; SIO_UDP_CONNRESET not supported on this platform
            }

            var boundEp = m_socket.LocalEndPoint as NetEndPoint;
            LogDebug("Socket bound to " + boundEp + ": " + m_socket.IsBound);
            m_listenPort = boundEp.Port;
        }

        private void InitializeNetwork()
        {
            lock (m_initializeLock)
            {
                // make sure this is properly set up again, if required.

                m_configuration.Lock();

                if (m_status == NetPeerStatus.Running)
                    return;

                if (m_configuration.m_enableUPnP)
                    m_upnp = new NetUPnP(this);

                InitializePools();

                m_releasedIncomingMessages.Clear();
                m_unsentUnconnectedMessages.Clear();

                _handshakeManager.Handshakes.Clear();

                // bind to socket
                BindSocket(false);

                m_receiveBuffer = new byte[m_configuration.ReceiveBufferSize];
                m_sendBuffer = new byte[m_configuration.SendBufferSize];
                m_readHelperMessage = new NetIncomingMessage(NetIncomingMessageType.Error);
                m_readHelperMessage.m_data = m_receiveBuffer;

                byte[] macBytes = NetUtility.GetMacAddressBytes();

                var boundEp = m_socket.LocalEndPoint as NetEndPoint;
                byte[] epBytes = BitConverter.GetBytes(boundEp.GetHashCode());
                byte[] combined = new byte[epBytes.Length + macBytes.Length];
                Array.Copy(epBytes, 0, combined, 0, epBytes.Length);
                Array.Copy(macBytes, 0, combined, epBytes.Length, macBytes.Length);
                m_uniqueIdentifier = BitConverter.ToInt64(NetUtility.ComputeSHAHash(combined), 0);

                m_status = NetPeerStatus.Running;
            }
        }

        public void ExecutePeerShutdown()
        {
            Console.WriteLine("Network shutdown enter");
            // disconnect and make one final heartbeat
            var list = new List<NetConnection>(_handshakeManager.Handshakes.Count + m_connections.Count);
            lock (m_connections)
            {
                foreach (var conn in m_connections)
                    if (conn != null)
                        list.Add(conn);
            }
            
            foreach (var hs in _handshakeManager.Handshakes.Values)
                if (hs != null && list.Contains(hs) == false)
                    list.Add(hs);
            

            // shut down connections
            foreach (NetConnection conn in list)
                conn.Shutdown(m_shutdownReason);

            FlushDelayedPackets();

            // one final heartbeat, will send stuff and do disconnect
            PeerUpdate();

            NetUtility.Sleep(10);

            lock (m_initializeLock)
            {
                try
                {
                    if (m_socket != null)
                    {
                        // shutdown socket send and recieve handlers.
                        //m_socket.Shutdown(SocketShutdown.Both);

                        // close connection, if present
                        m_socket.Close(2);
                    }
                }
                catch (Exception ex)
                {
                    LogDebug("socket shutdown method exception: " + ex.ToString());
                    throw;
                }
                finally
                {
                    NetPeerManager.RemovePeer(this);
                    // wake up any threads waiting for server shutdown
                    m_messageReceivedEvent?.Set();

                    m_lastSocketBind = float.MinValue;
                    m_receiveBuffer = null;
                    m_sendBuffer = null;
                    m_unsentUnconnectedMessages?.Clear();
                    m_connections?.Clear();
                    m_connectionLookup?.Clear();
                    _handshakeManager.Handshakes?.Clear();
                    _handshakeManager = null;

                    m_status = NetPeerStatus.NotRunning;
                    LogDebug("Shutdown complete");
                    Console.WriteLine("Shutdown network peer properly");
                }
            }

            return;
        }

        private void ProcessConnectionStateChanges()
        {
        }

        private void FlushNetwork()
        {
            // update m_executeFlushSendQueue
            if (m_configuration.m_autoFlushSendQueue && m_needFlushSendQueue)
            {
                m_executeFlushSendQueue = true;
                m_needFlushSendQueue =
                    false; // a race condition to this variable will simply result in a single superfluous call to FlushSendQueue()
            }
        }


        /// <summary>
        /// Process handshake messages / client connected, client disconnected
        /// </summary>
        /// <param name="delta"></param>
        /// <param name="now"></param>
        /// <param name="maxConnectionHeartbeatsPerSecond"></param>
        private void ProcessHandshakes(double delta, double now, double maxConnectionHeartbeatsPerSecond)
        {
            // do handshake heartbeats
            if ((m_frameCounter % 3) == 0)
            {
                foreach (var kvp in _handshakeManager.Handshakes)
                {
                    NetConnection conn = kvp.Value as NetConnection;
                    /*#if DEBUG
                                            // sanity check
                                            if (kvp.Key != kvp.Key)
                                                LogWarning("Sanity fail! Connection in handshake list under wrong key!");
                    #endif*/
                    conn.UnconnectedHeartbeat(now);
                    if (conn.m_status == NetConnectionStatus.Connected ||
                        conn.m_status == NetConnectionStatus.Disconnected)
                    {
                        /*#if DEBUG
                                                    // sanity check
                                                    if (conn.m_status == NetConnectionStatus.Disconnected && m_handshakes.ContainsKey(conn.RemoteEndPoint))
                                                    {
                                                        LogWarning("Sanity fail! Handshakes list contained disconnected connection!");
                                                        m_handshakes.Remove(conn.RemoteEndPoint);
                                                    }
                        #endif*/
                        break; // collection has been modified
                    }
                }
            }
        }

        private void ProcessConnectionUpdates(double now)
        {
            // do connection heartbeats
            lock (m_connections)
            {
                for (int i = m_connections.Count - 1; i >= 0; i--)
                {
                    var conn = m_connections[i];
                    conn.Heartbeat(now, m_frameCounter);
                    if (conn.m_status == NetConnectionStatus.Disconnected)
                    {
                        //
                        // remove connection
                        //
                        m_connections.RemoveAt(i);
                        m_connectionLookup.Remove(conn.RemoteEndPoint);
                    }
                }
            }

            m_executeFlushSendQueue = false;

            // send unsent unconnected messages
            NetTuple<NetEndPoint, NetOutgoingMessage> unsent;
            while (m_unsentUnconnectedMessages.TryDequeue(out unsent))
            {
                NetOutgoingMessage om = unsent.Item2;

                int len = om.Encode(m_sendBuffer, 0, 0);

                Interlocked.Decrement(ref om.m_recyclingCount);
                if (om.m_recyclingCount <= 0)
                    Recycle(om);

                bool connReset;
                SendPacket(len, unsent.Item1, 1, out connReset);
            }
        }


        public void PeerUpdate()
        {
            int maxCHBpS = 1250;
            if (maxCHBpS < 250)
                maxCHBpS = 250;

            double now = NetTime.Now;
            double delta = now - m_lastHeartbeat;
            m_frameCounter++;
            m_lastHeartbeat = now;

            if (delta > (1.0 / maxCHBpS) || delta < 0.0)
            {
                ProcessHandshakes(delta, now, maxCHBpS);
                FlushNetwork();
                ProcessConnectionUpdates(now);

                /*#if DEBUG
                SendDelayedPackets();
                #endif*/
            }

            m_upnp?.CheckForDiscoveryTimeout();
        }


        /// <summary>
        /// Called when the socket has data to process
        /// </summary>
        /// <param name="now"></param>
        /// <param name="peer"></param>
        public void SocketDataHandler()
        {
            // retrieve new now time
            double now = NetTime.Now;

            do
            {
                int bytesReceived = 0;
                try
                {
                    bytesReceived = m_socket.ReceiveFrom(m_receiveBuffer, 0, m_receiveBuffer.Length,
                        SocketFlags.None, ref m_senderRemote);
                }
                catch (SocketException sx)
                {
                    switch (sx.SocketErrorCode)
                    {
                        case SocketError.ConnectionReset:
                            // connection reset by peer, aka connection forcibly closed aka "ICMP port unreachable"
                            // we should shut down the connection; but m_senderRemote seemingly cannot be trusted, so which connection should we shut down?!
                            // So, what to do?
                            LogWarning("ConnectionReset");
                            return;

                        case SocketError.NotConnected:
                            // socket is unbound; try to rebind it (happens on mobile when process goes to sleep)
                            BindSocket(true);
                            return;

                        default:
                            LogWarning("Socket exception: " + sx.ToString());
                            return;
                    }
                }

                if (bytesReceived < NetConstants.HeaderByteSize)
                {
                    return;
                }
                //LogVerbose("Received " + bytesReceived + " bytes");

                var ipsender = (NetEndPoint) m_senderRemote;

                if (m_upnp != null && now < m_upnp.m_discoveryResponseDeadline && bytesReceived > 32)
                {
                    // is this an UPnP response?
                    string resp = System.Text.Encoding.UTF8.GetString(m_receiveBuffer, 0, bytesReceived);
                    if (resp.Contains("upnp:rootdevice") || resp.Contains("UPnP/1.0"))
                    {
                        try
                        {
                            resp = resp.Substring(resp.ToLower().IndexOf("location:") + 9);
                            resp = resp.Substring(0, resp.IndexOf("\r")).Trim();
                            m_upnp.ExtractServiceUrl(resp);
                            return;
                        }
                        catch (Exception ex)
                        {
                            LogDebug("Failed to parse UPnP response: " + ex.ToString());
                            // don't try to parse this packet further
                            return;
                        }
                    }
                }

                NetConnection sender = null;
                m_connectionLookup.TryGetValue(ipsender, out sender);

                //
                // parse packet into messages
                //
                int numMessages = 0;
                int numFragments = 0;
                int ptr = 0;
                while ((bytesReceived - ptr) >= NetConstants.HeaderByteSize)
                {
                    // decode header
                    //  8 bits - NetMessageType
                    //  1 bit  - Fragment?
                    // 15 bits - Sequence number
                    // 16 bits - Payload length in bits

                    numMessages++;

                    NetMessageType tp = (NetMessageType) m_receiveBuffer[ptr++];

                    byte low = m_receiveBuffer[ptr++];
                    byte high = m_receiveBuffer[ptr++];

                    bool isFragment = ((low & 1) == 1);
                    ushort sequenceNumber = (ushort) ((low >> 1) | (((int) high) << 7));

                    if (isFragment)
                        numFragments++;

                    ushort payloadBitLength = (ushort) (m_receiveBuffer[ptr++] | (m_receiveBuffer[ptr++] << 8));
                    int payloadByteLength = NetUtility.BytesToHoldBits(payloadBitLength);

                    if (bytesReceived - ptr < payloadByteLength)
                    {
                        LogWarning("Malformed packet; stated payload length " + payloadByteLength +
                                   ", remaining bytes " + (bytesReceived - ptr));
                        return;
                    }

                    if (tp >= NetMessageType.Unused1 && tp <= NetMessageType.Unused29)
                    {
                        ThrowOrLog("Unexpected NetMessageType: " + tp);
                        return;
                    }

                    try
                    {
                        if (tp >= NetMessageType.LibraryError)
                        {
                            if (sender != null)
                                sender.ReceivedLibraryMessage(tp, ptr, payloadByteLength);
                            else
                                ReceivedUnconnectedLibraryMessage(now, ipsender, tp, ptr, payloadByteLength);
                        }
                        else
                        {
                            if (sender == null &&
                                !m_configuration.IsMessageTypeEnabled(NetIncomingMessageType.UnconnectedData))
                            {
                                return; // dropping unconnected message since it's not enabled
                            }

                            NetIncomingMessage msg =
                                CreateIncomingMessage(NetIncomingMessageType.Data, payloadByteLength);
                            msg.m_isFragment = isFragment;
                            msg.m_receiveTime = now;
                            msg.m_sequenceNumber = sequenceNumber;
                            msg.m_receivedMessageType = tp;
                            msg.m_senderConnection = sender;
                            msg.m_senderEndPoint = ipsender;
                            msg.m_bitLength = payloadBitLength;

                            Buffer.BlockCopy(m_receiveBuffer, ptr, msg.m_data, 0, payloadByteLength);
                            if (sender != null)
                            {
                                if (tp == NetMessageType.Unconnected)
                                {
                                    // We're connected; but we can still send unconnected messages to this peer
                                    msg.m_incomingMessageType = NetIncomingMessageType.UnconnectedData;
                                    ReleaseMessage(msg);
                                }
                                else
                                {
                                    // connected application (non-library) message
                                    sender.ReceivedMessage(msg);
                                }
                            }
                            else
                            {
                                // at this point we know the message type is enabled
                                // unconnected application (non-library) message
                                msg.m_incomingMessageType = NetIncomingMessageType.UnconnectedData;
                                ReleaseMessage(msg);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError("Packet parsing error: " + ex.Message + " from " + ipsender);
                    }

                    ptr += payloadByteLength;
                }

                m_statistics.PacketReceived(bytesReceived, numMessages, numFragments);
                if (sender != null)
                    sender.m_statistics.PacketReceived(bytesReceived, numMessages, numFragments);
            } while (m_socket.Available > 0);
        }

        /// <summary>
        /// If NetPeerConfiguration.AutoFlushSendQueue() is false; you need to call this to send all messages queued using SendMessage()
        /// </summary>
        public void FlushSendQueue()
        {
            m_executeFlushSendQueue = true;
        }

        internal void HandleIncomingDiscoveryRequest(double now, NetEndPoint senderEndPoint, int ptr,
            int payloadByteLength)
        {
            if (m_configuration.IsMessageTypeEnabled(NetIncomingMessageType.DiscoveryRequest))
            {
                NetIncomingMessage dm =
                    CreateIncomingMessage(NetIncomingMessageType.DiscoveryRequest, payloadByteLength);
                if (payloadByteLength > 0)
                    Buffer.BlockCopy(m_receiveBuffer, ptr, dm.m_data, 0, payloadByteLength);
                dm.m_receiveTime = now;
                dm.m_bitLength = payloadByteLength * 8;
                dm.m_senderEndPoint = senderEndPoint;
                ReleaseMessage(dm);
            }
        }

        internal void HandleIncomingDiscoveryResponse(double now, NetEndPoint senderEndPoint, int ptr,
            int payloadByteLength)
        {
            if (m_configuration.IsMessageTypeEnabled(NetIncomingMessageType.DiscoveryResponse))
            {
                NetIncomingMessage dr =
                    CreateIncomingMessage(NetIncomingMessageType.DiscoveryResponse, payloadByteLength);
                if (payloadByteLength > 0)
                    Buffer.BlockCopy(m_receiveBuffer, ptr, dr.m_data, 0, payloadByteLength);
                dr.m_receiveTime = now;
                dr.m_bitLength = payloadByteLength * 8;
                dr.m_senderEndPoint = senderEndPoint;
                ReleaseMessage(dr);
            }
        }

        private void ReceivedUnconnectedLibraryMessage(double now, NetEndPoint senderEndPoint, NetMessageType tp,
            int ptr, int payloadByteLength)
        {
            NetConnection shake;
            if (_handshakeManager.Handshakes.TryGetValue(senderEndPoint, out shake))
            {
                shake.ReceivedHandshake(now, tp, ptr, payloadByteLength);
                return;
            }

            //
            // Library message from a completely unknown sender; lets just accept Connect
            //
            switch (tp)
            {
                case NetMessageType.Discovery:
                    HandleIncomingDiscoveryRequest(now, senderEndPoint, ptr, payloadByteLength);
                    return;
                case NetMessageType.DiscoveryResponse:
                    HandleIncomingDiscoveryResponse(now, senderEndPoint, ptr, payloadByteLength);
                    return;
                case NetMessageType.NatIntroduction:
                    if (m_configuration.IsMessageTypeEnabled(NetIncomingMessageType.NatIntroductionSuccess))
                        HandleNatIntroduction(ptr);
                    return;
                case NetMessageType.NatPunchMessage:
                    if (m_configuration.IsMessageTypeEnabled(NetIncomingMessageType.NatIntroductionSuccess))
                        HandleNatPunch(ptr, senderEndPoint);
                    return;
                case NetMessageType.NatIntroductionConfirmRequest:
                    if (m_configuration.IsMessageTypeEnabled(NetIncomingMessageType.NatIntroductionSuccess))
                        HandleNatPunchConfirmRequest(ptr, senderEndPoint);
                    return;
                case NetMessageType.NatIntroductionConfirmed:
                    if (m_configuration.IsMessageTypeEnabled(NetIncomingMessageType.NatIntroductionSuccess))
                        HandleNatPunchConfirmed(ptr, senderEndPoint);
                    return;
                case NetMessageType.ConnectResponse:
                    
                    foreach (var hs in _handshakeManager.Handshakes)
                    {
                        if (hs.Key.Address.Equals(senderEndPoint.Address))
                        {
                            if (hs.Value.m_connectionInitiator)
                            {
                                //
                                // We are currently trying to connection to XX.XX.XX.XX:Y
                                // ... but we just received a ConnectResponse from XX.XX.XX.XX:Z
                                // Lets just assume the router decided to use this port instead
                                //
                                var hsconn = hs.Value;
                                m_connectionLookup.Remove(hs.Key);
                                _handshakeManager.RemoveHandshake(hs.Key);
                                LogDebug("Detected host port change; rerouting connection to " +
                                            senderEndPoint);
                                hsconn.MutateEndPoint(senderEndPoint);

                                m_connectionLookup.Add(senderEndPoint, hsconn);
                                _handshakeManager.AddHandshake(senderEndPoint, hsconn);

                                hsconn.ReceivedHandshake(now, tp, ptr, payloadByteLength);
                                return;
                            }
                        }
                        
                    }

                    LogWarning("Received unhandled library message " + tp + " from " + senderEndPoint);
                    return;
                case NetMessageType.Connect:
                    if (m_configuration.AcceptIncomingConnections == false)
                    {
                        LogWarning("Received Connect, but we're not accepting incoming connections!");
                        return;
                    }
                    // handle connect
                    // It's someone wanting to shake hands with us!

                    int reservedSlots = _handshakeManager.Handshakes.Count + m_connections.Count;
                    if (reservedSlots >= m_configuration.m_maximumConnections)
                    {
                        // server full
                        NetOutgoingMessage full = CreateMessage("Server full");
                        full.m_messageType = NetMessageType.Disconnect;
                        SendLibrary(full, senderEndPoint);
                        return;
                    }

                    // Ok, start handshake!
                    NetConnection conn = new NetConnection(this, senderEndPoint);
                    conn.m_status = NetConnectionStatus.ReceivedInitiation;
                    _handshakeManager.Handshakes.TryAdd(senderEndPoint, conn);
                    conn.ReceivedHandshake(now, tp, ptr, payloadByteLength);
                    return;

                case NetMessageType.Disconnect:
                    // this is probably ok
                    LogVerbose("Received Disconnect from unconnected source: " + senderEndPoint);
                    return;
                default:
                    LogWarning("Received unhandled library message " + tp + " from " + senderEndPoint);
                    return;
            }
        }

        internal void AcceptConnection(NetConnection conn)
        {
            // LogDebug("Accepted connection " + conn);
            conn.InitExpandMTU(NetTime.Now);

            _handshakeManager.RemoveHandshake(conn.m_remoteEndPoint);

            lock (m_connections)
            {
                if (m_connections.Contains(conn))
                {
                    LogWarning("AcceptConnection called but m_connection already contains it!");
                }
                else
                {
                    m_connections.Add(conn);
                    m_connectionLookup.Add(conn.m_remoteEndPoint, conn);
                }
            }
        }

        internal NetIncomingMessage SetupReadHelperMessage(int ptr, int payloadLength)
        {
            m_readHelperMessage.m_bitLength = (ptr + payloadLength) * 8;
            m_readHelperMessage.m_readPosition = (ptr * 8);
            return m_readHelperMessage;
        }
    }
}