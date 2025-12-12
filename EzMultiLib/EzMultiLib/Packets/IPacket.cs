using EzMultiLib.IO;

namespace EzMultiLib.Packets
{
	// Eventually I would love to get rid of the serialize and deserialize in favor of a generator but thats for the future
	public interface IPacket
	{
		void Serialize(IPacketWriter writer);
		void Deserialize(IPacketReader reader);
	}
}
