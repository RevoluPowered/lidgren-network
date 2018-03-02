using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Lidgren.Network;
using NUnit.Framework;
using UnitTests;

namespace NUnitTestSuite
{
    [TestFixture]
    class GeneralTestSuite
    {
        [OneTimeSetUp]
        public void ContextSet()
        {
            Connections.InitTestContext();
        }

        [Test, MaxTime(30000), Repeat(5)]
        public void GenericTestSuite()
        {
            NetPeerConfiguration config = new NetPeerConfiguration("oldUnitTests");
            config.EnableUPnP = true;
            NetPeer peer = new NetPeer(config);
            peer.Start(); // needed for initialization

            TestContext.Out.WriteLine("Unique identifier is " + NetUtility.ToHexString(peer.UniqueIdentifier));

            ReadWriteTests.Run(peer);

            NetQueueTests.Run();

            MiscTests.Run(peer);

            BitVectorTests.Run();

            EncryptionTests.Run(peer);

            var om = peer.CreateMessage();
            peer.SendUnconnectedMessage(om, new IPEndPoint(IPAddress.Loopback, 14242));
            try
            {
                peer.SendUnconnectedMessage(om, new IPEndPoint(IPAddress.Loopback, 14242));
            }
            catch (NetException nex)
            {
                if (nex.Message != "This message has already been sent! Use NetPeer.SendMessage() to send to multiple recipients efficiently")
                    throw;
            }

            peer.Shutdown("bye");

            // read all message
            NetIncomingMessage inc = peer.WaitMessage(5000);
            while (inc != null)
            {
                switch (inc.MessageType)
                {
                    case NetIncomingMessageType.DebugMessage:
                    case NetIncomingMessageType.VerboseDebugMessage:
                    case NetIncomingMessageType.WarningMessage:
                    case NetIncomingMessageType.ErrorMessage:
                        TestContext.Out.WriteLine("Peer message: " + inc.ReadString());
                        break;
                    case NetIncomingMessageType.Error:
                        throw new Exception("Received error message!");
                }

                inc = peer.ReadMessage();
            }

            TestContext.Out.WriteLine("Completed Generic Test Suite");
            TestContext.Out.WriteLine("----------------------------");
        }
    }
}
