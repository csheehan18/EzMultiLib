using EzMultiLib.Serialization.IO;
using System;

namespace EzMultiLib.Serialization
{
	internal static class PacketBuilder
	{
		// Will basically choose the type in a big switch statment or if statment havent decided yet
		internal static void Write(EzWriter writer, Type fieldType, object? value)
		{
            if (fieldType.IsEnum)
            {
                var underlying = Enum.GetUnderlyingType(fieldType);
                Write(writer, underlying, Convert.ChangeType(value, underlying));
                return;
            }

            switch (Type.GetTypeCode(fieldType))
            {
                case TypeCode.Int32: writer.WriteInt((int)value!); break;
                case TypeCode.UInt16: writer.WriteUShort((ushort)value!); break;
                case TypeCode.Byte: writer.WriteByte((byte)value!); break;
                case TypeCode.Boolean: writer.WriteBool((bool)value!); break;
                case TypeCode.String: writer.WriteString((string)value!); break;
                default:
                    throw new NotSupportedException($"Type '{fieldType.Name}' is not supported.");
            }
        }

		// Same as top but send back the value of the field
		internal static object Read(EzReader reader, Type fieldType)
		{
            if (fieldType.IsEnum)
            {
                var underlying = Enum.GetUnderlyingType(fieldType);
                var raw = Read(reader, underlying);
                return Enum.ToObject(fieldType, raw);
            }

            return Type.GetTypeCode(fieldType) switch
            {
                TypeCode.Int32 => reader.ReadInt(),
                TypeCode.UInt16 => reader.ReadUShort(),
                TypeCode.Byte => reader.ReadByte(),
                TypeCode.Boolean => reader.ReadBool(),
                TypeCode.String => reader.ReadString(),
                _ => throw new NotSupportedException($"Type '{fieldType.Name}' is not supported.")
            };
        }
	}
}
