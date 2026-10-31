namespace EzMultiLib.Packets
{
	// How a packet wants to be sent. These are the usual five guarantees a UDP
	// transport offers, named the way transports tend to name them, so an adapter is a
	// single switch. Nothing here talks to a transport: it is a declaration the send
	// side reads, and it never changes how bytes are parsed.
	public enum Delivery
	{
		// Arrives, in the order it was sent. The default, because a packet should only
		// become droppable when someone says so.
		ReliableOrdered,

		// Arrives, in any order.
		ReliableUnordered,

		// Arrives, and anything it overtook is dropped.
		ReliableSequenced,

		// May be dropped, but never arrives stale. What position updates want.
		Sequenced,

		// May be dropped, in any order.
		Unreliable
	}

	public readonly struct PacketDelivery
	{
		public Delivery Method { get; }

		public byte Channel { get; }

		public PacketDelivery(Delivery method, byte channel)
		{
			Method = method;
			Channel = channel;
		}
	}
}
