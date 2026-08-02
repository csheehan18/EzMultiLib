using EzMultiLib.IO;

namespace EzMultiLib.Serialization.Packets
{
	public static class PacketFramer
	{
		public const int HeaderSize = sizeof(ushort);

		public static void WriteHeader(IPacketWriter writer, ushort id) => writer.WriteUShort(id);

		public static ushort ReadHeader(IPacketReader reader) => reader.ReadUShort();
	}
}
