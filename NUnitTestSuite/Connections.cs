
using System;
using System.ComponentModel;
using System.Net;
using System.Threading;
using NUnit.Framework;
using Lidgren;
using Lidgren.Network;
using NUnit.Framework.Constraints;

namespace NUnitTestSuite
{
    [TestFixture]
    public class Connections
    {
        public NetServer StartServer()
        {
            var config = new NetPeerConfiguration("test-suite")
            {
                EnableUPnP = true,
                Port = 27015,
                MaximumConnections = 10
            };

            // enable nat 
            config.EnableMessageType(NetIncomingMessageType.NatIntroductionSuccess);

            NetServer server = new NetServer(config);
            server.Start();

            return server;
        }

        public void StopServer( NetServer server )
        {
            server.Shutdown("closing server");
        }

        [Test, Repeat(20)]
        public void NetworkServerInitTest()
        {
            var server = StartServer();
            StopServer(server);

            Assert.AreEqual(NetPeerStatus.ShutdownRequested, server.Status);
            Assert.That(() => server.Status, Is.EqualTo(NetPeerStatus.NotRunning).After(4).Seconds.PollEvery(100));
        }

        public NetClient StartClient()
        {
            var config = new NetPeerConfiguration("test-suite")
            {
                EnableUPnP = true,
                Port = 27015
            };

            config.EnableMessageType(NetIncomingMessageType.NatIntroductionSuccess);

            var client = new NetClient(config);
            client.Start();

            return client;
        }

        public void StopClient(NetClient client)
        {
            client.Shutdown("closing client connection");
        }

        [Test, Repeat(20)]
        public void NetworkClientInitTest()
        {
            var client = StartClient();

            StopClient(client);
        }

        public bool ConnectionStatusHandler( NetIncomingMessage message )
        {
            string data = message.ReadString();
            TestContext.Out.WriteLine("ConnectionHandler: " + data);
            switch (message.MessageType)
            {
                case NetIncomingMessageType.StatusChanged:
                    NetConnectionStatus status = (NetConnectionStatus) message.ReadByte();
                    TestContext.Out.WriteLine("Connection status changed: " + status);
                    if (status == NetConnectionStatus.Disconnected)
                    {
                        return true;
                    }
                
                    break;
                case NetIncomingMessageType.Error:
                    break;
                case NetIncomingMessageType.UnconnectedData:
                    break;
                case NetIncomingMessageType.ConnectionApproval:
                    break;
                case NetIncomingMessageType.Data:
                    break;
                case NetIncomingMessageType.Receipt:
                    break;
                case NetIncomingMessageType.DiscoveryRequest:
                    break;
                case NetIncomingMessageType.DiscoveryResponse:
                    break;
                case NetIncomingMessageType.VerboseDebugMessage:
                    break;
                case NetIncomingMessageType.DebugMessage:
                    break;
                case NetIncomingMessageType.WarningMessage:
                    break;
                case NetIncomingMessageType.ErrorMessage:
                    break;
                case NetIncomingMessageType.NatIntroductionSuccess:
                    break;
                case NetIncomingMessageType.ConnectionLatencyUpdated:
                    break;
                default:
                    throw new InvalidEnumArgumentException();
            }

            return false;
        }

        public void ServerThread()
        {
            var server = StartServer();
            var running = true;
            try
            {
                // enter context for handling messages
                while (running)
                {
                    NetIncomingMessage message;

                    while ((message = server.ReadMessage()) != null)
                    {
                        var status = ConnectionStatusHandler(message);
                        if (status)
                        {
                            TestContext.Out.WriteLine("Client has disconnected from the server");

                            running = false;
                            break;
                        }

                        server.Recycle(message);
                    }

                }
            }
            catch (Exception e)
            {
                TestContext.Out.WriteLine(e.ToString());
                throw;
            }
            finally
            {
                TestContext.Out.WriteLine("Stopping server");
                StopServer(server);
            }
        }

        public void ClientThread()
        {
            var client = StartClient();
            client.Connect("127.0.0.1", 27015);

            var running = true;
            var calledDisconnect = false;
            try
            {
                // enter context for handling messages
                while (running)
                {
                    NetIncomingMessage message;

                    while ((message = client.ReadMessage()) != null)
                    {
                        var status = ConnectionStatusHandler(message);
                        if (status)
                        {
                            TestContext.Out.WriteLine("Recieved disconnection flag");
                            running = false;
                            break;
                        }

                        // disconnect client ONLY when the client has connected
                        if (client.ConnectionStatus == NetConnectionStatus.Connected && !calledDisconnect )
                        {
                            TestContext.Out.WriteLine("Informing client socket to disconnect and waiting for proper disconnect flag");
                            client.Disconnect("k thx bye");
                            calledDisconnect = true;
                        }

                        client.Recycle(message);
                    }

                }
            }
            catch (Exception e)
            {
                TestContext.Out.WriteLine(e.ToString());
                throw;
            }
            finally
            {
                TestContext.Out.WriteLine("Stopping client");
                Assert.IsFalse(running, "Client still running, when it should have closed");

                StopClient(client);
            }
        }
        
        [Test, MaxTime(5000)]
        public void NetworkConnectDisconnect()
        {
            Thread serverThread = new Thread(ServerThread);
            Thread clientThread = new Thread(ClientThread);

            serverThread.Start();
            clientThread.Start();

            // join for 10 seconds
            serverThread.Join(TimeSpan.FromSeconds(10));
            // server exited, give the client 2-3 seconds to stop.
            clientThread.Join(TimeSpan.FromSeconds(3));
        }
    }
}
