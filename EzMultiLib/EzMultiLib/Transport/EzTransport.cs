using System.Net;
using System.Net.Sockets;

namespace EzMultiLib.Transport
{
    public class EzTransport
    {
        private UdpClient udp;
		public event Action<IPEndPoint, byte[]>? OnData;

		public EzTransport(int port)
        {
            udp = new UdpClient(port);
            AcceptPackets();
        }

        private async void AcceptPackets()
        {
            while (true)
            {
                UdpReceiveResult result = await udp.ReceiveAsync();
                OnData?.Invoke(result.RemoteEndPoint, result.Buffer);
            }
        }

        public void Send(IPEndPoint endpoint, byte[] data)
        {
            udp.Send(data, data.Length, endpoint);
        }

        public void Dispose()
        {
            udp.Dispose();
        }
    }
}
