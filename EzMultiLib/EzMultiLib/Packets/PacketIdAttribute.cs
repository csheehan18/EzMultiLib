using System;

namespace EzMultiLib.Packets
{
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
	public sealed class PacketIdAttribute : Attribute
	{
		public ushort Id { get; }

		public PacketIdAttribute(ushort id)
		{
			Id = id;
		}
	}
}
