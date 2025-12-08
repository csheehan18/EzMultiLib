# EzMultiLib
I hate most networking solutions so I made my own.

## EzMultiLib Development Checklist

### 1. Core Architecture
- [ ] Define IPacket interface
- [ ] Implement base Client class
- [ ] Implement EzTransport (UDP with optional reliability layer)
- [ ] Implement packet receive loop (async)
- [ ] Implement packet send pipeline
- [ ] Implement connection tracking + client registry

### 2. Serialization System
- [ ] Implement EzSerializer for packet encoding/decoding
- [ ] Support primitives, arrays, and custom structs
- [ ] Support user-defined packet classes automatically
- [ ] Optimize serialization for minimal allocations
- [ ] Support optional compression flag

### 3. Packet Auto-Registration
- [ ] Implement packet scanning (search for all IPacket implementations)
- [ ] Implement automated PacketAction.g.cs generation
- [ ] Generate strongly typed events: `OnMovePacket`, `OnLoginPacket`, etc.
- [ ] Generate `PacketAction.AcceptPacket(Client, IPacket)` dispatcher
- [ ] Implement dynamic regeneration when new packets appear (optional)
- [ ] Add partial class extensions for user overrides (optional)

### 4. Reliability Layer (Reliable UDP)
- [ ] Add sequence numbers for reliable packets
- [ ] Add acknowledgement system
- [ ] Add resend queue for lost packets
- [ ] Add configurable resend interval
- [ ] Add packet ordering option
- [ ] Add reliability debugging logs

### 5. Server Framework
- [ ] Implement EzServer class
- [ ] Simple API: `server.Start(port)`
- [ ] Built-in event hooks: OnClientConnected, OnClientDisconnected
- [ ] Forward packets to PacketAction automatically
- [ ] Implement broadcast helpers and targeted send

### 6. Client Framework
- [ ] Implement EzClient class
- [ ] Simple API: `client.Connect(host, port)`
- [ ] Auto-reconnect option (optional)
- [ ] Forward packets to PacketAction automatically
- [ ] Implement latency tracking + heartbeat pings
