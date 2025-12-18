using EzMultiLib.Packets;
using EzMultiLib.Serialization.IO;
using System;
using System.Reflection;

namespace EzMultiLib.Serialization.Packets
{
	internal sealed class PacketType
	{
		public Type PacketClass { get; }
		private readonly FieldInfo[] _fields;

		public PacketType(Type packetClass, FieldInfo[] fields)
		{
			PacketClass = packetClass;
			_fields = fields;
		}

		public void Write(EzWriter writer, IPacket packet)
		{
			foreach (var field in _fields)
			{
				var value = field.GetValue(packet);
				PacketBuilder.Write(writer, field.FieldType, value);
			}
		}

		public IPacket Read(EzReader reader)
		{
			var packet = (IPacket)Activator.CreateInstance(PacketClass)!;

			foreach (var field in _fields)
			{
				var value = PacketBuilder.Read(reader, field.FieldType);
				field.SetValue(packet, value);
			}

			return packet;
		}
	}
}
