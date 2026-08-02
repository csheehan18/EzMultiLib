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

	private sealed class PacketModel
	{
		public INamedTypeSymbol Symbol { get; }
		public List<IFieldSymbol> Fields { get; }
		public ushort Id { get; }

		public PacketModel(INamedTypeSymbol symbol, List<IFieldSymbol> fields, ushort id)
		{
			Symbol = symbol;
			Fields = fields;
			Id = id;
		}

		public string Name => Symbol.Name;
		public string TypeName => Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
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

		var models = new List<PacketModel>();

		foreach (var packet in packets)
		{
			if (TryBuildModel(context, packet, packetIdSymbol, out var model))
				models.Add(model);
		}

		models = RejectDuplicateNames(context, models);
		models = RejectCollidingIds(context, models);

		context.AddSource("PacketAction.g.cs", GenerateSource(models));
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

	private static ushort StableId(string name)
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

			var folded = (ushort)((hash ^ (hash >> 16)) & 0xFFFF);
			return folded == 0 ? (ushort)1 : folded;
		}
	}

	private static bool TryResolveId(
		GeneratorExecutionContext context,
		INamedTypeSymbol packet,
		INamedTypeSymbol? packetIdSymbol,
		out ushort id)
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

		var declared = Convert.ToUInt16(value);

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

			if (PrimitiveName(field.Type) == null)
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

		if (!valid)
			return false;

		model = new PacketModel(packet, fields, id);
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

	private static string WriteFieldStatement(IFieldSymbol field)
	{
		var access = "packet." + field.Name;

		if (field.Type.TypeKind == TypeKind.Enum)
			access = "(" + Keyword(UnderlyingSpecialType(field.Type)) + ")" + access;

		return $"writer.Write{PrimitiveName(field.Type)}({access});";
	}

	private static string ReadFieldStatement(IFieldSymbol field)
	{
		var value = $"reader.Read{PrimitiveName(field.Type)}()";

		if (field.Type.TypeKind == TypeKind.Enum)
		{
			var typeName = field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			value = $"({typeName}){value}";
		}
		else if (UnderlyingSpecialType(field.Type) == SpecialType.System_String)
		{
			value += "!";
		}

		return $"packet.{field.Name} = {value};";
	}

	private static string GenerateSource(List<PacketModel> packets)
	{
		var sb = new StringBuilder();

		sb.AppendLine("// <auto-generated/>");
		sb.AppendLine("#nullable enable");
		sb.AppendLine();
		sb.AppendLine("using System;");
		sb.AppendLine("using EzMultiLib.IO;");
		sb.AppendLine("using EzMultiLib.Packets;");
		sb.AppendLine("using EzMultiLib.Peers;");
		sb.AppendLine("using EzMultiLib.Serialization.IO;");
		sb.AppendLine("using EzMultiLib.Serialization.Packets;");
		sb.AppendLine();

		AppendPacketAction(sb, packets);
		sb.AppendLine();
		AppendSerializer(sb, packets);

		return sb.ToString();
	}

	private static void AppendPacketAction(StringBuilder sb, List<PacketModel> packets)
	{
		sb.AppendLine("namespace EzMultiLib.Packets");
		sb.AppendLine("{");
		sb.AppendLine("    public static partial class PacketAction");
		sb.AppendLine("    {");

		foreach (var packet in packets)
		{
			sb.AppendLine($"        public const ushort {packet.Name}Id = {packet.Id};");
		}

		sb.AppendLine();

		foreach (var packet in packets)
		{
			sb.AppendLine($"        public static event Action<Peer?, {packet.TypeName}>? On{packet.Name};");
		}

		sb.AppendLine();
		sb.AppendLine("        public static ushort GetPacketId(IPacket packet)");
		sb.AppendLine("        {");
		sb.AppendLine("            switch (packet)");
		sb.AppendLine("            {");

		foreach (var packet in packets)
		{
			sb.AppendLine($"                case {packet.TypeName} _: return {packet.Name}Id;");
		}

		sb.AppendLine("                default:");
		sb.AppendLine("                    throw new ArgumentException($\"Unknown packet type '{packet?.GetType()}'\", nameof(packet));");
		sb.AppendLine("            }");
		sb.AppendLine("        }");
		sb.AppendLine();
		sb.AppendLine("        public static void AcceptPacket(Peer? peer, IPacket packet)");
		sb.AppendLine("        {");
		sb.AppendLine("            switch (packet)");
		sb.AppendLine("            {");

		foreach (var packet in packets)
		{
			sb.AppendLine($"                case {packet.TypeName} p:");
			sb.AppendLine($"                    On{packet.Name}?.Invoke(peer, p);");
			sb.AppendLine("                    break;");
		}

		sb.AppendLine("                default:");
		sb.AppendLine("                    throw new ArgumentException($\"Unknown packet type '{packet?.GetType()}'\", nameof(packet));");
		sb.AppendLine("            }");
		sb.AppendLine("        }");
		sb.AppendLine("    }");
		sb.AppendLine("}");
	}

	private static void AppendSerializer(StringBuilder sb, List<PacketModel> packets)
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
		sb.AppendLine("        public static bool TryRead(IPacketReader reader, out IPacket? packet)");
		sb.AppendLine("        {");
		sb.AppendLine("            packet = null;");
		sb.AppendLine();
		sb.AppendLine("            if (reader == null)");
		sb.AppendLine("                return false;");
		sb.AppendLine();
		sb.AppendLine("            var id = PacketFramer.ReadHeader(reader);");
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
		sb.AppendLine("        public static void Write(IPacketWriter writer, IPacket packet)");
		sb.AppendLine("        {");
		sb.AppendLine("            PacketFramer.WriteHeader(writer, PacketAction.GetPacketId(packet));");
		sb.AppendLine("            WriteBody(writer, packet);");
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
		sb.AppendLine("        public static void WriteBody(IPacketWriter writer, IPacket packet)");
		sb.AppendLine("        {");
		sb.AppendLine("            switch (packet)");
		sb.AppendLine("            {");

		foreach (var packet in packets)
		{
			sb.AppendLine($"                case {packet.TypeName} p:");
			sb.AppendLine($"                    Write{packet.Name}(writer, p);");
			sb.AppendLine("                    break;");
		}

		sb.AppendLine("                default:");
		sb.AppendLine("                    throw new ArgumentException($\"Unknown packet type '{packet?.GetType()}'\", nameof(packet));");
		sb.AppendLine("            }");
		sb.AppendLine("        }");
		sb.AppendLine();
		sb.AppendLine("        public static IPacket ReadBody(ushort id, IPacketReader reader)");
		sb.AppendLine("        {");
		sb.AppendLine("            if (!TryReadBody(id, reader, out var packet))");
		sb.AppendLine("                throw new MalformedPacketException($\"Unknown packet id '{id}'\");");
		sb.AppendLine();
		sb.AppendLine("            return packet!;");
		sb.AppendLine("        }");
		sb.AppendLine();
		sb.AppendLine("        public static bool TryReadBody(ushort id, IPacketReader reader, out IPacket? packet)");
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

			foreach (var field in packet.Fields)
				sb.AppendLine($"            {WriteFieldStatement(field)}");

			sb.AppendLine("        }");
			sb.AppendLine();
			sb.AppendLine($"        public static {packet.TypeName} Read{packet.Name}(IPacketReader reader)");
			sb.AppendLine("        {");
			sb.AppendLine($"            var packet = new {packet.TypeName}();");

			foreach (var field in packet.Fields)
				sb.AppendLine($"            {ReadFieldStatement(field)}");

			sb.AppendLine("            return packet;");
			sb.AppendLine("        }");
		}

		sb.AppendLine("    }");
		sb.AppendLine("}");
	}
}
