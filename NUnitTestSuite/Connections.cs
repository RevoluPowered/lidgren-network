
using System.Net;
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
                Port = 27015
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

        [Test, Repeat(100)]
        public void NetworkServerTest()
        {
            var server = StartServer();
            StopServer(server);

            Assert.AreEqual(NetPeerStatus.ShutdownRequested, server.Status);
            Assert.That(() => server.Status, Is.EqualTo(NetPeerStatus.NotRunning).After(2).Seconds.PollEvery(100));
        }

        [Test]
        public void NetworkClientInitTest()
        {
            var config = new NetPeerConfiguration("test-suite")
            {
                EnableUPnP = true,
                Port = 27015
            };

            config.EnableMessageType(NetIncomingMessageType.NatIntroductionSuccess);

            var client = new NetClient(config);
            client.Start();
            client.Shutdown("closing client connection");
        }
    }
}
