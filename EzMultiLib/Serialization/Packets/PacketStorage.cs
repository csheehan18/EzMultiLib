using System;
using System.Collections.Generic;

namespace EzMultiLib.Serialization.Packets
{
	internal static class PacketStorage
	{
		private static readonly Dictionary<Type, PacketType> pkts = new();

		public static PacketType GetOrCreate(Type type)
		{
			if (pkts.TryGetValue(type, out var pkt))
				return pkt;

			// Have to implement a way to cleanly store public fields from class
			pkt = PacketTypeModelBuilder.Build(type);
			pkts[type] = pkt;

			return pkt;
		}
	}
}
