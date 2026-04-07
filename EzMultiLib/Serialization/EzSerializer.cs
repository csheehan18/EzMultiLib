using EzMultiLib.Packets;
using EzMultiLib.Serialization.IO;
using EzMultiLib.Serialization.Packets;
using System;

namespace EzMultiLib.Serialization
{
	public class EzSerializer
	{
		public static byte[] Serialize(IPacket packet)
		{
			var writer = new EzWriter();
			var model = PacketStorage.GetOrCreate(packet.GetType());
			model.Write(writer, packet);
            return writer.ToArray();
        }

		public static IPacket Deserialize(Type packetType, byte[] data)
		{
			var reader = new EzReader(data);
			var model = PacketStorage.GetOrCreate(packetType);
			return (IPacket)model.Read(reader);
		}
	}
}
