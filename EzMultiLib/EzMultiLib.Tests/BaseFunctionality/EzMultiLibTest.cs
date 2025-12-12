using EzMultiLib.IO;
using EzMultiLib.Packets;
using EzMultiLib.Peers;
using System.Text;

public class EzMultiLibTest
{
	[Fact]
	public void Packet_Is_Serialized_Dispatched_And_Received()
	{
		SimplePacket? receivedPacket = null;
		Peer? receivedPeer = null;

		// Able to subscibe to incoming packets with their Class name
		PacketAction.OnSimplePacket += (peer, packet) =>
		{
			receivedPeer = peer;
			receivedPacket = packet;
		};

		var outgoing = new SimplePacket
		{
			SimpleText = "hello"
		};

		// Test writer which eventually would be handled by transports (in future)
		var writer = new TestPacketWriter();
		writer.WriteUShort(PacketAction.GetPacketId(outgoing));
		outgoing.Serialize(writer);

		var bytes = writer.ToArray();

		// Read the incoming packet
		var reader = new TestPacketReader(bytes);
		// Since Id will be consistent accross projects we can quickly read the type of packet we recieved
		ushort id = reader.ReadUShort();

		// Read the id of the packet and return our new packet type
		var incoming = PacketAction.CreatePacket(id, reader);

		// Send packet through PacketAction to invoke an action
		PacketAction.AcceptPacket(null, incoming);

		Assert.NotNull(receivedPacket);
		Assert.Equal("hello", receivedPacket!.SimpleText);
		Assert.Null(receivedPeer);
	}
}

public class SimplePacket : IPacket
{
	public string?	 SimpleText;

	public void Serialize(IPacketWriter writer)
	{
		writer.WriteString(SimpleText);
	}

	public void Deserialize(IPacketReader reader)
	{
		SimpleText = reader.ReadString();
	}
}

public sealed class TestPacketWriter : IPacketWriter
{
	private readonly MemoryStream _stream = new MemoryStream();
	private readonly BinaryWriter _writer;

	public TestPacketWriter()
	{
		_writer = new BinaryWriter(_stream, Encoding.UTF8, true);
	}

	public void WriteInt(int value) => _writer.Write(value);
	public void WriteFloat(float value) => _writer.Write(value);
	public void WriteBool(bool value) => _writer.Write(value);
	public void WriteUShort(ushort value) => _writer.Write(value);
	public void WriteString(string value) => _writer.Write(value);

	public byte[] ToArray() => _stream.ToArray();
}

public sealed class TestPacketReader : IPacketReader
{
	private readonly BinaryReader _reader;

	public TestPacketReader(byte[] data)
	{
		_reader = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
	}

	public int ReadInt() => _reader.ReadInt32();
	public float ReadFloat() => _reader.ReadSingle();
	public bool ReadBool() => _reader.ReadBoolean();
	public ushort ReadUShort() => _reader.ReadUInt16();
	public string ReadString() => _reader.ReadString();
}
