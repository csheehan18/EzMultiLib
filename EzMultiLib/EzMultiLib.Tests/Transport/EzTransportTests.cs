using System.Net;
using System.Text;
using EzMultiLib.Transport;
using EzMultiLib.Packets;

public class EzTransportTests
{
	[Fact]
	public async Task EzTransport_Receives_Sent_Data()
	{
		int port = 50000;
		var transport = new EzTransport(port);

		IPEndPoint sender = new IPEndPoint(IPAddress.Loopback, 50001);
		byte[] message = Encoding.UTF8.GetBytes("hello");

		TaskCompletionSource<(IPEndPoint ep, byte[] data)> tcs = new();

		transport.OnData += (ep, data) =>
		{
			tcs.TrySetResult((ep, data));
		};

		using (var senderUdp = new System.Net.Sockets.UdpClient())
		{
			await senderUdp.SendAsync(message, message.Length, new IPEndPoint(IPAddress.Loopback, port));
		}

		var resultTask = await Task.WhenAny(tcs.Task, Task.Delay(1000));

		Assert.True(resultTask == tcs.Task, "EzTransport did not receive packet within timeout.");

		var (receivedEP, receivedData) = tcs.Task.Result;

		Assert.Equal(sender.Address, receivedEP.Address);
		Assert.Equal("hello", Encoding.UTF8.GetString(receivedData));

		transport.Dispose();
	}
}

public class SimplePacket : IPacket 
{
	public string? text;
}
