using System;

namespace EzMultiLib.Packets
{
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
	public sealed class PacketIdAttribute : Attribute
	{
		public uint Id { get; }

		public PacketIdAttribute(uint id)
		{
			Id = id;
		}
	}
}
