namespace EzMultiLib.IO
{
	public interface IPacketWriter
	{
		void WriteBool(bool value);
		void WriteSByte(sbyte value);
		void WriteByte(byte value);
		void WriteShort(short value);
		void WriteUShort(ushort value);
		void WriteInt(int value);
		void WriteUInt(uint value);
		void WriteLong(long value);
		void WriteULong(ulong value);
		void WriteFloat(float value);
		void WriteDouble(double value);
		void WriteString(string? value);
	}
}
