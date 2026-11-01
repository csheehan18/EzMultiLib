# EzMultiLib
I hate most networking solutions so I made my own. Built transport-agnostically 
so it works with whatever you prefer. So you could hook in whatever you want. I will 
eventually make a UDP transport as EzMultiLib.Transport

## How It Works
Define a packet, subscribe to an event, done. A Roslyn source generator writes the
serializer for every IPacket class at compile time, so there is no reflection and no
boxing at runtime, just direct field reads and writes. Unsupported field types are
reported as build errors rather than runtime exceptions.

## Define Packets
Just implement IPacket its as simple as that
Fields are serialized automatically.
```csharp
public class LoginPacket : IPacket
{
    public string username;
    public int playerId;
}
```
## Subscribe To Events
Handlers live on a `PacketDispatcher`, which is generated with one event per IPacket
class in your project. `PacketAction.Default` is a ready made one for when you only
need a single endpoint.
```csharp
PacketAction.Default.OnLoginPacket += (peer, packet) =>
{
    Console.WriteLine($"{packet.username} connected!");
};

// or
PacketAction.Default.OnLoginPacket += HandleLogin;
void HandleLogin(Peer peer, LoginPacket packet) { }
```
Make your own when one process is more than one endpoint. A client and a server in
the same process need a dispatcher each, otherwise every handler sees every packet
no matter which socket it arrived on:
```csharp
var server = new PacketDispatcher();
var client = new PacketDispatcher();

server.OnChatPacket += (peer, chat) => BroadcastToAll(chat);
client.OnChatPacket += (peer, chat) => chatUI.Append(chat.text);
```
One dispatcher per endpoint, not per peer. A server has one and all of its peers
share it, which is what the `Peer` argument is for. `ClearHandlers()` detaches
everything at once, for teardown that would otherwise leak subscribers.

A handler that throws would otherwise unwind straight into your receive loop and take
the tick with it. Subscribe `HandlerFailed` and the exception comes to you instead,
and the next packet still gets delivered:
```csharp
server.HandlerFailed += (peer, packet, error) =>
    Log.Error($"{packet.GetType().Name} from {peer} failed: {error}");
```
Leave it unsubscribed and the exception propagates the way it always did, rather than
disappearing where nobody can see it. Attaching it costs nothing per packet: the
invocation list is never copied and dispatch still allocates zero. One caveat, a
throwing handler does still shortcut any later subscriber to that same event, so keep
one handler per packet if that matters to you. `ClearHandlers()` detaches this too.
## Send A Packet
The packet id is written into the payload, so a transport only has to move the bytes.
```csharp
var bytes = EzSerializer.Serialize(new LoginPacket { username = "Alice", playerId = 1 });
```
## Receive A Packet
Bytes off the network are untrusted, so the receive path never throws on bad input. Use
`TryDeserialize` in your server loop and drop whatever fails.
```csharp
if (EzSerializer.TryDeserialize(bytes, out var packet))
    dispatcher.Accept(peer, packet);
else
    peer.Kick();
```
A truncated buffer, a garbage buffer, an unknown packet id, a string claiming to be
longer than the data, or trailing bytes are all rejected without allocating and without
throwing. `Deserialize` is the same thing but throws `MalformedPacketException`, which is
fine for tests and local tools and a bad idea on a socket: exceptions are roughly 150x
more expensive than a rejection, so a client can hurt you just by sending junk.

If your transport already has its own buffers, write and read through `IPacketWriter` /
`IPacketReader` instead to skip the intermediate array. `TryRead` reads one packet and
leaves the rest alone, so you can pack several into one datagram:
```csharp
EzSerializer.Write(writer, packet);

while (EzSerializer.TryRead(reader, out var packet))
    dispatcher.Accept(peer, packet);
```

`Accept` has to look the packet's id up by its type. On a receive loop that is wasted
work, because `TryRead` already read the id off the wire. Take it and hand it straight
to `Accept` to skip the lookup:
```csharp
while (EzSerializer.TryRead(reader, out var packet, out var id))
    dispatcher.Accept(peer, id, packet);
```
Dispatch is a jump table on the id either way, so it costs the same whether you have
five packet types or five hundred.
## Peers
`Peer` is the handle your handlers are given. It carries an id and nothing else, so a
transport subclasses it to carry the connection itself:
```csharp
public sealed class LiteNetPeer : Peer
{
    public NetPeer Connection { get; }

    public LiteNetPeer(NetPeer connection)
        : base(connection.Id)
    {
        Connection = connection;
    }

    public override void Kick() => Connection.Disconnect();
}
```
That override is what makes `peer.Kick()` above do anything. `Kick()` on a plain `Peer`
throws, because a kick that quietly does not happen leaves a misbehaving client
connected and tells you nothing.

Handlers take the base type, so casting back is how you reach the connection:
```csharp
dispatcher.OnChatPacket += (peer, chat) =>
{
    var connection = ((LiteNetPeer)peer!).Connection;
};
```
Ids come from the transport and are only unique inside it, so two transports in one
process can both hand out peer 1. Peers compare by reference rather than by id, which
is what keeps those two from collapsing into one player.

## Delivery
Not everything wants the same guarantees. A position update is better dropped than
delivered late; chat has to arrive. Declare it on the packet:
```csharp
[Delivery(Delivery.Sequenced)]
public class MovePacket : IPacket
{
    public float x;
    public float y;
}

[Delivery(Delivery.ReliableOrdered, Channel = 2)]
public class ChatPacket : IPacket
{
    public string? text;
}
```
With no attribute a packet is `ReliableOrdered` on channel 0, so nothing becomes
droppable unless you said so. Channels keep unrelated reliable traffic off each other's
backs: a bulk inventory sync on its own channel will not stall chat behind it.

Reading it back is a jump table on the packet id, so it costs the same with five packet
types or five hundred. `Write` hands you the id it just used, so there is nothing to
look up twice:
```csharp
writer.Reset();
EzSerializer.Write(writer, packet, out var id);
writer.CopyTo(buffer, 0);

var delivery = PacketAction.GetDelivery(id);
```
`GetDelivery(packet)` is there too and finds the id for you.

Nothing here touches a transport and the declaration adds no bytes to the packet. Map it
where you send. For LiteNetLib:
```csharp
static DeliveryMethod Map(Delivery delivery) => delivery switch
{
    Delivery.ReliableOrdered   => DeliveryMethod.ReliableOrdered,
    Delivery.ReliableUnordered => DeliveryMethod.ReliableUnordered,
    Delivery.ReliableSequenced => DeliveryMethod.ReliableSequenced,
    Delivery.Sequenced         => DeliveryMethod.Sequenced,
    _                          => DeliveryMethod.Unreliable
};

peer.Send(buffer, 0, writer.Length, delivery.Channel, Map(delivery.Method));
```
Give `NetManager.ChannelsCount` enough room for the highest channel you declare.

Delivery is a send side choice and changes no byte of the payload, so it stays out of
`ProtocolHash`. The two sides do not have to agree on it, and retuning it on one of them
will not turn the other away.

## Shared Packet
The real power of this library comes from a shared project. Define your packets once, 
reference the dll from both your server and client. The IDs are assigned 
deterministically at compile time so both sides always agree without 
any handshake or negotiation.

Packets have to be `public` to cross an assembly boundary. Internal ones stay private
to the project that declares them.

There are two ways to arrange it. Put the generator on your client and server, and keep
the shared project as plain packet definitions:
```
SharedPackets      (packets only, references EzMultiLib)
  Server           (generator, references SharedPackets)
  Client           (generator, references SharedPackets)
```
Each side generates its own copy and both land on the same ids. Client and server can
each add their own local packets without disturbing the shared ones.

Or compile the protocol once, in the shared project, and let both sides consume it:
```
SharedPackets      (generator, packets, references EzMultiLib + EzMultiLib.Serialization)
  Server           (references SharedPackets)
  Client           (references SharedPackets)
```
Here neither side may declare packets of its own, since the shared protocol was already
generated without them. If you try, you get a build error telling you so.

## Packet IDs
An id is derived from the packet's type name, so it does not change when you add,
remove or reorder packets. Only renaming a packet changes its id. A client built
one patch behind still agrees with the server about every packet they share.

Ids are 32 bit and go on the wire as a 4 byte little endian header. Deriving the id
from the name is what lets both sides agree without a handshake, but it also means
two names can hash to the same id, and the odds of that grow with the square of your
packet count. A 32 bit space keeps it negligible: roughly 1 in a million at 100
packet types, where a 16 bit space would have been about 1 in 14.

If two packets happen to hash to the same id you get a build error, and you pin one
of them:
```csharp
[PacketId(1234)]
public class LoginPacket : IPacket
{
    public string username;
}
```
Pinning is also how you rename a packet without breaking the wire format. Id 0 is
reserved.

## Protocol Hash
An id survives adding and reordering packets, but it says nothing about the shape of
the body behind it. Fields go on the wire in name order unless you number them, so
renaming one reorders the payload while the id and the length stay exactly the same.
Two builds that disagree about a field name would read each other's fields in the wrong
order and never notice.

`PacketAction.ProtocolHash` is a compile time constant over every shared packet's id,
field names, field order and field types. Both sides derive the same number from the
same definitions, so compare it when a connection opens. With LiteNetLib it goes
straight in the connect key:
```csharp
// client
client.Connect(host, port, PacketAction.ProtocolHash.ToString());

// server
listener.ConnectionRequestEvent += request =>
    request.AcceptIfKey(PacketAction.ProtocolHash.ToString());
```
A client built against different packet definitions is turned away at connect instead
of desyncing an hour later.

Only shared packets count, so a client and a server can still each declare local
packets of their own without drifting apart. With no shared project every packet
counts, since both sides are built from the same source anyway.

## Field Numbers
Name ordering is fine until you need to change a packet. Number its fields instead and
the order comes from the numbers, so renaming a field stops being a wire change at all:
```csharp
public class MovePacket : IPacket
{
    [Field(1)] public int x;
    [Field(2)] public int y;
}
```
Number every field of a packet or none of them, running from 1 with no gaps and no
duplicates. A packet with no numbers keeps name ordering and the exact bytes it always
had, so nothing you have already written moves.

Contiguity is not fussiness. It is what makes deleting a field a build error instead of
a silent shift of everything behind it.

An unnumbered packet reports `EZML012` to say so, because a rename that quietly moves
your bytes is not something you should have to already know about. It is informational,
since a project full of unnumbered packets is not broken. Turn it up once the packets
that matter are numbered:
```
# .editorconfig
[*.cs]
dotnet_diagnostic.EZML012.severity = warning
```

### Adding A Field Later
Mark an appended field optional and a build that predates it can still talk to one that
has it:
```csharp
public class MovePacket : IPacket
{
    [Field(1)] public int x;
    [Field(2)] public int y;
    [Field(3, Optional = true)] public int z;   // added a patch later
}
```
A numbered packet carries one byte saying how many optional fields follow, which is what
lets a reader tell a field the sender never had from a field that got truncated.
Optional fields come after every required one, and they are read back in order:

- An older sender sends fewer of them, and the ones it did not know stay at their
  default. `z` comes back as 0.
- A newer sender sends more. Their sizes are unknown, so the tail cannot be stepped over
  and the packet is refused rather than misread. Update servers before clients.

`ProtocolHash` covers the required fields and leaves the optional tail out, so appending
one does not lock out older builds. It also leaves field names out once a packet is
numbered, because by then a rename is not a change to anything.

Optional fields are append only. Nothing can see you remove, reorder or retype one that
has already shipped, because tolerating a different number of them is the whole point.
Add to the end and leave what is there alone.

## Nested Packets And Collections
A field can be another packet type. It goes on the wire inline, with no id of its own,
so nesting costs nothing:
```csharp
public class ItemStack : IPacket
{
    public int count;
    public string? name;
}

public class InventoryPacket : IPacket
{
    public ItemStack? equipped;
    public ItemStack[]? stacks;
    public List<float>? weights;
}
```
A nested type is still a packet, so it also gets an id and a dispatcher event of its
own. Ignore them, or send it on its own when that happens to be useful.

`null` and empty are different values and both survive the round trip, for the
collection itself and for every element in it. A length goes on the wire as a ushort,
so 65535 elements is the ceiling. Reads never reserve for a claimed count, so a
datagram that says it carries 65535 items and then stops allocates nothing for the
ones it does not have.

Arrays and lists share a wire format, so trading `T[]` for `List<T>` is not a protocol
change and the hash stays where it was. One level deep only: `int[][]` and
`List<List<int>>` are build errors, and so is a packet that contains itself, directly
or through another packet.

## Supported Field Types (I'll add more in the future)
- bool
- sbyte, byte
- short, ushort
- int, uint
- long, ulong
- float, double
- string (null is preserved)
- any enum, written as its underlying type
- another packet type, written inline
- `T[]` or `List<T>` of any of the above

Public instance fields are serialized, including inherited ones, ordered by name.
