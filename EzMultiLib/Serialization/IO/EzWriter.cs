using EzMultiLib.IO;
using System;
using System.Text;

namespace EzMultiLib.Serialization.IO
{
	public sealed class EzWriter : IPacketWriter
	{
		private byte[] _buffer;
		private int _position;

		public EzWriter()
			: this(64)
		{
		}

		public EzWriter(int capacity)
		{
			_buffer = new byte[capacity < 1 ? 1 : capacity];
		}

		public int Length => _position;

		public void Reset() => _position = 0;

		// Take can reallocate _buffer, so callers must call it before they touch the
		// field. Inlined into a _buffer expression it captures the old array and the
		// bytes land in the copy that the resize already discarded.
		private int Take(int count)
		{
			var required = _position + count;

			if (required > _buffer.Length)
			{
				var size = _buffer.Length;

				while (size < required)
					size *= 2;

				Array.Resize(ref _buffer, size);
			}

			var start = _position;
			_position += count;
			return start;
		}

		public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

		public void WriteByte(byte value)
		{
			var i = Take(1);
			_buffer[i] = value;
		}

		public void WriteSByte(sbyte value) => WriteByte((byte)value);

		public void WriteShort(short value) => WriteUShort((ushort)value);

		public void WriteUShort(ushort value)
		{
			var i = Take(2);
			_buffer[i] = (byte)value;
			_buffer[i + 1] = (byte)(value >> 8);
		}

		public void WriteInt(int value) => WriteUInt((uint)value);

		public void WriteUInt(uint value)
		{
			var i = Take(4);
			_buffer[i] = (byte)value;
			_buffer[i + 1] = (byte)(value >> 8);
			_buffer[i + 2] = (byte)(value >> 16);
			_buffer[i + 3] = (byte)(value >> 24);
		}

		public void WriteLong(long value) => WriteULong((ulong)value);

		public void WriteULong(ulong value)
		{
			var i = Take(8);
			_buffer[i] = (byte)value;
			_buffer[i + 1] = (byte)(value >> 8);
			_buffer[i + 2] = (byte)(value >> 16);
			_buffer[i + 3] = (byte)(value >> 24);
			_buffer[i + 4] = (byte)(value >> 32);
			_buffer[i + 5] = (byte)(value >> 40);
			_buffer[i + 6] = (byte)(value >> 48);
			_buffer[i + 7] = (byte)(value >> 56);
		}

		public void WriteFloat(float value) => WriteInt(BitConverter.SingleToInt32Bits(value));

		public void WriteDouble(double value) => WriteLong(BitConverter.DoubleToInt64Bits(value));

		public void WriteString(string? value)
		{
			WriteBool(value != null);

			if (value == null)
				return;

			var count = Encoding.UTF8.GetByteCount(value);

			if (count > ushort.MaxValue)
				throw new ArgumentException($"String is {count} bytes encoded, which exceeds the {ushort.MaxValue} byte limit.", nameof(value));

			WriteUShort((ushort)count);

			var i = Take(count);
			Encoding.UTF8.GetBytes(value, 0, value.Length, _buffer, i);
		}

		public byte[] ToArray()
		{
			var result = new byte[_position];
			Array.Copy(_buffer, 0, result, 0, _position);
			return result;
		}

		public void CopyTo(byte[] destination, int offset) =>
			Array.Copy(_buffer, 0, destination, offset, _position);
	}
}
