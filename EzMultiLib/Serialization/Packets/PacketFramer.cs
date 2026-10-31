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

		// Drains the reader so that Failed trips. For a packet this build cannot parse,
		// where the unknown part's size is not known either: framing is already lost,
		// so the rest of the buffer has to go with it rather than be misread as the
		// next packet.
		public static void Fail(IPacketReader reader)
		{
			var remaining = reader.Remaining + 1;

			for (var i = 0; i < remaining; i++)
				reader.ReadByte();
		}

		public static void WriteCount(IPacketWriter writer, int count)
		{
			if (count > MaxCount)
				throw new ArgumentException($"Collection has {count} elements, which exceeds the {MaxCount} element limit.", nameof(count));

			writer.WriteUShort((ushort)count);
		}
	}
}
