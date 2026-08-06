using EzMultiLib.IO;
using EzMultiLib.Packets;
using EzMultiLib.Peers;
using EzMultiLib.Serialization;
using EzMultiLib.Serialization.IO;
using System.Buffers.Binary;
using System.Text;

public sealed class SteamPeer : Peer
{
	public ulong SteamId { get; }

	public SteamPeer(int id, ulong steamId)
		: base(id)
	{
		SteamId = steamId;
	}
}

public sealed class LobbyPeer : Peer
{
	public LobbyPeer(int id)
		: base(id)
	{
	}
}

public sealed class SegmentReader : IPacketReader
{
	private readonly ArraySegment<byte> _segment;
	private int _position;
	private bool _failed;

	public SegmentReader(ArraySegment<byte> segment) => _segment = segment;

	public bool Failed => _failed;

	public int Remaining => _failed ? 0 : _segment.Count - _position;

	private ReadOnlySpan<byte> Take(int count)
	{
		if (_failed || count > Remaining)
		{
			_failed = true;
			return default;
		}

		var span = _segment.AsSpan(_position, count);
		_position += count;
		return span;
	}

	public bool ReadBool() => ReadByte() != 0;

	public byte ReadByte()
	{
		var span = Take(1);
		return span.IsEmpty ? (byte)0 : span[0];
	}

	public sbyte ReadSByte() => (sbyte)ReadByte();

	public short ReadShort() => (short)ReadUShort();

	public ushort ReadUShort()
	{
		var span = Take(2);
		return span.IsEmpty ? (ushort)0 : BinaryPrimitives.ReadUInt16LittleEndian(span);
	}

	public int ReadInt() => (int)ReadUInt();

	public uint ReadUInt()
	{
		var span = Take(4);
		return span.IsEmpty ? 0u : BinaryPrimitives.ReadUInt32LittleEndian(span);
	}

	public long ReadLong() => (long)ReadULong();

	public ulong ReadULong()
	{
		var span = Take(8);
		return span.IsEmpty ? 0ul : BinaryPrimitives.ReadUInt64LittleEndian(span);
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

		var span = Take(length);

		return _failed ? null : Encoding.UTF8.GetString(span);
	}
}

public class TransportIntegrationTest
{
	[Fact]
	public void Same_Id_On_Two_Connections_Stays_Two_Players()
	{
		var mesh = new SteamPeer(1, 76561198000000000);
		var lobby = new LobbyPeer(1);

		var state = new Dictionary<Peer, string>
		{
			[mesh] = "mesh player",
			[lobby] = "lobby player"
		};

		Assert.Equal(2, state.Count);
		Assert.Equal("mesh player", state[mesh]);
		Assert.Equal("lobby player", state[lobby]);
	}

	[Fact]
	public void Two_Connections_On_One_Transport_Stay_Distinct()
	{
		var first = new Peer(1);
		var second = new Peer(1);

		var state = new Dictionary<Peer, string>
		{
			[first] = "first",
			[second] = "second"
		};

		Assert.Equal(2, state.Count);
		Assert.NotEqual(first, second);
	}

	[Fact]
	public void Transport_Peer_Survives_Dispatch_With_Its_Handle()
	{
		var dispatcher = new PacketDispatcher();
		Peer? seen = null;

		dispatcher.OnSimplePacket += (peer, packet) => seen = peer;

		var peer = new SteamPeer(7, 76561198000000000);
		dispatcher.Accept(peer, new SimplePacket());

		Assert.Same(peer, seen);
		Assert.Equal(76561198000000000UL, Assert.IsType<SteamPeer>(seen).SteamId);
	}

	[Fact]
	public void Custom_Reader_Drains_A_Datagram_Using_Remaining()
	{
		var writer = new EzWriter();
		EzSerializer.Write(writer, new SimplePacket { favoriteNumber = 1, simpleText = "first" });
		EzSerializer.Write(writer, new SimplePacket { favoriteNumber = 2, simpleText = "second" });

		var transportBuffer = new byte[writer.Length + 4];
		writer.CopyTo(transportBuffer, 4);

		var reader = new SegmentReader(new ArraySegment<byte>(transportBuffer, 4, writer.Length));

		var dispatcher = new PacketDispatcher();
		var received = new List<SimplePacket>();

		dispatcher.OnSimplePacket += (peer, packet) => received.Add(packet);

		var peer = new SteamPeer(1, 76561198000000000);

		while (reader.Remaining > 0)
		{
			Assert.True(EzSerializer.TryRead(reader, out var packet));
			dispatcher.Accept(peer, packet!);
		}

		Assert.Equal(2, received.Count);
		Assert.Equal("first", received[0].simpleText);
		Assert.Equal("second", received[1].simpleText);
	}

	[Fact]
	public void Custom_Reader_Reports_Trailing_Bytes()
	{
		var padded = EzSerializer.Serialize(new SimplePacket { favoriteNumber = 1 })
			.Concat(new byte[] { 0xFF, 0xFF })
			.ToArray();

		var reader = new SegmentReader(new ArraySegment<byte>(padded));

		Assert.True(EzSerializer.TryRead(reader, out _));
		Assert.Equal(2, reader.Remaining);
	}

	[Fact]
	public void Custom_Reader_Rejects_A_Truncated_Datagram()
	{
		var valid = EzSerializer.Serialize(new SimplePacket { favoriteNumber = 1, simpleText = "hello" });

		for (var length = 0; length < valid.Length; length++)
		{
			var reader = new SegmentReader(new ArraySegment<byte>(valid, 0, length));

			Assert.False(EzSerializer.TryRead(reader, out var packet) && reader.Remaining == 0);
			Assert.Null(packet);
		}
	}
}
