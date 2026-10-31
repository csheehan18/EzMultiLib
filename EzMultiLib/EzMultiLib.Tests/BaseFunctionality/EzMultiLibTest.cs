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

	[Fact]
	public void A_Throwing_Handler_Does_Not_Unwind_The_Receive_Loop()
	{
		var dispatcher = new PacketDispatcher();
		var peer = new Peer(42);
		var delivered = 0;

		Peer? reportedPeer = null;
		IPacket? reportedPacket = null;
		Exception? reportedError = null;

		dispatcher.OnSimplePacket += (p, packet) =>
		{
			delivered++;

			if (packet.favoriteNumber == 2)
				throw new InvalidOperationException("boom");
		};

		dispatcher.HandlerFailed += (p, packet, error) =>
		{
			reportedPeer = p;
			reportedPacket = packet;
			reportedError = error;
		};

		for (var n = 1; n <= 4; n++)
			dispatcher.Accept(peer, new SimplePacket { favoriteNumber = n });

		Assert.Equal(4, delivered);
		Assert.Same(peer, reportedPeer);
		Assert.Equal(2, Assert.IsType<SimplePacket>(reportedPacket).favoriteNumber);
		Assert.Equal("boom", Assert.IsType<InvalidOperationException>(reportedError).Message);
	}

	[Fact]
	public void Without_The_Hook_A_Throwing_Handler_Still_Propagates()
	{
		var dispatcher = new PacketDispatcher();

		dispatcher.OnSimplePacket += (peer, packet) => throw new InvalidOperationException("boom");

		Assert.Throws<InvalidOperationException>(
			() => dispatcher.Accept(new Peer(1), new SimplePacket()));
	}

	[Fact]
	public void The_Hook_Does_Not_Swallow_An_Unknown_Packet_Id()
	{
		var dispatcher = new PacketDispatcher();

		dispatcher.HandlerFailed += (peer, packet, error) => { };

		Assert.Throws<ArgumentException>(
			() => dispatcher.Accept(new Peer(1), PacketAction.SimplePacketId + 1, new SimplePacket()));
	}

	[Fact]
	public void Clear_Handlers_Detaches_The_Hook_Too()
	{
		var dispatcher = new PacketDispatcher();

		dispatcher.OnSimplePacket += (peer, packet) => throw new InvalidOperationException("boom");
		dispatcher.HandlerFailed += (peer, packet, error) => { };
		dispatcher.ClearHandlers();

		dispatcher.OnSimplePacket += (peer, packet) => throw new InvalidOperationException("boom");

		Assert.Throws<InvalidOperationException>(
			() => dispatcher.Accept(new Peer(1), new SimplePacket()));
	}

	[Fact]
	public void Collections_And_Nested_Packets_Round_Trip()
	{
		var sent = new InventoryPacket
		{
			ids = new[] { 1, 2, 3 },
			weights = new List<float> { 1.5f, 2.5f },
			labels = new[] { "a", null, "ccc" },
			teams = new[] { Team.Red, Team.Blue },
			equipped = new ItemStack { count = 7, name = "sword" },
			stacks = new[] { new ItemStack { count = 8, name = "x" }, null },
			spares = new List<ItemStack> { new ItemStack { count = 9, name = null } },
			origin = new Point { x = 10f, y = 20f },
			route = new List<Point> { new Point { x = 1, y = 2 }, new Point { x = 3, y = 4 } }
		};

		var back = (InventoryPacket)EzSerializer.Deserialize(EzSerializer.Serialize(sent));

		Assert.Equal(sent.ids, back.ids);
		Assert.Equal(sent.weights, back.weights);
		Assert.Equal(sent.labels, back.labels);
		Assert.Equal(sent.teams, back.teams);
		Assert.Equal(7, back.equipped!.count);
		Assert.Equal("sword", back.equipped.name);
		Assert.Equal(8, back.stacks![0]!.count);
		Assert.Null(back.stacks[1]);
		Assert.Null(back.spares![0].name);
		Assert.Equal(20f, back.origin.y);
		Assert.Equal(4f, back.route![1].y);
	}

	[Fact]
	public void Null_And_Empty_Collections_Stay_Distinct()
	{
		var sent = new InventoryPacket
		{
			ids = Array.Empty<int>(),
			spares = new List<ItemStack>(),
			labels = null,
			weights = null,
			equipped = null
		};

		var back = (InventoryPacket)EzSerializer.Deserialize(EzSerializer.Serialize(sent));

		Assert.Empty(back.ids!);
		Assert.Empty(back.spares!);
		Assert.Null(back.labels);
		Assert.Null(back.weights);
		Assert.Null(back.equipped);
	}

	[Fact]
	public void A_Count_Bigger_Than_The_Buffer_Is_Rejected_Without_Allocating_For_It()
	{
		// id, a presence flag, then a count claiming 65535 ints and no data at all.
		var hostile = new byte[7];
		BitConverter.GetBytes(PacketAction.BigListPacketId).CopyTo(hostile, 0);
		hostile[4] = 1;
		hostile[5] = 0xFF;
		hostile[6] = 0xFF;

		Assert.False(EzSerializer.TryDeserialize(hostile, out var packet));
		Assert.Null(packet);

		var before = GC.GetAllocatedBytesForCurrentThread();

		for (var i = 0; i < 100; i++)
			EzSerializer.TryDeserialize(hostile, out _);

		// Reserving for the claimed count would be 100 * 65535 ints here.
		Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1_000_000);
	}

	[Fact]
	public void Every_Truncation_Of_A_Collection_Packet_Is_Rejected()
	{
		var valid = EzSerializer.Serialize(new InventoryPacket
		{
			ids = new[] { 1, 2 },
			labels = new[] { "hello" },
			stacks = new[] { new ItemStack { count = 1, name = "a" } }
		});

		for (var length = 0; length < valid.Length; length++)
		{
			Assert.False(EzSerializer.TryDeserialize(valid.Take(length).ToArray(), out var packet));
			Assert.Null(packet);
		}
	}

	[Fact]
	public void Field_Numbers_Order_The_Wire_Not_Field_Names()
	{
		var bytes = EzSerializer.Serialize(new TaggedPacket { zeta = 0x11111111, alpha = 0x22222222 });

		// The id, then how many optional fields follow, then field 1 ahead of field 2
		// even though 'alpha' sorts before 'zeta'.
		Assert.Equal(PacketAction.TaggedPacketId, BitConverter.ToUInt32(bytes, 0));
		Assert.Equal(1, bytes[4]);
		Assert.Equal(0x11111111, BitConverter.ToInt32(bytes, 5));
		Assert.Equal(0x22222222, BitConverter.ToInt32(bytes, 9));
	}

	[Fact]
	public void A_Numbered_Packet_Round_Trips()
	{
		var back = (TaggedPacket)EzSerializer.Deserialize(
			EzSerializer.Serialize(new TaggedPacket { zeta = 1, alpha = 2, later = 3 }));

		Assert.Equal(1, back.zeta);
		Assert.Equal(2, back.alpha);
		Assert.Equal(3, back.later);
	}

	[Fact]
	public void An_Older_Sender_Leaves_A_Newer_Optional_Field_At_Its_Default()
	{
		var full = EzSerializer.Serialize(new TaggedPacket { zeta = 1, alpha = 2, later = 99 });

		// The same packet as a build that never had field 3 would have sent it: one
		// fewer optional field, and four fewer bytes on the tail.
		var older = full.Take(full.Length - sizeof(int)).ToArray();
		older[4] = 0;

		Assert.True(EzSerializer.TryDeserialize(older, out var packet));

		var typed = Assert.IsType<TaggedPacket>(packet);

		Assert.Equal(1, typed.zeta);
		Assert.Equal(2, typed.alpha);
		Assert.Equal(0, typed.later);
	}

	[Fact]
	public void A_Newer_Sender_With_Unknown_Optional_Fields_Is_Rejected()
	{
		var full = EzSerializer.Serialize(new TaggedPacket { zeta = 1, alpha = 2, later = 99 });

		// A build carrying one more optional field than this one. Its size is unknown,
		// so the tail cannot be stepped over and the packet has to be refused rather
		// than read as something it is not.
		var newer = full.Concat(new byte[sizeof(int)]).ToArray();
		newer[4] = 2;

		Assert.False(EzSerializer.TryDeserialize(newer, out var packet));
		Assert.Null(packet);
	}

	[Fact]
	public void Truncating_A_Numbered_Packet_Is_Still_Rejected()
	{
		var valid = EzSerializer.Serialize(new TaggedPacket { zeta = 1, alpha = 2, later = 3 });

		for (var length = 0; length < valid.Length; length++)
		{
			Assert.False(EzSerializer.TryDeserialize(valid.Take(length).ToArray(), out var packet));
			Assert.Null(packet);
		}
	}

	[Fact]
	public void Delivery_Defaults_To_Reliable_Ordered()
	{
		var delivery = PacketAction.GetDelivery(new SimplePacket());

		Assert.Equal(Delivery.ReliableOrdered, delivery.Method);
		Assert.Equal(0, delivery.Channel);
	}

	[Fact]
	public void Declared_Delivery_And_Channel_Are_Reported()
	{
		var move = PacketAction.GetDelivery(new MovePacket());
		var chat = PacketAction.GetDelivery(new ChatPacket());

		Assert.Equal(Delivery.Sequenced, move.Method);
		Assert.Equal(0, move.Channel);
		Assert.Equal(Delivery.ReliableOrdered, chat.Method);
		Assert.Equal(3, chat.Channel);
	}

	[Fact]
	public void Delivery_Can_Be_Looked_Up_By_The_Id_A_Send_Already_Has()
	{
		var writer = new EzWriter();

		EzSerializer.Write(writer, new ChatPacket { text = "hi" }, out var id);

		Assert.Equal(PacketAction.ChatPacketId, id);
		Assert.Equal(3, PacketAction.GetDelivery(id).Channel);
	}

	[Fact]
	public void Declaring_Delivery_Costs_Nothing_On_The_Wire()
	{
		// Two floats and the id, with nothing added for the declaration.
		Assert.Equal(
			PacketFramer.HeaderSize + sizeof(float) + sizeof(float),
			EzSerializer.Serialize(new MovePacket()).Length);
	}

	[Fact]
	public void A_Plain_Peer_Cannot_Be_Kicked()
	{
		// Better to say so than to look like a kick that worked.
		var error = Assert.Throws<NotSupportedException>(() => new Peer(1).Kick());

		Assert.Contains("Kick", error.Message);
	}

	[Fact]
	public void A_Transport_Peer_Kicks_Through_Its_Override()
	{
		var peer = new KickablePeer(1);

		Assert.False(peer.Kicked);

		// Reached through the base type, the way a receive loop has it.
		((Peer)peer).Kick();

		Assert.True(peer.Kicked);
	}

	[Fact]
	public void Protocol_Hash_Covers_The_Shared_Packets()
	{
		// Pinned deliberately. It moves when a shared packet's id, field names, field
		// order or field types change, and local packets never move it. A failure here
		// means the wire contract shifted and older builds will not agree with this one.
		Assert.Equal(1838504802u, PacketAction.ProtocolHash);
		Assert.NotEqual(0u, PacketAction.ProtocolHash);
	}

	[Fact]
	public void Read_Yields_The_Id_So_Dispatch_Needs_No_Lookup()
	{
		var reader = new EzReader(EzSerializer.Serialize(new SimplePacket { favoriteNumber = 8 }));

		Assert.True(EzSerializer.TryRead(reader, out var packet, out var id));
		Assert.Equal(PacketAction.SimplePacketId, id);

		var dispatcher = new PacketDispatcher();
		SimplePacket? seen = null;

		dispatcher.OnSimplePacket += (peer, p) => seen = p;
		dispatcher.Accept(new Peer(1), id, packet!);

		Assert.Equal(8, seen?.favoriteNumber);
	}

	[Fact]
	public void A_Packet_Deriving_From_Another_Packet_Keeps_Its_Own_Identity()
	{
		var sent = new ExtendedPacket { baseNumber = 3, extra = 7 };

		Assert.Equal(PacketAction.ExtendedPacketId, PacketAction.GetPacketId(sent));

		var typed = Assert.IsType<ExtendedPacket>(
			EzSerializer.Deserialize(EzSerializer.Serialize(sent)));

		Assert.Equal(3, typed.baseNumber);
		Assert.Equal(7, typed.extra);

		var dispatcher = new PacketDispatcher();
		var baseFired = 0;
		var extendedFired = 0;

		dispatcher.OnConcreteBasePacket += (peer, p) => baseFired++;
		dispatcher.OnExtendedPacket += (peer, p) => extendedFired++;

		dispatcher.Accept(new Peer(1), sent);

		Assert.Equal(0, baseFired);
		Assert.Equal(1, extendedFired);
	}
}

// A concrete base that sorts before its subclass, which is the arrangement that
// used to make the generated type switch unreachable.
public class ConcreteBasePacket : IPacket
{
	public int baseNumber;
}

public class ExtendedPacket : ConcreteBasePacket
{
	public int extra;
}

public class ItemStack : IPacket
{
	public int count;
	public string? name;
}

public struct Point : IPacket
{
	public float x;
	public float y;
}

public class InventoryPacket : IPacket
{
	public ItemStack? equipped;
	public int[]? ids;
	public string?[]? labels;
	public Point origin;
	public List<Point>? route;
	public List<ItemStack>? spares;
	public ItemStack?[]? stacks;
	public Team[]? teams;
	public List<float>? weights;
}

public class BigListPacket : IPacket
{
	public int[]? values;
}

// Numbered on purpose with names that sort against the numbers, so the tests can
// tell which of the two decides the order.
public class TaggedPacket : IPacket
{
	[Field(1)] public int zeta;
	[Field(2)] public int alpha;
	[Field(3, Optional = true)] public int later;
}

[Delivery(Delivery.Sequenced)]
public class MovePacket : IPacket
{
	public float x;
	public float y;
}

[Delivery(Delivery.ReliableOrdered, Channel = 3)]
public class ChatPacket : IPacket
{
	public string? text;
}

public sealed class KickablePeer : Peer
{
	public bool Kicked { get; private set; }

	public KickablePeer(int id)
		: base(id)
	{
	}

	public override void Kick() => Kicked = true;
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
