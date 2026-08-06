using EzMultiLib.IO;

namespace EzMultiLib.Serialization.Packets
{
	public static class PacketFramer
	{
		public const int HeaderSize = sizeof(uint);

		public static void WriteHeader(IPacketWriter writer, uint id) => writer.WriteUInt(id);

		public static uint ReadHeader(IPacketReader reader) => reader.ReadUInt();
	}
}
