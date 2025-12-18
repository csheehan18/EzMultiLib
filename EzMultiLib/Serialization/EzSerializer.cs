using EzMultiLib.Packets;
using EzMultiLib.Serialization.Packets;
using System;

namespace EzMultiLib.Serialization
{
	public class EzSerializer
	{
		public static void Serialize(IPacket packet, EzWriter writer)
		{
			var model = PacketStorage.GetOrCreate(packet.GetType());
			model.Write(writer, packet);
		}

		public static IPacket Deserialize(Type packetType, EzReader reader)
		{
			var model = PacketStorage.GetOrCreate(packetType);
			return (IPacket)model.Read(reader);
		}
	}
}
