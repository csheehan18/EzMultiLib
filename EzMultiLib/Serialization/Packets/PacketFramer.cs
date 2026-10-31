using EzMultiLib.IO;
using System;

namespace EzMultiLib.Serialization.Packets
{
	public static class PacketFramer
	{
		public const int HeaderSize = sizeof(uint);

		// A collection's length goes on the wire as a ushort, the same width a string
		// uses, so a hostile count can never describe more than this many elements.
		public const int MaxCount = ushort.MaxValue;

		public static void WriteHeader(IPacketWriter writer, uint id) => writer.WriteUInt(id);

		public static uint ReadHeader(IPacketReader reader) => reader.ReadUInt();

		public static void WriteCount(IPacketWriter writer, int count)
		{
			if (count > MaxCount)
				throw new ArgumentException($"Collection has {count} elements, which exceeds the {MaxCount} element limit.", nameof(count));

			writer.WriteUShort((ushort)count);
		}
	}
}
