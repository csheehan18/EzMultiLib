using EzMultiLib.Serialization.IO;
using System;

namespace EzMultiLib.Serialization
{
	public static class PacketBuilder
	{
		// Will basically choose the type in a big switch statment or if statment havent decided yet
		internal static void Write(EzWriter writer, Type fieldType, object? value)
		{

		}

		// Same as top but send back the value of the field
		internal static object Read(EzReader reader, Type fieldType)
		{
			return null;
		}
	}
}
