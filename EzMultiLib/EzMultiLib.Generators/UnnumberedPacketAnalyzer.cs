using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

// An analyzer rather than part of the generator, so the fix attached to it is offered in
// the editor and can be applied in bulk by dotnet format. Its view of a packet is
// advisory: getting it wrong costs a missing or spurious hint, never a wrong byte, since
// the generator decides the wire on its own.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnnumberedPacketAnalyzer : DiagnosticAnalyzer
{
	public const string DiagnosticId = "EZML012";

	private static readonly DiagnosticDescriptor UnnumberedPacket = new DiagnosticDescriptor(
		DiagnosticId,
		"Packet does not number its fields",
		"Packet '{0}' does not number its fields, so their order on the wire comes from their names and renaming one silently changes that order; number them with [Field(n)] to pin it",
		"EzMultiLib",
		DiagnosticSeverity.Info,
		true);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; }
		= ImmutableArray.Create(UnnumberedPacket);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
	}

	private static void Analyze(SymbolAnalysisContext context)
	{
		if (!(context.Symbol is INamedTypeSymbol type))
			return;

		var ipacket = context.Compilation.GetTypeByMetadataName("EzMultiLib.Packets.IPacket");

		if (ipacket == null || !IsPacket(type, ipacket))
			return;

		var fields = SerializedFields(type).ToList();

		if (fields.Count == 0)
			return;

		var fieldAttribute = context.Compilation.GetTypeByMetadataName("EzMultiLib.Packets.FieldAttribute");

		// Numbering some but not all of them is the generator's error to report, so only
		// the completely unnumbered case is a hint.
		if (fieldAttribute != null && fields.Any(f => HasFieldAttribute(f, fieldAttribute)))
			return;

		foreach (var location in type.Locations.Where(l => l.IsInSource))
		{
			context.ReportDiagnostic(Diagnostic.Create(
				UnnumberedPacket,
				location,
				type.ToDisplayString()));

			return;
		}
	}

	public static bool IsPacket(INamedTypeSymbol type, INamedTypeSymbol ipacket)
	{
		if (type.IsAbstract || type.IsGenericType)
			return false;

		if (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct)
			return false;

		return type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, ipacket));
	}

	// Matches what the generator serializes: public instance fields, this type's and its
	// bases', which is also the set that has to be numbered all together or not at all.
	public static System.Collections.Generic.IEnumerable<IFieldSymbol> SerializedFields(INamedTypeSymbol type)
	{
		for (var current = type; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
		{
			foreach (var field in current.GetMembers().OfType<IFieldSymbol>())
			{
				if (field.IsStatic || field.IsConst || field.IsImplicitlyDeclared)
					continue;

				if (field.DeclaredAccessibility != Accessibility.Public)
					continue;

				yield return field;
			}
		}
	}

	public static bool HasFieldAttribute(IFieldSymbol field, INamedTypeSymbol fieldAttribute) =>
		field.GetAttributes().Any(a =>
			SymbolEqualityComparer.Default.Equals(a.AttributeClass, fieldAttribute));
}
