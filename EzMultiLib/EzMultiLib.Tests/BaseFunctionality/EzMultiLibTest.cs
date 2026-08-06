using EzMultiLib.Packets;
using EzMultiLib.Peers;
using EzMultiLib.Serialization;
using EzMultiLib.Serialization.IO;
using EzMultiLib.Serialization.Packets;
using SharedProtocol;
using System.Linq;

public class EzMultiLibTest
{
	[Fact]
	public void Packet_Round_Trips_Through_Dispatch()
	{
		SimplePacket? receivedPacket = null;
		Peer? receivedPeer = null;

		void Handler(Peer? peer, SimplePacket packet)
		{
			receivedPeer = peer;
			receivedPacket = packet;
		}

		var dispatcher = new PacketDispatcher();
		dispatcher.OnSimplePacket += Handler;

		var outgoing = new SimplePacket
		{
			favoriteNumber = 1,
			simpleText = "hello"
		};

		dispatcher.Accept(null, EzSerializer.Deserialize(EzSerializer.Serialize(outgoing)));

		Assert.NotNull(receivedPacket);
		Assert.Equal(1, receivedPacket!.favoriteNumber);
		Assert.Equal("hello", receivedPacket!.simpleText);
		Assert.Null(receivedPeer);
	}

	[Fact]
	public void Every_Supported_Field_Type_Round_Trips()
	{
		var outgoing = new EveryTypePacket
		{
			flag = true,
			signedByte = -8,
			unsignedByte = 200,
			shortValue = -300,
			ushortValue = 40000,
			intValue = -70000,
			uintValue = 3000000000,
			longValue = -9000000000,
			ulongValue = 18000000000000000000,
			floatValue = 1.5f,
			doubleValue = -2.25,
			text = "round trip",
			team = Team.Blue
		};

		var incoming = (EveryTypePacket)EzSerializer.Deserialize(EzSerializer.Serialize(outgoing));

		Assert.True(incoming.flag);
		Assert.Equal(-8, incoming.signedByte);
		Assert.Equal(200, incoming.unsignedByte);
		Assert.Equal(-300, incoming.shortValue);
		Assert.Equal(40000, incoming.ushortValue);
		Assert.Equal(-70000, incoming.intValue);
		Assert.Equal(3000000000, incoming.uintValue);
		Assert.Equal(-9000000000, incoming.longValue);
		Assert.Equal(18000000000000000000, incoming.ulongValue);
		Assert.Equal(1.5f, incoming.floatValue);
		Assert.Equal(-2.25, incoming.doubleValue);
		Assert.Equal("round trip", incoming.text);
		Assert.Equal(Team.Blue, incoming.team);
	}

	[Fact]
	public void Null_String_Round_Trips_As_Null()
	{
		var incoming = (SimplePacket)EzSerializer.Deserialize(
			EzSerializer.Serialize(new SimplePacket { favoriteNumber = 7 }));

		Assert.Null(incoming.simpleText);
		Assert.Equal(7, incoming.favoriteNumber);
	}

	[Fact]
	public void Packet_Id_Is_Written_Into_The_Payload()
	{
		var bytes = EzSerializer.Serialize(new SimplePacket());

		Assert.Equal(PacketAction.SimplePacketId, BitConverter.ToUInt32(bytes, 0));
	}

	[Fact]
	public void Unknown_Packet_Id_Is_Rejected()
	{
		Assert.False(EzSerializer.TryDeserialize(BitConverter.GetBytes(9999u), out var packet));
		Assert.Null(packet);
		Assert.Throws<MalformedPacketException>(
			() => EzSerializer.Deserialize(BitConverter.GetBytes(9999u)));
	}

	[Fact]
	public void Every_Truncation_Of_A_Valid_Packet_Is_Rejected()
	{
		var valid = EzSerializer.Serialize(new EveryTypePacket { text = "a reasonably long string", floatValue = 3f });

		for (var length = 0; length < valid.Length; length++)
		{
			var truncated = valid.Take(length).ToArray();

			Assert.False(EzSerializer.TryDeserialize(truncated, out var packet), $"length {length} was accepted");
			Assert.Null(packet);
		}

		Assert.True(EzSerializer.TryDeserialize(valid, out _));
	}

	[Fact]
	public void Several_Packets_Can_Be_Read_From_One_Buffer()
	{
		var writer = new EzWriter();
		EzSerializer.Write(writer, new SimplePacket { favoriteNumber = 1, simpleText = "first" });
		EzSerializer.Write(writer, new SimplePacket { favoriteNumber = 2, simpleText = "second" });

		var reader = new EzReader(writer.ToArray());

		Assert.True(EzSerializer.TryRead(reader, out var first));
		Assert.True(EzSerializer.TryRead(reader, out var second));
		Assert.False(EzSerializer.TryRead(reader, out _));

		Assert.Equal("first", ((SimplePacket)first!).simpleText);
		Assert.Equal("second", ((SimplePacket)second!).simpleText);
	}

	[Fact]
	public void Trailing_Bytes_Are_Rejected()
	{
		var padded = EzSerializer.Serialize(new SimplePacket { favoriteNumber = 1 }).Concat(new byte[] { 0xFF }).ToArray();

		Assert.False(EzSerializer.TryDeserialize(padded, out _));
	}

	[Fact]
	public void Oversized_String_Length_Is_Rejected_Without_Allocating()
	{
		var hostile = new List<byte>();
		hostile.AddRange(BitConverter.GetBytes(PacketAction.SimplePacketId));
		hostile.AddRange(BitConverter.GetBytes(0));
		hostile.Add(1);
		hostile.AddRange(BitConverter.GetBytes(ushort.MaxValue));

		Assert.False(EzSerializer.TryDeserialize(hostile.ToArray(), out _));
	}

	[Fact]
	public void Null_And_Empty_Buffers_Are_Rejected()
	{
		Assert.False(EzSerializer.TryDeserialize(null!, out _));
		Assert.False(EzSerializer.TryDeserialize(Array.Empty<byte>(), out _));
		Assert.False(EzSerializer.TryDeserialize(new byte[] { 0x01 }, out _));
	}

	[Fact]
	public void Random_Bytes_Never_Throw()
	{
		var random = new Random(20260802);
		var accepted = 0;

		for (var i = 0; i < 20000; i++)
		{
			var buffer = new byte[random.Next(0, 64)];
			random.NextBytes(buffer);

			if (EzSerializer.TryDeserialize(buffer, out var packet))
			{
				accepted++;
				Assert.NotNull(packet);
			}
		}

		Assert.True(accepted < 20000);
	}

	[Fact]
	public void Corrupted_Valid_Packets_Never_Throw()
	{
		var random = new Random(11);
		var template = EzSerializer.Serialize(new EveryTypePacket { text = "corrupt me", intValue = 9 });

		for (var i = 0; i < 20000; i++)
		{
			var buffer = template.ToArray();
			buffer[random.Next(buffer.Length)] = (byte)random.Next(256);

			EzSerializer.TryDeserialize(buffer, out _);
		}
	}

	[Theory]
	[InlineData(PacketAction.SimplePacketId, 405039938u)]
	[InlineData(PacketAction.EveryTypePacketId, 1088620065u)]
	[InlineData(PacketAction.DerivedPacketId, 1756485217u)]
	public void Packet_Ids_Are_Pinned_To_Known_Values(uint actual, uint expected)
	{
		Assert.Equal(expected, actual);
	}

	[Fact]
	public void Declared_Packet_Id_Overrides_The_Hash()
	{
		Assert.Equal(4242u, PacketAction.PinnedPacketId);
	}

	[Fact]
	public void Declared_Packet_Id_Is_What_Goes_On_The_Wire()
	{
		var bytes = EzSerializer.Serialize(new PinnedPacket { value = 5 });

		Assert.Equal(4242u, BitConverter.ToUInt32(bytes, 0));
		Assert.Equal(5, ((PinnedPacket)EzSerializer.Deserialize(bytes)).value);
	}

	[Fact]
	public void Dispatchers_Do_Not_Share_Handlers()
	{
		var server = new PacketDispatcher();
		var client = new PacketDispatcher();

		var serverCalls = 0;
		var clientCalls = 0;

		server.OnSimplePacket += (peer, packet) => serverCalls++;
		client.OnSimplePacket += (peer, packet) => clientCalls++;

		server.Accept(null, new SimplePacket());

		Assert.Equal(1, serverCalls);
		Assert.Equal(0, clientCalls);

		client.Accept(null, new SimplePacket());

		Assert.Equal(1, serverCalls);
		Assert.Equal(1, clientCalls);
	}

	[Fact]
	public void Dispatcher_Routes_The_Peer_It_Was_Given()
	{
		var dispatcher = new PacketDispatcher();
		var expected = new List<Peer?>();

		dispatcher.OnSimplePacket += (peer, packet) => expected.Add(peer);

		dispatcher.Accept(null, new SimplePacket());

		Assert.Single(expected);
		Assert.Null(expected[0]);
	}

	[Fact]
	public void Clear_Handlers_Detaches_Every_Subscriber()
	{
		var dispatcher = new PacketDispatcher();
		var calls = 0;

		dispatcher.OnSimplePacket += (peer, packet) => calls++;
		dispatcher.OnEveryTypePacket += (peer, packet) => calls++;

		dispatcher.Accept(null, new SimplePacket());
		dispatcher.Accept(null, new EveryTypePacket());

		Assert.Equal(2, calls);

		dispatcher.ClearHandlers();

		dispatcher.Accept(null, new SimplePacket());
		dispatcher.Accept(null, new EveryTypePacket());

		Assert.Equal(2, calls);
	}

	[Fact]
	public void Accept_Packet_Forwards_To_The_Default_Dispatcher()
	{
		var calls = 0;

		void Handler(Peer? peer, PinnedPacket packet) => calls++;

		PacketAction.Default.OnPinnedPacket += Handler;

		try
		{
			PacketAction.AcceptPacket(null, new PinnedPacket());

			Assert.Equal(1, calls);
		}
		finally
		{
			PacketAction.Default.OnPinnedPacket -= Handler;
		}
	}

	[Fact]
	public void Header_Is_A_Four_Byte_Id()
	{
		Assert.Equal(4, PacketFramer.HeaderSize);
		Assert.Equal(PacketFramer.HeaderSize + sizeof(int), EzSerializer.Serialize(new PinnedPacket()).Length);
	}

	[Fact]
	public void Declared_Id_Above_The_Old_Sixteen_Bit_Ceiling_Round_Trips()
	{
		var bytes = EzSerializer.Serialize(new WidePinnedPacket { value = 6 });

		Assert.Equal(3000000000u, PacketAction.WidePinnedPacketId);
		Assert.Equal(3000000000u, BitConverter.ToUInt32(bytes, 0));
		Assert.Equal(6, ((WidePinnedPacket)EzSerializer.Deserialize(bytes)).value);
	}

	[Fact]
	public void Packet_Ids_Are_Unique()
	{
		var ids = new[]
		{
			PacketAction.SimplePacketId,
			PacketAction.EveryTypePacketId,
			PacketAction.DerivedPacketId,
			PacketAction.PinnedPacketId,
			PacketAction.WidePinnedPacketId,
			PacketAction.SharedLoginPacketId,
			PacketAction.SharedPinnedPacketId
		};

		Assert.Equal(ids.Length, ids.Distinct().Count());
	}

	[Fact]
	public void Zero_Is_Never_Assigned_As_A_Packet_Id()
	{
		Assert.All(
			new[]
			{
				PacketAction.SimplePacketId,
				PacketAction.EveryTypePacketId,
				PacketAction.DerivedPacketId,
				PacketAction.PinnedPacketId
			},
			id => Assert.NotEqual(0u, id));
	}

	[Fact]
	public void Packet_From_A_Referenced_Assembly_Round_Trips()
	{
		SharedLoginPacket? receivedPacket = null;

		void Handler(Peer? peer, SharedLoginPacket packet) => receivedPacket = packet;

		var dispatcher = new PacketDispatcher();
		dispatcher.OnSharedLoginPacket += Handler;

		var outgoing = new SharedLoginPacket { username = "Alice", playerId = 7 };
		dispatcher.Accept(null, EzSerializer.Deserialize(EzSerializer.Serialize(outgoing)));

		Assert.NotNull(receivedPacket);
		Assert.Equal("Alice", receivedPacket!.username);
		Assert.Equal(7, receivedPacket!.playerId);
	}

	[Fact]
	public void Referenced_Packet_Id_Matches_Its_Name_Hash()
	{
		Assert.Equal(2964381181u, PacketAction.SharedLoginPacketId);
	}

	[Fact]
	public void Internal_Packets_In_A_Referenced_Assembly_Are_Not_Exposed()
	{
		var events = typeof(PacketDispatcher).GetEvents().Select(e => e.Name).ToArray();

		Assert.Contains("OnSharedLoginPacket", events);
		Assert.DoesNotContain("OnInternalPacket", events);
	}

	[Fact]
	public void Declared_Id_On_A_Referenced_Packet_Is_Honoured()
	{
		var bytes = EzSerializer.Serialize(new SharedPinnedPacket { health = 12.5f });

		Assert.Equal(9001u, PacketAction.SharedPinnedPacketId);
		Assert.Equal(9001u, BitConverter.ToUInt32(bytes, 0));
		Assert.Equal(12.5f, ((SharedPinnedPacket)EzSerializer.Deserialize(bytes)).health);
	}

	[Fact]
	public void Local_And_Referenced_Packets_Coexist()
	{
		var local = EzSerializer.Deserialize(EzSerializer.Serialize(new SimplePacket { favoriteNumber = 3 }));
		var shared = EzSerializer.Deserialize(EzSerializer.Serialize(new SharedLoginPacket { playerId = 4 }));

		Assert.IsType<SimplePacket>(local);
		Assert.IsType<SharedLoginPacket>(shared);
	}

	[Fact]
	public void Inherited_Fields_Are_Serialized()
	{
		var incoming = (DerivedPacket)EzSerializer.Deserialize(
			EzSerializer.Serialize(new DerivedPacket { baseValue = 11, derivedValue = 22 }));

		Assert.Equal(11, incoming.baseValue);
		Assert.Equal(22, incoming.derivedValue);
	}
}

public enum Team : byte
{
	Red = 1,
	Blue = 2
}

public class SimplePacket : IPacket
{
	public int favoriteNumber;
	public string? simpleText;
}

[PacketId(4242)]
public class PinnedPacket : IPacket
{
	public int value;
}

[PacketId(3_000_000_000)]
public class WidePinnedPacket : IPacket
{
	public int value;
}

public class EveryTypePacket : IPacket
{
	public bool flag;
	public sbyte signedByte;
	public byte unsignedByte;
	public short shortValue;
	public ushort ushortValue;
	public int intValue;
	public uint uintValue;
	public long longValue;
	public ulong ulongValue;
	public float floatValue;
	public double doubleValue;
	public string? text;
	public Team team;
}

public abstract class BasePacket : IPacket
{
	public int baseValue;
}

public class DerivedPacket : BasePacket
{
	public int derivedValue;
}
