using EzMultiLib.IO;
using System.IO;
using System.Text;

namespace EzMultiLib.Serialization.IO
{
	internal class EzReader : IPacketReader
	{
		private readonly BinaryReader _reader;

		public EzReader(byte[] data)
		{
			_reader = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
		}

		public int ReadInt() => _reader.ReadInt32();
		public float ReadFloat() => _reader.ReadSingle();
		public bool ReadBool() => _reader.ReadBoolean();
		public ushort ReadUShort() => _reader.ReadUInt16();
		public string ReadString() => _reader.ReadString();
	}
}
