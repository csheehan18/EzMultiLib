# EzMultiLib
I hate most networking solutions so I made my own.

## EzMultiLib Development Checklist

## EzMultiLib Development Roadmap

### 1. Packet & Protocol Core
- [x] Define `IPacket` interface
- [x] Explicit `Serialize / Deserialize` contract
- [x] Compile-time discovery of all `IPacket` implementations
- [x] Deterministic packet ID generation
- [x] Source-generated packet ID constants
- [x] Source-generated packet factory (`CreatePacket`)
- [x] Source-generated packet dispatcher (`AcceptPacket`)
- [x] Source-generated strongly typed events (`OnMovePacket`, etc.)
- [x] Zero reflection / zero runtime registration
- [x] Transport-agnostic protocol layer
- [x] End-to-end protocol test validating serialize → dispatch pipeline

### 2. Serialization System
- [x] `IPacketReader` / `IPacketWriter` abstractions
- [x] Primitive serialization support
- [ ] Built-in helper serializer (optional)
- [ ] Allocation optimizations
- [ ] Optional compression support

### 3. Server / Client Coordination
- [ ] `EzMultiServer`
- [ ] `EzMultiClient`
- [ ] Peer lifecycle ownership (created by server/client only)
- [ ] Automatic packet receive → `CreatePacket` → `AcceptPacket` wiring
- [ ] Broadcast helpers and targeted sends

### 4. Reliability Layer (Protocol-Level)
- [ ] Reliable packet flag
- [ ] Sequence numbers
- [ ] ACK packets
- [ ] Resend queues
- [ ] Packet ordering options
- [ ] Reliability diagnostics / debugging hooks

### 5. Transport Layer (Separate Projects)
- [ ] Transport interface definition
- [ ] UDP transport implementation
- [ ] In-memory transport (testing / simulation)
- [ ] Optional TCP / WebSocket transport
