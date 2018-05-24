using System;
using System.Reflection;
using Lidgren.Network;
using NUnit.Framework;

namespace UnitTests
{
	public static class MiscTests
	{
	    /// <summary>
	    /// Helper method
	    /// </summary>
	    public static NetIncomingMessage CreateIncomingMessage(byte[] fromData, int bitLength)
	    {
	        NetIncomingMessage inc = (NetIncomingMessage)Activator.CreateInstance(typeof(NetIncomingMessage), true);
	        typeof(NetIncomingMessage).GetField("m_data", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(inc, fromData);
	        typeof(NetIncomingMessage).GetField("m_bitLength", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(inc, bitLength);
	        return inc;
	    }

        public static void Run(NetPeer peer)
		{
			NetPeerConfiguration config = new NetPeerConfiguration("Test");

			config.EnableMessageType(NetIncomingMessageType.UnconnectedData);
			if (config.IsMessageTypeEnabled(NetIncomingMessageType.UnconnectedData) == false)
				throw new NetException("setting enabled message types failed");

			config.SetMessageTypeEnabled(NetIncomingMessageType.UnconnectedData, false);
			if (config.IsMessageTypeEnabled(NetIncomingMessageType.UnconnectedData) == true)
				throw new NetException("setting enabled message types failed");

		    TestContext.Out.WriteLine("Misc tests OK");

		    TestContext.Out.WriteLine("Hex test: " + NetUtility.ToHexString(new byte[]{0xDE,0xAD,0xBE,0xEF}));

			if (NetUtility.BitsToHoldUInt64((ulong)UInt32.MaxValue + 1ul) != 33)
				throw new NetException("BitsToHoldUInt64 failed");
		}
	}
}
