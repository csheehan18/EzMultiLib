using EzMultiLib.Packets;

namespace SharedProtocol
{
	[Delivery(Delivery.Sequenced, Channel = 2)]
	public class SharedLoginPacket : IPacket
	{
		public string? username;
		public int playerId;
	}

	[PacketId(9001)]
	public class SharedPinnedPacket : IPacket
	{
		public float health;
	}

	internal class InternalPacket : IPacket
	{
		public int value = 1;
	}
}
