using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

[Generator]
public sealed class PacketActionGenerator : ISourceGenerator
{
	private static readonly DiagnosticDescriptor UnsupportedFieldType = new DiagnosticDescriptor(
		"EZML001",
		"Unsupported packet field type",
		"Field '{0}' on packet '{1}' has type '{2}', which EzMultiLib cannot serialize",
		"EzMultiLib",
		DiagnosticSeverity.Error,
		true);

	private static readonly DiagnosticDescriptor MissingParameterlessConstructor = new DiagnosticDescriptor(
		"EZML002",
		"Packet requires a parameterless constructor",
		"Packet '{0}' must declare a public parameterless constructor",
		"EzMultiLib",
		DiagnosticSeverity.Error,
		true);

	private static readonly DiagnosticDescriptor ReadOnlyPacketField = new DiagnosticDescriptor(
		"EZML003",
		"Packet fields cannot be readonly",
		"Field '{0}' on packet '{1}' is readonly and cannot be assigned during deserialization",
		"EzMultiLib",
		DiagnosticSeverity.Error,
		true);

	private static readonly DiagnosticDescriptor DuplicatePacketName = new DiagnosticDescriptor(
		"EZML004",
		"Duplicate packet type name",
		"Packet '{0}' shares its type name with another packet; packet type names must be unique",
		"EzMultiLib",
		DiagnosticSeverity.Error,
		true);

	private static readonly DiagnosticDescriptor PacketIdCollision = new DiagnosticDescriptor(
		"EZML005",
		"Packet id collision",
		"Packets '{0}' and '{1}' both map to packet id {2}; pin one of them with [PacketId]",
		"EzMultiLib",
		DiagnosticSeverity.Error,
		true);

	private static readonly DiagnosticDescriptor ReservedPacketId = new DiagnosticDescriptor(
		"EZML006",
		"Reserved packet id",
		"Packet '{0}' declares packet id 0, which is reserved",
		"EzMultiLib",
		DiagnosticSeverity.Error,
		true);

	private static readonly DiagnosticDescriptor ProtocolAlreadyGenerated = new DiagnosticDescriptor(
		"EZML007",
		"Packets split across generated protocols",
		"Packet '{0}' is declared here, but referenced assembly '{1}' already generated the EzMultiLib protocol; move the packet into that project, or remove the EzMultiLib generator from it",
		"EzMultiLib",
		DiagnosticSeverity.Error,
		true);

	private static readonly DiagnosticDescriptor RecursivePacket = new DiagnosticDescriptor(
		"EZML008",
		"Recursive packet",
		"Packet '{0}' contains itself, directly or through another packet; a packet graph cannot be cyclic",
		"EzMultiLib",
		DiagnosticSeverity.Error,
		true);

	private static readonly DiagnosticDescriptor PartiallyNumberedPacket = new DiagnosticDescriptor(
		"EZML009",
		"Packet numbers only some of its fields",
		"Packet '{0}' gives a [Field] number to some of its fields but not all of them; number every field or none",
		"EzMultiLib",
		DiagnosticSeverity.Error,
		true);

	private static readonly DiagnosticDescriptor FieldNumbering = new DiagnosticDescriptor(
		"EZML010",
		"Field numbers must be contiguous",
		"Packet '{0}' must number its fields 1 to {1}, with no gaps and no duplicates",
		"EzMultiLib",
		DiagnosticSeverity.Error,
		true);

	private static readonly DiagnosticDescriptor OptionalFieldOrder = new DiagnosticDescriptor(
		"EZML011",
		"Optional fields must come last",
		"Packet '{0}' numbers a required field after an optional one; every optional field has to come after every required one",
		"EzMultiLib",
		DiagnosticSeverity.Error,
		true);

	// Info, not a warning: it is advice, and a project full of unnumbered packets is
	// not broken. Raise it with
	// dotnet_diagnostic.EZML012.severity = warning
	// in .editorconfig once the packets that matter are numbered.
	private static readonly DiagnosticDescriptor UnnumberedPacket = new DiagnosticDescriptor(
		"EZML012",
		"Packet does not number its fields",
		"Packet '{0}' does not number its fields, so their order on the wire comes from their names and renaming one silently changes that order; number them with [Field(n)] to pin it",
		"EzMultiLib",
		DiagnosticSeverity.Info,
		true);

	private sealed class PacketModel
	{
		public INamedTypeSymbol Symbol { get; }
		public List<IFieldSymbol> Fields { get; }
		public uint Id { get; }
		public bool Tagged { get; }
		public int OptionalCount { get; }
		public string Delivery { get; }
		public byte Channel { get; }

		public PacketModel(
			INamedTypeSymbol symbol,
			List<IFieldSymbol> fields,
			uint id,
			bool tagged,
			int optionalCount,
			string delivery,
			byte channel)
		{
			Symbol = symbol;
			Fields = fields;
			Id = id;
			Tagged = tagged;
			OptionalCount = optionalCount;
			Delivery = delivery;
			Channel = channel;
		}

		public string Name => Symbol.Name;
		public string TypeName => Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

		// Numbering puts every optional field after every required one, so they are
		// simply the tail of the list.
		public IEnumerable<IFieldSymbol> Required => Fields.Take(Fields.Count - OptionalCount);
		public IEnumerable<IFieldSymbol> Optional => Fields.Skip(Fields.Count - OptionalCount);
	}

	public void Initialize(GeneratorInitializationContext context)
	{
		context.RegisterForSyntaxNotifications(
			() => new PacketSyntaxReceiver());
	}

	public void Execute(GeneratorExecutionContext context)
	{
		if (!(context.SyntaxReceiver is PacketSyntaxReceiver receiver))
			return;

		var compilation = context.Compilation;

		var ipacketSymbol =
			compilation.GetTypeByMetadataName("EzMultiLib.Packets.IPacket");

		if (ipacketSymbol == null)
			return;

		var sourcePackets = new List<INamedTypeSymbol>();

		foreach (var candidate in receiver.Candidates)
		{
			var model = compilation.GetSemanticModel(candidate.SyntaxTree);

			if (!(model.GetDeclaredSymbol(candidate) is INamedTypeSymbol symbol))
				continue;

			if (!IsPacket(symbol, ipacketSymbol))
				continue;

			sourcePackets.Add(symbol);
		}

		sourcePackets = sourcePackets
			.Distinct(SymbolEqualityComparer.Default)
			.Cast<INamedTypeSymbol>()
			.ToList();

		if (AlreadyGeneratedElsewhere(compilation, out var owner))
		{
			foreach (var packet in sourcePackets)
			{
				context.ReportDiagnostic(Diagnostic.Create(
					ProtocolAlreadyGenerated,
					SourceLocation(packet),
					packet.ToDisplayString(),
					owner!.Name));
			}

			return;
		}

		var packets = sourcePackets
			.Concat(ReferencedPackets(compilation, ipacketSymbol))
			.Distinct(SymbolEqualityComparer.Default)
			.Cast<INamedTypeSymbol>()
			.OrderBy(p => p.ToDisplayString(), StringComparer.Ordinal)
			.ToList();

		var packetIdSymbol =
			compilation.GetTypeByMetadataName("EzMultiLib.Packets.PacketIdAttribute");

		var fieldSymbol =
			compilation.GetTypeByMetadataName("EzMultiLib.Packets.FieldAttribute");

		var deliverySymbol =
			compilation.GetTypeByMetadataName("EzMultiLib.Packets.DeliveryAttribute");

		var models = new List<PacketModel>();

		foreach (var packet in packets)
		{
			if (TryBuildModel(context, packet, packetIdSymbol, fieldSymbol, deliverySymbol, ipacketSymbol, out var model))
				models.Add(model);
		}

		models = RejectDuplicateNames(context, models);
		models = RejectCollidingIds(context, models);
		models = RejectRecursive(context, models, ipacketSymbol);

		context.AddSource(
			"PacketAction.g.cs",
			GenerateSource(models, ProtocolHash(models, compilation, ipacketSymbol), ipacketSymbol));
	}

	private static bool IsPacket(INamedTypeSymbol symbol, INamedTypeSymbol ipacketSymbol)
	{
		if (symbol.IsAbstract || symbol.IsGenericType)
			return false;

		if (symbol.TypeKind != TypeKind.Class && symbol.TypeKind != TypeKind.Struct)
			return false;

		return symbol.AllInterfaces.Any(i =>
			SymbolEqualityComparer.Default.Equals(i, ipacketSymbol));
	}

	private static Location? SourceLocation(ISymbol symbol)
	{
		return symbol.Locations.FirstOrDefault(l => l.IsInSource);
	}

	private static bool AlreadyGeneratedElsewhere(Compilation compilation, out IAssemblySymbol? owner)
	{
		owner = null;

		var existing = compilation.GetTypeByMetadataName("EzMultiLib.Serialization.EzSerializer");

		if (existing == null)
			return false;

		if (SymbolEqualityComparer.Default.Equals(existing.ContainingAssembly, compilation.Assembly))
			return false;

		owner = existing.ContainingAssembly;
		return true;
	}

	private static IEnumerable<INamedTypeSymbol> ReferencedPackets(
		Compilation compilation,
		INamedTypeSymbol ipacketSymbol)
	{
		var core = ipacketSymbol.ContainingAssembly;

		foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
		{
			if (!ReferencesAssembly(reference, core))
				continue;

			foreach (var type in EnumerateTypes(reference.GlobalNamespace))
			{
				if (type.DeclaredAccessibility != Accessibility.Public)
					continue;

				if (!IsPacket(type, ipacketSymbol))
					continue;

				yield return type;
			}
		}
	}

	private static bool ReferencesAssembly(IAssemblySymbol assembly, IAssemblySymbol target)
	{
		if (SymbolEqualityComparer.Default.Equals(assembly, target))
			return true;

		foreach (var module in assembly.Modules)
		{
			foreach (var referenced in module.ReferencedAssemblies)
			{
				if (referenced.Name == target.Name)
					return true;
			}
		}

		return false;
	}

	private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol ns)
	{
		foreach (var member in ns.GetMembers())
		{
			if (member is INamespaceSymbol child)
			{
				foreach (var type in EnumerateTypes(child))
					yield return type;
			}
			else if (member is INamedTypeSymbol type)
			{
				foreach (var nested in EnumerateNested(type))
					yield return nested;
			}
		}
	}

	private static IEnumerable<INamedTypeSymbol> EnumerateNested(INamedTypeSymbol type)
	{
		yield return type;

		foreach (var nested in type.GetTypeMembers())
		{
			foreach (var inner in EnumerateNested(nested))
				yield return inner;
		}
	}

	private static List<PacketModel> RejectDuplicateNames(
		GeneratorExecutionContext context,
		List<PacketModel> models)
	{
		var accepted = new List<PacketModel>();

		foreach (var group in models.GroupBy(m => m.Name, StringComparer.Ordinal))
		{
			if (group.Count() == 1)
			{
				accepted.Add(group.First());
				continue;
			}

			foreach (var model in group)
			{
				context.ReportDiagnostic(Diagnostic.Create(
					DuplicatePacketName,
					SourceLocation(model.Symbol),
					model.Symbol.ToDisplayString()));
			}
		}

		return accepted;
	}

	private static List<PacketModel> RejectCollidingIds(
		GeneratorExecutionContext context,
		List<PacketModel> models)
	{
		var accepted = new List<PacketModel>();

		foreach (var group in models.GroupBy(m => m.Id))
		{
			if (group.Count() == 1)
			{
				accepted.Add(group.First());
				continue;
			}

			var ordered = group.OrderBy(m => m.Name, StringComparer.Ordinal).ToList();

			for (var i = 1; i < ordered.Count; i++)
			{
				context.ReportDiagnostic(Diagnostic.Create(
					PacketIdCollision,
					SourceLocation(ordered[i].Symbol),
					ordered[0].Name,
					ordered[i].Name,
					group.Key));
			}
		}

		return accepted;
	}

	private static List<PacketModel> RejectRecursive(
		GeneratorExecutionContext context,
		List<PacketModel> models,
		INamedTypeSymbol ipacket)
	{
		var byName = new Dictionary<string, PacketModel>(StringComparer.Ordinal);

		foreach (var model in models)
			byName[model.Name] = model;

		var accepted = new List<PacketModel>();

		foreach (var model in models)
		{
			if (Reaches(model, model.Name, byName, ipacket, new HashSet<string>(StringComparer.Ordinal)))
			{
				context.ReportDiagnostic(Diagnostic.Create(
					RecursivePacket,
					SourceLocation(model.Symbol),
					model.Symbol.ToDisplayString()));

				continue;
			}

			accepted.Add(model);
		}

		return accepted;
	}

	private static bool Reaches(
		PacketModel from,
		string target,
		Dictionary<string, PacketModel> byName,
		INamedTypeSymbol ipacket,
		HashSet<string> seen)
	{
		foreach (var field in from.Fields)
		{
			var type = ElementType(field.Type, out _) ?? field.Type;

			if (!IsNested(type, ipacket))
				continue;

			if (type.Name == target)
				return true;

			if (!seen.Add(type.Name) || !byName.TryGetValue(type.Name, out var next))
				continue;

			if (Reaches(next, target, byName, ipacket, seen))
				return true;
		}

		return false;
	}

	private static uint StableId(string name)
	{
		unchecked
		{
			var hash = 2166136261u;

			foreach (var c in name)
			{
				hash ^= (byte)(c & 0xFF);
				hash *= 16777619u;
				hash ^= (byte)((c >> 8) & 0xFF);
				hash *= 16777619u;
			}

			hash ^= hash >> 16;
			hash *= 0x85EBCA6Bu;
			hash ^= hash >> 13;
			hash *= 0xC2B2AE35u;
			hash ^= hash >> 16;

			return hash == 0 ? 1u : hash;
		}
	}

	// Two processes only have to agree about the packets they share. In the shared
	// project layout that is everything coming from the referenced assembly, and each
	// side stays free to declare local packets of its own. With no shared assembly
	// both sides are built from the same source, so every packet counts.
	//
	// Field names are part of the hash because field order is derived from them, so a
	// rename reorders the wire without changing any packet id or length.
	private static uint ProtocolHash(
		List<PacketModel> models,
		Compilation compilation,
		INamedTypeSymbol ipacket)
	{
		var shared = models
			.Where(m => !SymbolEqualityComparer.Default.Equals(
				m.Symbol.ContainingAssembly, compilation.Assembly))
			.ToList();

		var sb = new StringBuilder();

		foreach (var model in shared.Count > 0 ? shared : models)
		{
			sb.Append(model.Name).Append(':').Append(model.Id).Append('(');

			if (model.Tagged)
			{
				// Numbers fix the order, so a rename is no longer a wire change and names
				// stay out. The optional tail stays out too: appending to it is exactly
				// the compatible change numbering exists to allow.
				var number = 0;

				foreach (var field in model.Required)
					sb.Append(++number).Append(':').Append(WireToken(field.Type, ipacket)).Append(',');

				sb.Append('+');
			}
			else
			{
				foreach (var field in model.Fields)
					sb.Append(field.Name).Append(':').Append(WireToken(field.Type, ipacket)).Append(',');
			}

			sb.Append(')');
		}

		return StableId(sb.ToString());
	}

	private static bool TryResolveId(
		GeneratorExecutionContext context,
		INamedTypeSymbol packet,
		INamedTypeSymbol? packetIdSymbol,
		out uint id)
	{
		id = StableId(packet.Name);

		if (packetIdSymbol == null)
			return true;

		var attribute = packet.GetAttributes().FirstOrDefault(a =>
			SymbolEqualityComparer.Default.Equals(a.AttributeClass, packetIdSymbol));

		if (attribute == null || attribute.ConstructorArguments.Length != 1)
			return true;

		var value = attribute.ConstructorArguments[0].Value;

		if (value == null)
			return true;

		var declared = Convert.ToUInt32(value);

		if (declared == 0)
		{
			context.ReportDiagnostic(Diagnostic.Create(
				ReservedPacketId,
				SourceLocation(packet),
				packet.ToDisplayString()));

			return false;
		}

		id = declared;
		return true;
	}

	private static bool TryBuildModel(
		GeneratorExecutionContext context,
		INamedTypeSymbol packet,
		INamedTypeSymbol? packetIdSymbol,
		INamedTypeSymbol? fieldSymbol,
		INamedTypeSymbol? deliverySymbol,
		INamedTypeSymbol ipacket,
		out PacketModel model)
	{
		model = null!;

		var valid = TryResolveId(context, packet, packetIdSymbol, out var id);

		var constructor = packet.InstanceConstructors
			.FirstOrDefault(c => c.Parameters.Length == 0);

		if (constructor == null || constructor.DeclaredAccessibility != Accessibility.Public)
		{
			context.ReportDiagnostic(Diagnostic.Create(
				MissingParameterlessConstructor,
				SourceLocation(packet),
				packet.ToDisplayString()));

			valid = false;
		}

		var fields = CollectFields(packet);

		foreach (var field in fields)
		{
			if (field.IsReadOnly)
			{
				context.ReportDiagnostic(Diagnostic.Create(
					ReadOnlyPacketField,
					SourceLocation(field),
					field.Name,
					packet.ToDisplayString()));

				valid = false;
				continue;
			}

			if (!IsSupportedField(field.Type, ipacket))
			{
				context.ReportDiagnostic(Diagnostic.Create(
					UnsupportedFieldType,
					SourceLocation(field),
					field.Name,
					packet.ToDisplayString(),
					field.Type.ToDisplayString()));

				valid = false;
			}
		}

		if (!TryOrderFields(context, packet, fields, fieldSymbol, out var ordered, out var tagged, out var optionalCount))
			valid = false;

		if (!valid)
			return false;

		if (!tagged && ordered.Count > 0)
		{
			context.ReportDiagnostic(Diagnostic.Create(
				UnnumberedPacket,
				SourceLocation(packet),
				packet.ToDisplayString()));
		}

		ResolveDelivery(packet, deliverySymbol, out var delivery, out var channel);

		model = new PacketModel(packet, ordered, id, tagged, optionalCount, delivery, channel);
		return true;
	}

	// An unnumbered packet keeps name ordering, which is what every packet written
	// before numbering existed uses. A numbered one is ordered by its numbers, so
	// renaming a field no longer moves it on the wire.
	// Delivery is a send side choice and does not change how any byte is parsed, so it
	// stays out of the protocol hash: tuning it on one side should not turn the other
	// side away. Reliable and ordered unless the packet says otherwise.
	private static void ResolveDelivery(
		INamedTypeSymbol packet,
		INamedTypeSymbol? deliverySymbol,
		out string method,
		out byte channel)
	{
		method = "ReliableOrdered";
		channel = 0;

		if (deliverySymbol == null)
			return;

		var attribute = packet.GetAttributes().FirstOrDefault(a =>
			SymbolEqualityComparer.Default.Equals(a.AttributeClass, deliverySymbol));

		if (attribute == null)
			return;

		if (attribute.ConstructorArguments.Length == 1
			&& attribute.ConstructorArguments[0].Type is INamedTypeSymbol enumType)
		{
			var value = attribute.ConstructorArguments[0].Value;

			// Read the member's name off the symbol rather than mapping the underlying
			// number, so reordering the enum cannot quietly change what is emitted.
			var member = enumType.GetMembers()
				.OfType<IFieldSymbol>()
				.FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, value));

			if (member != null)
				method = member.Name;
		}

		foreach (var named in attribute.NamedArguments)
		{
			if (named.Key == "Channel" && named.Value.Value is byte declared)
				channel = declared;
		}
	}

	private static bool TryOrderFields(
		GeneratorExecutionContext context,
		INamedTypeSymbol packet,
		List<IFieldSymbol> fields,
		INamedTypeSymbol? fieldSymbol,
		out List<IFieldSymbol> ordered,
		out bool tagged,
		out int optionalCount)
	{
		ordered = fields;
		tagged = false;
		optionalCount = 0;

		if (fieldSymbol == null || fields.Count == 0)
			return true;

		var tags = new List<(IFieldSymbol Field, int Number, bool Optional)>();

		foreach (var field in fields)
		{
			var attribute = field.GetAttributes().FirstOrDefault(a =>
				SymbolEqualityComparer.Default.Equals(a.AttributeClass, fieldSymbol));

			if (attribute == null || attribute.ConstructorArguments.Length != 1)
				continue;

			var number = Convert.ToInt32(attribute.ConstructorArguments[0].Value);

			var optional = attribute.NamedArguments.Any(a =>
				a.Key == "Optional" && a.Value.Value is bool flag && flag);

			tags.Add((field, number, optional));
		}

		if (tags.Count == 0)
			return true;

		if (tags.Count != fields.Count)
		{
			context.ReportDiagnostic(Diagnostic.Create(
				PartiallyNumberedPacket,
				SourceLocation(packet),
				packet.ToDisplayString()));

			return false;
		}

		var sorted = tags.OrderBy(t => t.Number).ToList();

		// Contiguous from 1 is what makes removing a field a build error rather than a
		// silent shift of everything behind it.
		for (var i = 0; i < sorted.Count; i++)
		{
			if (sorted[i].Number == i + 1)
				continue;

			context.ReportDiagnostic(Diagnostic.Create(
				FieldNumbering,
				SourceLocation(packet),
				packet.ToDisplayString(),
				sorted.Count));

			return false;
		}

		var first = sorted.FindIndex(t => t.Optional);

		if (first >= 0 && sorted.Skip(first).Any(t => !t.Optional))
		{
			context.ReportDiagnostic(Diagnostic.Create(
				OptionalFieldOrder,
				SourceLocation(packet),
				packet.ToDisplayString()));

			return false;
		}

		ordered = sorted.Select(t => t.Field).ToList();
		tagged = true;
		optionalCount = sorted.Count(t => t.Optional);

		return true;
	}

	private static List<IFieldSymbol> CollectFields(INamedTypeSymbol packet)
	{
		var levels = new List<List<IFieldSymbol>>();

		for (var type = packet; type != null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
		{
			var level = type.GetMembers()
				.OfType<IFieldSymbol>()
				.Where(f => !f.IsStatic && !f.IsConst && !f.IsImplicitlyDeclared)
				.Where(f => f.DeclaredAccessibility == Accessibility.Public)
				.OrderBy(f => f.Name, StringComparer.Ordinal)
				.ToList();

			levels.Add(level);
		}

		levels.Reverse();

		return levels.SelectMany(l => l).ToList();
	}

	private static string? PrimitiveName(ITypeSymbol type)
	{
		switch (UnderlyingSpecialType(type))
		{
			case SpecialType.System_Boolean: return "Bool";
			case SpecialType.System_SByte: return "SByte";
			case SpecialType.System_Byte: return "Byte";
			case SpecialType.System_Int16: return "Short";
			case SpecialType.System_UInt16: return "UShort";
			case SpecialType.System_Int32: return "Int";
			case SpecialType.System_UInt32: return "UInt";
			case SpecialType.System_Int64: return "Long";
			case SpecialType.System_UInt64: return "ULong";
			case SpecialType.System_Single: return "Float";
			case SpecialType.System_Double: return "Double";
			case SpecialType.System_String: return "String";
			default: return null;
		}
	}

	private static SpecialType UnderlyingSpecialType(ITypeSymbol type)
	{
		if (type.TypeKind == TypeKind.Enum
			&& type is INamedTypeSymbol named
			&& named.EnumUnderlyingType != null)
		{
			return named.EnumUnderlyingType.SpecialType;
		}

		return type.SpecialType;
	}

	private static string Keyword(SpecialType special)
	{
		switch (special)
		{
			case SpecialType.System_SByte: return "sbyte";
			case SpecialType.System_Byte: return "byte";
			case SpecialType.System_Int16: return "short";
			case SpecialType.System_UInt16: return "ushort";
			case SpecialType.System_Int32: return "int";
			case SpecialType.System_UInt32: return "uint";
			case SpecialType.System_Int64: return "long";
			case SpecialType.System_UInt64: return "ulong";
			default: return "int";
		}
	}

	// T[] and List<T>, one rank and one level deep. Anything else, a jagged array or
	// a list of lists, falls through as an unsupported field type.
	private static ITypeSymbol? ElementType(ITypeSymbol type, out bool isArray)
	{
		isArray = false;

		if (type is IArrayTypeSymbol array)
		{
			isArray = true;
			return array.Rank == 1 ? array.ElementType : null;
		}

		if (type is INamedTypeSymbol named
			&& named.IsGenericType
			&& named.TypeArguments.Length == 1
			&& named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>")
		{
			return named.TypeArguments[0];
		}

		return null;
	}

	private static bool IsNested(ITypeSymbol type, INamedTypeSymbol ipacket) =>
		type is INamedTypeSymbol named && IsPacket(named, ipacket);

	private static bool IsSupportedValue(ITypeSymbol type, INamedTypeSymbol ipacket) =>
		PrimitiveName(type) != null || IsNested(type, ipacket);

	private static bool IsSupportedField(ITypeSymbol type, INamedTypeSymbol ipacket)
	{
		if (IsSupportedValue(type, ipacket))
			return true;

		var element = ElementType(type, out _);

		return element != null && IsSupportedValue(element, ipacket);
	}

	// What a field contributes to the protocol hash. Shape belongs in here, not just
	// the primitive, or swapping int for int[] would go unnoticed. An array and a
	// List of the same element share a token, because they share a wire format and
	// trading one for the other is not a protocol change.
	private static string WireToken(ITypeSymbol type, INamedTypeSymbol ipacket)
	{
		var element = ElementType(type, out _);

		if (element != null)
			return "[]" + WireToken(element, ipacket);

		return PrimitiveName(type) ?? ("@" + type.Name);
	}

	private const string Pad = "            ";

	// One value. A nested reference type carries a presence flag so null survives the
	// round trip; a nested struct cannot be null, so it does not need one.
	private static void AppendWriteValue(StringBuilder sb, string pad, string access, ITypeSymbol type)
	{
		var primitive = PrimitiveName(type);

		if (primitive != null)
		{
			if (type.TypeKind == TypeKind.Enum)
				access = "(" + Keyword(UnderlyingSpecialType(type)) + ")" + access;

			sb.AppendLine($"{pad}writer.Write{primitive}({access});");
			return;
		}

		if (type.IsReferenceType)
		{
			sb.AppendLine($"{pad}writer.WriteBool({access} != null);");
			sb.AppendLine($"{pad}if ({access} != null)");
			sb.AppendLine($"{pad}    Write{type.Name}(writer, {access}!);");
			return;
		}

		sb.AppendLine($"{pad}Write{type.Name}(writer, {access});");
	}

	private static string ReadValueExpression(ITypeSymbol type)
	{
		var primitive = PrimitiveName(type);

		if (primitive == null)
		{
			return type.IsReferenceType
				? $"(reader.ReadBool() ? Read{type.Name}(reader) : null!)"
				: $"Read{type.Name}(reader)";
		}

		var value = $"reader.Read{primitive}()";

		if (type.TypeKind == TypeKind.Enum)
			return $"({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){value}";

		return UnderlyingSpecialType(type) == SpecialType.System_String ? value + "!" : value;
	}

	private static void AppendWriteField(StringBuilder sb, IFieldSymbol field)
	{
		var access = "packet." + field.Name;
		var element = ElementType(field.Type, out var isArray);

		if (element == null)
		{
			AppendWriteValue(sb, Pad, access, field.Type);
			return;
		}

		var count = access + (isArray ? ".Length" : ".Count");

		sb.AppendLine($"{Pad}writer.WriteBool({access} != null);");
		sb.AppendLine($"{Pad}if ({access} != null)");
		sb.AppendLine($"{Pad}{{");
		sb.AppendLine($"{Pad}    PacketFramer.WriteCount(writer, {count});");
		sb.AppendLine();
		sb.AppendLine($"{Pad}    for (var i = 0; i < {count}; i++)");
		sb.AppendLine($"{Pad}    {{");
		AppendWriteValue(sb, Pad + "        ", $"{access}[i]", element);
		sb.AppendLine($"{Pad}    }}");
		sb.AppendLine($"{Pad}}}");
	}

	private static void AppendReadField(StringBuilder sb, IFieldSymbol field, string pad)
	{
		var access = "packet." + field.Name;
		var element = ElementType(field.Type, out var isArray);

		if (element == null)
		{
			sb.AppendLine($"{pad}{access} = {ReadValueExpression(field.Type)};");
			return;
		}

		var elementName = element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

		sb.AppendLine($"{pad}if (!reader.ReadBool())");
		sb.AppendLine($"{pad}{{");
		sb.AppendLine($"{pad}    {access} = null!;");
		sb.AppendLine($"{pad}}}");
		sb.AppendLine($"{pad}else");
		sb.AppendLine($"{pad}{{");
		sb.AppendLine($"{pad}    var count = reader.ReadUShort();");
		sb.AppendLine();

		// Capacity is clamped rather than taken from the count, so a datagram that
		// claims 65535 elements and then stops cannot make us allocate for the ones
		// it does not carry. The loop stops the moment the reader runs out.
		sb.AppendLine($"{pad}    var items = new List<{elementName}>(count < 32 ? count : 32);");
		sb.AppendLine();
		sb.AppendLine($"{pad}    for (var i = 0; i < count && !reader.Failed; i++)");
		sb.AppendLine($"{pad}        items.Add({ReadValueExpression(element)});");
		sb.AppendLine();
		var suffix = isArray ? ".ToArray()" : string.Empty;

		sb.AppendLine($"{pad}    {access} = items{suffix};");
		sb.AppendLine($"{pad}}}");
	}

	private static string GenerateSource(
		List<PacketModel> packets,
		uint protocolHash,
		INamedTypeSymbol ipacket)
	{
		var sb = new StringBuilder();

		sb.AppendLine("// <auto-generated/>");
		sb.AppendLine("#nullable enable");
		sb.AppendLine();
		sb.AppendLine("using System;");
		sb.AppendLine("using System.Collections.Generic;");
		sb.AppendLine("using EzMultiLib.IO;");
		sb.AppendLine("using EzMultiLib.Packets;");
		sb.AppendLine("using EzMultiLib.Peers;");
		sb.AppendLine("using EzMultiLib.Serialization.IO;");
		sb.AppendLine("using EzMultiLib.Serialization.Packets;");
		sb.AppendLine();

		AppendPacketAction(sb, packets, protocolHash);
		sb.AppendLine();
		AppendDispatcher(sb, packets);
		sb.AppendLine();
		AppendSerializer(sb, packets, ipacket);

		return sb.ToString();
	}

	private static void AppendPacketAction(StringBuilder sb, List<PacketModel> packets, uint protocolHash)
	{
		sb.AppendLine("namespace EzMultiLib.Packets");
		sb.AppendLine("{");
		sb.AppendLine("    public static partial class PacketAction");
		sb.AppendLine("    {");

		foreach (var packet in packets)
		{
			sb.AppendLine($"        public const uint {packet.Name}Id = {packet.Id}u;");
		}

		sb.AppendLine();
		sb.AppendLine($"        public const uint ProtocolHash = {protocolHash}u;");
		sb.AppendLine();
		sb.AppendLine("        public static PacketDispatcher Default { get; } = new PacketDispatcher();");
		sb.AppendLine();
		sb.AppendLine($"        private static readonly Dictionary<Type, uint> Ids = new Dictionary<Type, uint>({packets.Count})");
		sb.AppendLine("        {");

		foreach (var packet in packets)
		{
			sb.AppendLine($"            {{ typeof({packet.TypeName}), {packet.Name}Id }},");
		}

		sb.AppendLine("        };");
		sb.AppendLine();
		sb.AppendLine("        public static uint GetPacketId(IPacket packet)");
		sb.AppendLine("        {");
		sb.AppendLine("            if (packet != null && Ids.TryGetValue(packet.GetType(), out var id))");
		sb.AppendLine("                return id;");
		sb.AppendLine();
		sb.AppendLine("            throw new ArgumentException($\"Unknown packet type '{packet?.GetType()}'\", nameof(packet));");
		sb.AppendLine("        }");
		sb.AppendLine();
		sb.AppendLine();
		sb.AppendLine("        public static PacketDelivery GetDelivery(IPacket packet) =>");
		sb.AppendLine("            GetDelivery(GetPacketId(packet));");
		sb.AppendLine();
		sb.AppendLine("        public static PacketDelivery GetDelivery(uint id)");
		sb.AppendLine("        {");
		sb.AppendLine("            switch (id)");
		sb.AppendLine("            {");

		// Only packets that asked for something get a case. Everything else, and an id
		// this build does not know, lands on the safe default below.
		foreach (var packet in packets)
		{
			if (packet.Delivery == "ReliableOrdered" && packet.Channel == 0)
				continue;

			sb.AppendLine($"                case {packet.Name}Id:");
			sb.AppendLine($"                    return new PacketDelivery(Delivery.{packet.Delivery}, {packet.Channel});");
		}

		sb.AppendLine("                default:");
		sb.AppendLine("                    return new PacketDelivery(Delivery.ReliableOrdered, 0);");
		sb.AppendLine("            }");
		sb.AppendLine("        }");
		sb.AppendLine();
		sb.AppendLine("        public static void AcceptPacket(Peer? peer, IPacket packet) => Default.Accept(peer, packet);");
		sb.AppendLine("    }");
		sb.AppendLine("}");
	}

	private static void AppendDispatcher(StringBuilder sb, List<PacketModel> packets)
	{
		sb.AppendLine("namespace EzMultiLib.Packets");
		sb.AppendLine("{");
		sb.AppendLine("    public sealed class PacketDispatcher");
		sb.AppendLine("    {");

		foreach (var packet in packets)
		{
			sb.AppendLine($"        public event Action<Peer?, {packet.TypeName}>? On{packet.Name};");
		}

		sb.AppendLine();
		sb.AppendLine("        // Subscribe this and a handler that throws is reported here instead of");
		sb.AppendLine("        // unwinding into the receive loop, and the next packet still gets");
		sb.AppendLine("        // delivered. Leave it unsubscribed and the exception propagates the way");
		sb.AppendLine("        // it always has, rather than being swallowed where nobody can see it.");
		sb.AppendLine("        public event Action<Peer?, IPacket, Exception>? HandlerFailed;");

		sb.AppendLine();
		sb.AppendLine("        public void Accept(Peer? peer, IPacket packet) =>");
		sb.AppendLine("            Accept(peer, PacketAction.GetPacketId(packet), packet);");
		sb.AppendLine();
		sb.AppendLine("        public void Accept(Peer? peer, uint id, IPacket packet)");
		sb.AppendLine("        {");
		sb.AppendLine("            switch (id)");
		sb.AppendLine("            {");

		foreach (var packet in packets)
		{
			sb.AppendLine($"                case PacketAction.{packet.Name}Id:");
			sb.AppendLine($"                    Deliver(On{packet.Name}, peer, ({packet.TypeName})packet);");
			sb.AppendLine("                    break;");
		}

		sb.AppendLine("                default:");
		sb.AppendLine("                    throw new ArgumentException($\"Unknown packet type '{packet?.GetType()}'\", nameof(packet));");
		sb.AppendLine("            }");
		sb.AppendLine("        }");
		sb.AppendLine();
		sb.AppendLine("        public void ClearHandlers()");
		sb.AppendLine("        {");

		foreach (var packet in packets)
		{
			sb.AppendLine($"            On{packet.Name} = null;");
		}

		sb.AppendLine("            HandlerFailed = null;");
		sb.AppendLine("        }");
		sb.AppendLine();
		sb.AppendLine("        // No allocation on the way through: the invocation list is never copied,");
		sb.AppendLine("        // and with no error hook attached there is not even a try block.");
		sb.AppendLine("        private void Deliver<T>(Action<Peer?, T>? handler, Peer? peer, T packet)");
		sb.AppendLine("            where T : IPacket");
		sb.AppendLine("        {");
		sb.AppendLine("            if (handler == null)");
		sb.AppendLine("                return;");
		sb.AppendLine();
		sb.AppendLine("            var failed = HandlerFailed;");
		sb.AppendLine();
		sb.AppendLine("            if (failed == null)");
		sb.AppendLine("            {");
		sb.AppendLine("                handler(peer, packet);");
		sb.AppendLine("                return;");
		sb.AppendLine("            }");
		sb.AppendLine();
		sb.AppendLine("            try");
		sb.AppendLine("            {");
		sb.AppendLine("                handler(peer, packet);");
		sb.AppendLine("            }");
		sb.AppendLine("            catch (Exception error)");
		sb.AppendLine("            {");
		sb.AppendLine("                failed(peer, packet, error);");
		sb.AppendLine("            }");
		sb.AppendLine("        }");

		sb.AppendLine("    }");
		sb.AppendLine("}");
	}

	private static void AppendSerializer(StringBuilder sb, List<PacketModel> packets, INamedTypeSymbol ipacket)
	{
		sb.AppendLine("namespace EzMultiLib.Serialization");
		sb.AppendLine("{");
		sb.AppendLine("    public static class EzSerializer");
		sb.AppendLine("    {");
		sb.AppendLine("        public static byte[] Serialize(IPacket packet)");
		sb.AppendLine("        {");
		sb.AppendLine("            var writer = new EzWriter();");
		sb.AppendLine("            Write(writer, packet);");
		sb.AppendLine("            return writer.ToArray();");
		sb.AppendLine("        }");
		sb.AppendLine();
		sb.AppendLine("        public static IPacket Deserialize(byte[] data)");
		sb.AppendLine("        {");
		sb.AppendLine("            if (!TryDeserialize(data, out var packet))");
		sb.AppendLine("                throw new MalformedPacketException(\"The buffer does not contain exactly one valid packet.\");");
		sb.AppendLine();
		sb.AppendLine("            return packet!;");
		sb.AppendLine("        }");
		sb.AppendLine();
		sb.AppendLine("        public static bool TryDeserialize(byte[] data, out IPacket? packet)");
		sb.AppendLine("        {");
		sb.AppendLine("            packet = null;");
		sb.AppendLine();
		sb.AppendLine("            if (data == null)");
		sb.AppendLine("                return false;");
		sb.AppendLine();
		sb.AppendLine("            var reader = new EzReader(data);");
		sb.AppendLine();
		sb.AppendLine("            if (!TryRead(reader, out packet))");
		sb.AppendLine("                return false;");
		sb.AppendLine();
		sb.AppendLine("            if (reader.Remaining != 0)");
		sb.AppendLine("            {");
		sb.AppendLine("                packet = null;");
		sb.AppendLine("                return false;");
		sb.AppendLine("            }");
		sb.AppendLine();
		sb.AppendLine("            return true;");
		sb.AppendLine("        }");
		sb.AppendLine();
		sb.AppendLine("        public static bool TryRead(IPacketReader reader, out IPacket? packet) =>");
		sb.AppendLine("            TryRead(reader, out packet, out _);");
		sb.AppendLine();
		sb.AppendLine("        public static bool TryRead(IPacketReader reader, out IPacket? packet, out uint id)");
		sb.AppendLine("        {");
		sb.AppendLine("            packet = null;");
		sb.AppendLine("            id = 0;");
		sb.AppendLine();
		sb.AppendLine("            if (reader == null)");
		sb.AppendLine("                return false;");
		sb.AppendLine();
		sb.AppendLine("            id = PacketFramer.ReadHeader(reader);");
		sb.AppendLine();
		sb.AppendLine("            if (reader.Failed || !TryReadBody(id, reader, out packet) || reader.Failed)");
		sb.AppendLine("            {");
		sb.AppendLine("                packet = null;");
		sb.AppendLine("                return false;");
		sb.AppendLine("            }");
		sb.AppendLine();
		sb.AppendLine("            return true;");
		sb.AppendLine("        }");
		sb.AppendLine();
		sb.AppendLine("        public static void Write(IPacketWriter writer, IPacket packet) =>");
		sb.AppendLine("            Write(writer, packet, out _);");
		sb.AppendLine();
		sb.AppendLine("        public static void Write(IPacketWriter writer, IPacket packet, out uint id)");
		sb.AppendLine("        {");
		sb.AppendLine("            id = PacketAction.GetPacketId(packet);");
		sb.AppendLine("            PacketFramer.WriteHeader(writer, id);");
		sb.AppendLine("            WriteBody(writer, id, packet);");
		sb.AppendLine("        }");
		sb.AppendLine();
		sb.AppendLine("        public static IPacket Read(IPacketReader reader)");
		sb.AppendLine("        {");
		sb.AppendLine("            if (!TryRead(reader, out var packet))");
		sb.AppendLine("                throw new MalformedPacketException(\"The reader does not contain a valid packet.\");");
		sb.AppendLine();
		sb.AppendLine("            return packet!;");
		sb.AppendLine("        }");
		sb.AppendLine();
		sb.AppendLine("        public static void WriteBody(IPacketWriter writer, IPacket packet) =>");
		sb.AppendLine("            WriteBody(writer, PacketAction.GetPacketId(packet), packet);");
		sb.AppendLine();
		sb.AppendLine("        public static void WriteBody(IPacketWriter writer, uint id, IPacket packet)");
		sb.AppendLine("        {");
		sb.AppendLine("            switch (id)");
		sb.AppendLine("            {");

		foreach (var packet in packets)
		{
			sb.AppendLine($"                case PacketAction.{packet.Name}Id:");
			sb.AppendLine($"                    Write{packet.Name}(writer, ({packet.TypeName})packet);");
			sb.AppendLine("                    break;");
		}

		sb.AppendLine("                default:");
		sb.AppendLine("                    throw new ArgumentException($\"Unknown packet type '{packet?.GetType()}'\", nameof(packet));");
		sb.AppendLine("            }");
		sb.AppendLine("        }");
		sb.AppendLine();
		sb.AppendLine("        public static IPacket ReadBody(uint id, IPacketReader reader)");
		sb.AppendLine("        {");
		sb.AppendLine("            if (!TryReadBody(id, reader, out var packet))");
		sb.AppendLine("                throw new MalformedPacketException($\"Unknown packet id '{id}'\");");
		sb.AppendLine();
		sb.AppendLine("            return packet!;");
		sb.AppendLine("        }");
		sb.AppendLine();
		sb.AppendLine("        public static bool TryReadBody(uint id, IPacketReader reader, out IPacket? packet)");
		sb.AppendLine("        {");
		sb.AppendLine("            switch (id)");
		sb.AppendLine("            {");

		foreach (var packet in packets)
		{
			sb.AppendLine($"                case PacketAction.{packet.Name}Id:");
			sb.AppendLine($"                    packet = Read{packet.Name}(reader);");
			sb.AppendLine("                    return true;");
		}

		sb.AppendLine("                default:");
		sb.AppendLine("                    packet = null;");
		sb.AppendLine("                    return false;");
		sb.AppendLine("            }");
		sb.AppendLine("        }");

		foreach (var packet in packets)
		{
			sb.AppendLine();
			sb.AppendLine($"        public static void Write{packet.Name}(IPacketWriter writer, {packet.TypeName} packet)");
			sb.AppendLine("        {");

			// How many optional fields follow. A constant for this build, and what lets
			// the other side tell an omitted field from a truncated one.
			if (packet.Tagged)
				sb.AppendLine($"            writer.WriteByte({packet.OptionalCount});");

			foreach (var field in packet.Fields)
				AppendWriteField(sb, field);

			sb.AppendLine("        }");
			sb.AppendLine();
			sb.AppendLine($"        public static {packet.TypeName} Read{packet.Name}(IPacketReader reader)");
			sb.AppendLine("        {");
			sb.AppendLine($"            var packet = new {packet.TypeName}();");

			if (packet.Tagged)
			{
				sb.AppendLine("            var optional = reader.ReadByte();");
				sb.AppendLine();
				sb.AppendLine("            // The sender has optional fields this build does not know, and their");
				sb.AppendLine("            // sizes are unknown too, so the tail cannot be stepped over and the");
				sb.AppendLine("            // framing is gone with it. Fail rather than guess.");
				sb.AppendLine($"            if (optional > {packet.OptionalCount})");
				sb.AppendLine("            {");
				sb.AppendLine("                PacketFramer.Fail(reader);");
				sb.AppendLine("                return packet;");
				sb.AppendLine("            }");
				sb.AppendLine();
			}

			foreach (var field in packet.Required)
				AppendReadField(sb, field, Pad);

			var present = 0;

			foreach (var field in packet.Optional)
			{
				sb.AppendLine($"{Pad}if (optional > {present++})");
				sb.AppendLine($"{Pad}{{");
				AppendReadField(sb, field, Pad + "    ");
				sb.AppendLine($"{Pad}}}");
			}

			sb.AppendLine("            return packet;");
			sb.AppendLine("        }");
		}

		sb.AppendLine("    }");
		sb.AppendLine("}");
	}
}
