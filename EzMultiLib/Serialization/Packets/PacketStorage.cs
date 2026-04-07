using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EzMultiLib.Serialization.Packets
{
	internal static class PacketStorage
	{
		private static readonly Dictionary<Type, PacketType> pkts = new();

        public static PacketType GetOrCreate(Type type)
        {
            if (pkts.TryGetValue(type, out var pkt))
                return pkt;

            if (type.GetConstructor(Type.EmptyTypes) == null)
                throw new InvalidOperationException($"{type.Name} must have a parameterless constructor.");

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name).ToArray();

            pkt = new PacketType(type, fields);
            pkts[type] = pkt;

            return pkt;
        }
    }
}
