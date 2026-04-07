namespace EzMultiLib.IO
{
	public interface IPacketReader
	{
		byte ReadByte();
		int ReadInt();
		float ReadFloat();
		bool ReadBool();
		ushort ReadUShort();
		string ReadString();
	}
}
