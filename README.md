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
Events are generated automatically for every IPacket class in your project.
```csharp
PacketAction.OnLoginPacket += (peer, packet) =>
{
    Console.WriteLine($"{packet.username} connected!");
};

// or
PacketAction.OnLoginPacket += HandleLogin;
void HandleLogin(Peer peer, LoginPacket packet) { }
```
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
    PacketAction.AcceptPacket(peer, packet);
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
    PacketAction.AcceptPacket(peer, packet);
```
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

## Supported Field Types (I'll add more in the future)
- bool
- sbyte, byte
- short, ushort
- int, uint
- long, ulong
- float, double
- string (null is preserved)
- any enum, written as its underlying type

Public instance fields are serialized, including inherited ones, ordered by name.
Collections, arrays and nested packets are not supported yet.
