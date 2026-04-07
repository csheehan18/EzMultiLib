using EzMultiLib.Packets;
using EzMultiLib.Peers;
using EzMultiLib.Serialization;

public class EzMultiLibTest
{
    [Fact]
    public void Serializer_Test()
    {
        SimplePacket? receivedPacket = null;
        Peer? emptyPeer = null;

        PacketAction.OnSimplePacket += (peer, packet) =>
        {
            emptyPeer = peer;
            receivedPacket = packet;
        };

        var outgoing = new SimplePacket
        {
            favoriteNumber = 1,
            simpleText = "hello"
        };

        // Serialize to bytes, prepend the packet ID
        var packetBytes = EzSerializer.Serialize(outgoing);
        var buffer = new byte[2 + packetBytes.Length];
        var id = PacketAction.GetPacketId(outgoing);
        buffer[0] = (byte)(id >> 8);
        buffer[1] = (byte)(id & 0xFF);
        packetBytes.CopyTo(buffer, 2);

        // Read incoming
        var idFromBuffer = (ushort)((buffer[0] << 8) | buffer[1]);
        var dataFromBuffer = buffer[2..];

        var incoming = PacketAction.CreatePacket(idFromBuffer, dataFromBuffer);
        PacketAction.AcceptPacket(null, incoming);

        Assert.NotNull(receivedPacket);
        Assert.Equal("hello", receivedPacket!.simpleText);
        Assert.Equal(1, receivedPacket!.favoriteNumber);
    }
}

public class SimplePacket : IPacket
{
	public int favoriteNumber;
	public string? simpleText;
}
