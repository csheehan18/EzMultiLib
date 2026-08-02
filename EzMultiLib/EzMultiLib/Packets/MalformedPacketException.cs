using System;

namespace EzMultiLib.Packets
{
	public sealed class MalformedPacketException : Exception
	{
		public MalformedPacketException(string message)
			: base(message)
		{
		}
	}
}
