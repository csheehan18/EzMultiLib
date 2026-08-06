using EzMultiLib.Serialization;
using EzMultiLib.Serialization.IO;

public class EzWriterTest
{
	[Fact]
	public void Byte_Writes_Survive_Growth()
	{
		var writer = new EzWriter(1);

		for (var i = 0; i < 300; i++)
			writer.WriteByte((byte)i);

		var reader = new EzReader(writer.ToArray());

		for (var i = 0; i < 300; i++)
			Assert.Equal((byte)i, reader.ReadByte());

		Assert.False(reader.Failed);
		Assert.Equal(0, reader.Remaining);
	}

	[Fact]
	public void Bool_Writes_Survive_Growth()
	{
		var writer = new EzWriter(1);

		for (var i = 0; i < 300; i++)
			writer.WriteBool(i % 3 == 0);

		var reader = new EzReader(writer.ToArray());

		for (var i = 0; i < 300; i++)
			Assert.Equal(i % 3 == 0, reader.ReadBool());

		Assert.False(reader.Failed);
	}

	[Fact]
	public void String_Writes_Survive_Growth()
	{
		var writer = new EzWriter(1);
		var value = new string('x', 300);

		writer.WriteString(value);

		Assert.Equal(value, new EzReader(writer.ToArray()).ReadString());
	}

	[Fact]
	public void String_Straddling_The_Capacity_Boundary_Survives_Growth()
	{
		// The write fits before the length prefix and does not after it, so the grow lands between reserving the prefix and encoding the bytes.
		for (var length = 1; length < 40; length++)
		{
			var writer = new EzWriter(16);
			var value = new string('y', length);

			writer.WriteString(value);

			Assert.Equal(value, new EzReader(writer.ToArray()).ReadString());
		}
	}

	[Fact]
	public void Multibyte_String_Survives_Growth()
	{
		var writer = new EzWriter(4);
		var value = string.Concat(Enumerable.Repeat("日本語", 40));

		writer.WriteString(value);

		Assert.Equal(value, new EzReader(writer.ToArray()).ReadString());
	}

	[Fact]
	public void Every_Write_Method_Survives_Growth()
	{
		var writer = new EzWriter(1);

		for (var i = 0; i < 20; i++)
		{
			writer.WriteBool(true);
			writer.WriteSByte(-3);
			writer.WriteByte(250);
			writer.WriteShort(-1234);
			writer.WriteUShort(60000);
			writer.WriteInt(-123456);
			writer.WriteUInt(4000000000);
			writer.WriteLong(-1234567890123);
			writer.WriteULong(17000000000000000000);
			writer.WriteFloat(0.5f);
			writer.WriteDouble(-0.25);
			writer.WriteString("value " + i);
		}

		var reader = new EzReader(writer.ToArray());

		for (var i = 0; i < 20; i++)
		{
			Assert.True(reader.ReadBool());
			Assert.Equal(-3, reader.ReadSByte());
			Assert.Equal(250, reader.ReadByte());
			Assert.Equal(-1234, reader.ReadShort());
			Assert.Equal(60000, reader.ReadUShort());
			Assert.Equal(-123456, reader.ReadInt());
			Assert.Equal(4000000000, reader.ReadUInt());
			Assert.Equal(-1234567890123, reader.ReadLong());
			Assert.Equal(17000000000000000000, reader.ReadULong());
			Assert.Equal(0.5f, reader.ReadFloat());
			Assert.Equal(-0.25, reader.ReadDouble());
			Assert.Equal("value " + i, reader.ReadString());
		}

		Assert.False(reader.Failed);
		Assert.Equal(0, reader.Remaining);
	}

	[Fact]
	public void Packet_Larger_Than_The_Default_Capacity_Round_Trips()
	{
		var outgoing = new SimplePacket
		{
			favoriteNumber = 42,
			simpleText = new string('z', 500)
		};

		var incoming = (SimplePacket)EzSerializer.Deserialize(EzSerializer.Serialize(outgoing));

		Assert.Equal(42, incoming.favoriteNumber);
		Assert.Equal(outgoing.simpleText, incoming.simpleText);
	}

	[Fact]
	public void Reset_Reuses_The_Grown_Buffer()
	{
		var writer = new EzWriter(1);

		writer.WriteString(new string('a', 200));
		writer.Reset();
		writer.WriteString("short");

		Assert.Equal(0, new EzReader(writer.ToArray()).Remaining - writer.Length);
		Assert.Equal("short", new EzReader(writer.ToArray()).ReadString());
	}

	[Fact]
	public void Maximum_Length_String_Round_Trips()
	{
		var writer = new EzWriter();
		var value = new string('m', ushort.MaxValue);

		writer.WriteString(value);

		Assert.Equal(value, new EzReader(writer.ToArray()).ReadString());
	}

	[Fact]
	public void String_Longer_Than_The_Length_Prefix_Allows_Is_Rejected()
	{
		Assert.Throws<ArgumentException>(
			() => new EzWriter().WriteString(new string('m', ushort.MaxValue + 1)));
	}
}
