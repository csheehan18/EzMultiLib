using EzMultiLib.IO;
using System.IO;
using System.Text;

namespace EzMultiLib.Serialization.IO
{
	internal class EzWriter : IPacketWriter
	{
		private readonly MemoryStream _stream = new MemoryStream();
		private readonly BinaryWriter _writer;

		public EzWriter()
		{
			_writer = new BinaryWriter(_stream, Encoding.UTF8, true);
		}

		public void WriteInt(int value) => _writer.Write(value);
		public void WriteFloat(float value) => _writer.Write(value);
		public void WriteBool(bool value) => _writer.Write(value);
		public void WriteUShort(ushort value) => _writer.Write(value);
		public void WriteString(string value) => _writer.Write(value);
		public void Flush() => _writer.Flush();

		public byte[] ToArray() => _stream.ToArray();
	}
}
