# EzMultiLib
I hate most networking solutions so I made my own. Built transport-agnostically 
so it works with whatever you prefer. I will eventually make a UDP transport 
as EzMultiLib.Transport. So you could hook in whatever you want.

## How It Works
Define a packet, subscribe to an event, done. It handles serialization 
and dispatch automatically behind the scenes using a Roslyn source generator 
and a reflection-based serializer which occurs once during each new packet type and results
are cached and reused for all purposes.

## Define Packets
Just implement IPacket — no attributes, no registration, no boilerplate.
Fields are serialized automatically.

public class LoginPacket : IPacket
{
    public string username;
    public int playerId;
}

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
```csharp
var bytes = EzSerializer.Serialize(new LoginPacket { username = "Alice", playerId = 1 });
```
## Shared Packet
The real power comes from a shared project. Define your packets once, 
reference the dll from both your server and client — IDs are assigned 
deterministically at compile time so both sides always agree without 
any handshake or negotiation.

SharedPackets.dll
    LoginPacket  → ID 1
    MovePacket   → ID 2


## Supported Field Types
- int
- ushort
- byte
- bool
- string
