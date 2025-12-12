namespace EzMultiLib.IO
{
	public interface IPacketWriter
	{
		void WriteInt(int value);
		void WriteFloat(float value);
		void WriteBool(bool value);
		void WriteUShort(ushort value);
		void WriteString(string value);
	}
}
