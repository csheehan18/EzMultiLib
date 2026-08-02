namespace EzMultiLib.IO
{
	public interface IPacketReader
	{
		bool Failed { get; }

		bool ReadBool();
		sbyte ReadSByte();
		byte ReadByte();
		short ReadShort();
		ushort ReadUShort();
		int ReadInt();
		uint ReadUInt();
		long ReadLong();
		ulong ReadULong();
		float ReadFloat();
		double ReadDouble();
		string? ReadString();
	}
}
