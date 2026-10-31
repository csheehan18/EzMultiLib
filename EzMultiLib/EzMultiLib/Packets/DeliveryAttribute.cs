using System;

namespace EzMultiLib.Packets
{
	// Declares how a packet should be sent. Without one a packet is ReliableOrdered on
	// channel 0. Separate channels keep unrelated reliable traffic from blocking each
	// other, so a bulk inventory sync does not stall chat behind it.
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
	public sealed class DeliveryAttribute : Attribute
	{
		public Delivery Method { get; }

		public byte Channel { get; set; }

		public DeliveryAttribute(Delivery method)
		{
			Method = method;
		}
	}
}
