using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lidgren.Network;
using NUnit.Framework;

namespace NUnitTestSuite
{
    public class TestClient
    {
        private readonly NetClient _clientSocket;
        private bool _calledDisconnect = false;

        public void WaitForExit()
        {
            // max connection shutdown time is 4 seconds, otherwise exit.
            _clientSocket.GetNetworkThread().Join(4000);
        }

        public TestClient()
        {
            var config = new NetPeerConfiguration("tests");

            _clientSocket = new NetClient(config);
            _clientSocket.RegisterReceivedCallback(HandleMessageClientCallback);
            _clientSocket.Start();
        }

        public void DoConnectTest()
        {
            _clientSocket.Connect(new IPEndPoint(IPAddress.Loopback, 27015));
        }


        public void StopClient()
        {

            _clientSocket.Shutdown("closing client connection");
        }


        public bool ConnectionStatusHandler(string context, NetIncomingMessage message)
        {
            switch (message.MessageType)
            {
                case NetIncomingMessageType.StatusChanged:
                    NetConnectionStatus status = (NetConnectionStatus)message.ReadByte();
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

        public void HandleMessageClientCallback(object peer)
        {
            NetIncomingMessage message;
            NetClient client = (NetClient)peer;
            Assert.IsNotNull(client, "NetClient null");

            while ((message = client.ReadMessage()) != null)
            {
                var status = ConnectionStatusHandler("client", message);
                if (status)
                {
                    TestContext.Out.WriteLine("Received disconnection flag");
                    StopClient();
                    // make sure client stops properly
                    Assert.That(() => client.Status, Is.EqualTo(NetPeerStatus.NotRunning).After(4).Seconds.PollEvery(10));
                    break;
                }

                // disconnect client ONLY when the client has connected
                if (client.ConnectionStatus == NetConnectionStatus.Connected && !_calledDisconnect)
                {
                    TestContext.Out.WriteLine("Informing client socket to disconnect and waiting for proper disconnect flag");
                    client.Disconnect("k thx bye");
                    _calledDisconnect = true;
                }

                client.Recycle(message);
            }
        }
    }
}
