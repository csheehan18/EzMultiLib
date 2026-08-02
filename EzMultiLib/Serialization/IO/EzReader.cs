using EzMultiLib.IO;
using System;
using System.Text;

namespace EzMultiLib.Serialization.IO
{
	public sealed class EzReader : IPacketReader
	{
		private readonly byte[] _data;
		private readonly int _end;
		private int _position;
		private bool _failed;

		public EzReader(byte[] data)
			: this(data, 0, data?.Length ?? 0)
		{
		}

		public EzReader(byte[] data, int offset, int count)
		{
			if (data == null || offset < 0 || count < 0 || offset > data.Length - count)
			{
				_data = Array.Empty<byte>();
				_end = 0;
				_position = 0;
				_failed = true;
				return;
			}

			_data = data;
			_position = offset;
			_end = offset + count;
		}

		public bool Failed => _failed;

		public int Remaining => _failed ? 0 : _end - _position;

		private bool Take(int count, out int start)
		{
			if (_failed || count > _end - _position)
			{
				_failed = true;
				start = 0;
				return false;
			}

			start = _position;
			_position += count;
			return true;
		}

		public bool ReadBool() => ReadByte() != 0;

		public byte ReadByte() => Take(1, out var i) ? _data[i] : (byte)0;

		public sbyte ReadSByte() => (sbyte)ReadByte();

		public short ReadShort() => (short)ReadUShort();

		public ushort ReadUShort()
		{
			if (!Take(2, out var i))
				return 0;

			return (ushort)(_data[i] | (_data[i + 1] << 8));
		}

		public int ReadInt() => (int)ReadUInt();

		public uint ReadUInt()
		{
			if (!Take(4, out var i))
				return 0;

			return (uint)(_data[i]
				| (_data[i + 1] << 8)
				| (_data[i + 2] << 16)
				| (_data[i + 3] << 24));
		}

		public long ReadLong() => (long)ReadULong();

		public ulong ReadULong()
		{
			if (!Take(8, out var i))
				return 0;

			var low = (uint)(_data[i]
				| (_data[i + 1] << 8)
				| (_data[i + 2] << 16)
				| (_data[i + 3] << 24));

			var high = (uint)(_data[i + 4]
				| (_data[i + 5] << 8)
				| (_data[i + 6] << 16)
				| (_data[i + 7] << 24));

			return ((ulong)high << 32) | low;
		}

		public float ReadFloat() => BitConverter.Int32BitsToSingle(ReadInt());

		public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadLong());

		public string? ReadString()
		{
			if (!ReadBool())
				return null;

			var length = ReadUShort();

			if (_failed)
				return null;

			if (!Take(length, out var start))
				return null;

			return Encoding.UTF8.GetString(_data, start, length);
		}
	}
}
