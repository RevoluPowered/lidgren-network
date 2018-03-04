using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using NUnit.Framework;
using Lidgren.Network;

namespace NUnitTestSuite
{
    [TestFixture]
    public class Connections
    {
        public static void InitTestContext()
        {
            // console app sync context
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        }

        public NetServer StartServer()
        {
            var config = new NetPeerConfiguration("tests")
            {
                Port = 27015,
                MaximumConnections = 1024
            };

            var server = new NetServer(config);
            server.Start();

            return server;
        }

        public void StopServer( NetServer server )
        {
            server.Shutdown("closing server");
        }

        [Test, Repeat(5)]
        public void NetworkServerInitTest()
        {
            NetPeerManager.StartNetworkThread();
            var server = StartServer();

            Assert.That(() => server.Status, Is.EqualTo(NetPeerStatus.Running).After(4).Seconds.PollEvery(100));

            StopServer(server);
            
            Assert.That(() => server.Status, Is.EqualTo(NetPeerStatus.NotRunning).After(4).Seconds.PollEvery(100));
            NetPeerManager.WaitForExit();
        }




        [Test, Repeat(5)]
        public void NetworkClientInitTest()
        {
            NetPeerManager.StartNetworkThread();
            InitTestContext();
            var client = new TestClient();
            
            Assert.That(() => client.NetClient.Status, Is.EqualTo(NetPeerStatus.Running).After(4).Seconds.PollEvery(50));

            client.StopClient();

            Assert.That(() => client.NetClient.Status, Is.EqualTo(NetPeerStatus.NotRunning).After(4).Seconds.PollEvery(50));
            NetPeerManager.WaitForExit();
        }

        public bool ConnectionStatusHandler( string context, NetIncomingMessage message )
        {
            switch (message.MessageType)
            {
                case NetIncomingMessageType.StatusChanged:
                    NetConnectionStatus status = (NetConnectionStatus) message.ReadByte();
                    TestContext.Out.WriteLine("[" + context + "] Connection status changed: " + status);
                    if (status == NetConnectionStatus.Disconnected)
                    {
                        return true;
                    }
                
                    break;
                default:
                    TestContext.Out.WriteLine("[" + context + "] data: " + message.ReadString());
                    break;
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
                        var status = ConnectionStatusHandler("server", message);
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
        




        [Test]
        public void NetworkConnectDisconnect()
        {
            InitTestContext();
            TestContext.Out.WriteLine("-----------------------------------------------------------");
            NetPeerManager.StartNetworkThread();
            Thread serverThread = new Thread(ServerThread);
            
            serverThread.Start();

            var clients = new List<TestClient>(1000);

            // pool 20 clients
            for (var x = 0; x < 1000; x++)
            {
                clients.Add( new TestClient());
            }
            
            foreach (var client in clients)
            {
                client.DoConnectTest();
            }

            serverThread.Join();
            NetPeerManager.WaitForExit();
        }
    }
}
