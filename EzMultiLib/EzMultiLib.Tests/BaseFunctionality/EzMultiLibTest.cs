using EzMultiLib.Packets;
using EzMultiLib.Peers;
using EzMultiLib.Serialization;
using EzMultiLib.Serialization.Packets;

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

        var framed = PacketFramer.Frame(PacketAction.GetPacketId(outgoing), outgoing);

        var (id, data) = PacketFramer.Deframe(framed);
        var incoming = PacketAction.CreatePacket(id, data);
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
