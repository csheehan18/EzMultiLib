using EzMultiLib.Packets;

namespace EzMultiLib.Serialization.Packets
{
    public static class PacketFramer
    {
        public static byte[] Frame(ushort id, IPacket packet)
        {
            var data = EzSerializer.Serialize(packet);
            var buffer = new byte[2 + data.Length];
            buffer[0] = (byte)(id >> 8);
            buffer[1] = (byte)(id & 0xFF);
            data.CopyTo(buffer, 2);
            return buffer;
        }

        public static (ushort id, byte[] data) Deframe(byte[] buffer)
        {
            var id = (ushort)((buffer[0] << 8) | buffer[1]);
            var data = buffer[2..];
            return (id, data);
        }
    }
}
