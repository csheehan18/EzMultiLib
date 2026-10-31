using System;

namespace EzMultiLib.Packets
{
	// Numbering a packet's fields fixes their order on the wire, instead of deriving it
	// from their names. Number every field of a packet or none of them, from 1 upwards
	// with no gaps.
	[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
	public sealed class FieldAttribute : Attribute
	{
		public int Number { get; }

		// An optional field may be missing because the sender was built before it
		// existed, in which case it keeps its default. Optional fields come last and
		// are append only: once one has shipped, never remove, reorder or retype it.
		public bool Optional { get; set; }

		public FieldAttribute(int number)
		{
			Number = number;
		}
	}
}
