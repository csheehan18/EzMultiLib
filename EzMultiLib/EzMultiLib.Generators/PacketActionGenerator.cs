using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Text;

// Heart and soul of IPacket working behind the scenes, this should allow for clean usage of events without the user noticing

[Generator]
public sealed class PacketActionGenerator : ISourceGenerator
{
	public void Initialize(GeneratorInitializationContext context)
	{
		context.RegisterForSyntaxNotifications(
			() => new PacketSyntaxReceiver());
	}

	public void Execute(GeneratorExecutionContext context)
	{
		var receiver = context.SyntaxReceiver as PacketSyntaxReceiver;
		if (receiver == null)
			return;

		var compilation = context.Compilation;

		var ipacketSymbol =
			compilation.GetTypeByMetadataName("EzMultiLib.Packets.IPacket");

		if (ipacketSymbol == null)
			return;

		var packets = new List<INamedTypeSymbol>();

		foreach (var candidate in receiver.Candidates)
		{
			var model = compilation.GetSemanticModel(candidate.SyntaxTree);
			var symbol = model.GetDeclaredSymbol(candidate) as INamedTypeSymbol;
			if (symbol == null)
				continue;

			if (symbol.IsAbstract)
				continue;

			if (symbol.IsGenericType)
				continue;

			if (!symbol.AllInterfaces.Any(i =>
				SymbolEqualityComparer.Default.Equals(i, ipacketSymbol)))
				continue;

			packets.Add(symbol);
		}

		if (packets.Count == 0)
			return;

		packets = packets
			.Distinct(SymbolEqualityComparer.Default)
			.Cast<INamedTypeSymbol>()
			.OrderBy(p => p.ToDisplayString())
			.ToList();

		var source = GeneratePacketActionSource(packets);
		context.AddSource("PacketAction.g.cs", source);
	}

	private static string GeneratePacketActionSource(
		List<INamedTypeSymbol> packets)
	{
		var sb = new StringBuilder();

		sb.AppendLine("using System;");
		sb.AppendLine("using EzMultiLib;");
		sb.AppendLine("using EzMultiLib.Packets;");
		sb.AppendLine("using EzMultiLib.Peers;");
		sb.AppendLine("using EzMultiLib.IO;");
		sb.AppendLine();
		sb.AppendLine("namespace EzMultiLib.Packets");
		sb.AppendLine("{");
		sb.AppendLine("    public static partial class PacketAction");
		sb.AppendLine("    {");

		ushort id = 1;
		foreach (var pkt in packets)
		{
			sb.AppendLine(
				$"        public const ushort {pkt.Name}Id = {id};");
			id++;
		}

		sb.AppendLine();

		foreach (var pkt in packets)
		{
			var typeName =
				pkt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

			sb.AppendLine(
				$"        public static event Action<Peer?, {typeName}> On{pkt.Name};");
		}

		sb.AppendLine();

		sb.AppendLine("        public static void AcceptPacket(Peer? peer, IPacket packet)");
		sb.AppendLine("        {");
		sb.AppendLine("            switch (packet)");
		sb.AppendLine("            {");

		foreach (var pkt in packets)
		{
			var typeName =
				pkt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

			sb.AppendLine($"                case {typeName} p:");
			sb.AppendLine($"                    On{pkt.Name}?.Invoke(peer, p);");
			sb.AppendLine("                    break;");
		}

		sb.AppendLine("            }");
		sb.AppendLine("        }");

		sb.AppendLine();

		sb.AppendLine("        public static ushort GetPacketId(IPacket packet)");
		sb.AppendLine("        {");
		sb.AppendLine("            switch (packet)");
		sb.AppendLine("            {");

		foreach (var pkt in packets)
		{
			var typeName = pkt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

			sb.AppendLine(
				$"                case {typeName}: return {pkt.Name}Id;");
		}

		sb.AppendLine("                default:");
		sb.AppendLine("                    throw new ArgumentException(\"Unknown packet type\");");
		sb.AppendLine("            }");
		sb.AppendLine("        }");

		sb.AppendLine();
		sb.AppendLine("        public static IPacket CreatePacket(ushort id, IPacketReader reader)");
		sb.AppendLine("        {");
		sb.AppendLine("            switch (id)");
		sb.AppendLine("            {");

		foreach (var pkt in packets)
		{
			var typeName = pkt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

			sb.AppendLine($"                case {pkt.Name}Id:");
			sb.AppendLine($"                {{");
			sb.AppendLine($"                    var p = new {typeName}();");
			sb.AppendLine($"                    p.Deserialize(reader);");
			sb.AppendLine($"                    return p;");
			sb.AppendLine($"                }}");
		}

		sb.AppendLine("                default:");
		sb.AppendLine("                    throw new ArgumentException(\"Unknown packet id\", nameof(id));");
		sb.AppendLine("            }");
		sb.AppendLine("        }");

		sb.AppendLine("    }");
		sb.AppendLine("}");

		return sb.ToString();
	}
}