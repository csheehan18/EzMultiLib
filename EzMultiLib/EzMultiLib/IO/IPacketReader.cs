namespace EzMultiLib.IO
{
	public interface IPacketReader
	{
		int ReadInt();
		float ReadFloat();
		bool ReadBool();
		ushort ReadUShort();
		string ReadString();
	}
}
