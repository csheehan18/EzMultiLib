using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// The other half of EZML012. Numbering a packet by hand is easy to get wrong and easy to
// skip; this writes the numbers, and the batch fixer does a whole project at once.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NumberPacketFieldsCodeFix)), Shared]
public sealed class NumberPacketFieldsCodeFix : CodeFixProvider
{
	public override ImmutableArray<string> FixableDiagnosticIds { get; }
		= ImmutableArray.Create("EZML012");

	public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

	public override async Task RegisterCodeFixesAsync(CodeFixContext context)
	{
		var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

		if (root == null || model == null)
			return;

		foreach (var diagnostic in context.Diagnostics)
		{
			var declaration = root.FindNode(diagnostic.Location.SourceSpan)
				.AncestorsAndSelf()
				.OfType<TypeDeclarationSyntax>()
				.FirstOrDefault();

			if (declaration == null)
				continue;

			if (!(model.GetDeclaredSymbol(declaration, context.CancellationToken) is INamedTypeSymbol type))
				continue;

			// Numbering has to cover inherited fields too, and those live in a
			// declaration this fix is not looking at. Leave them to be done by hand
			// rather than write numbers that only count half the packet.
			if (HasInheritedFields(type))
				continue;

			context.RegisterCodeFix(
				CodeAction.Create(
					"Number this packet's fields",
					token => Apply(context.Document, declaration, type, token),
					nameof(NumberPacketFieldsCodeFix)),
				diagnostic);
		}
	}

	private static async Task<Document> Apply(
		Document document,
		TypeDeclarationSyntax declaration,
		INamedTypeSymbol type,
		CancellationToken token)
	{
		// Name order is the order the generator already serializes in, so every field
		// keeps the position it had and nothing gets transposed. The packet does gain
		// the one byte optional count that numbering brings with it, so adopting
		// numbering is a one time wire change: do both sides together.
		var order = type.GetMembers()
			.OfType<IFieldSymbol>()
			.Where(IsSerialized)
			.Select(f => f.Name)
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToList();

		var members = new List<MemberDeclarationSyntax>();

		foreach (var member in declaration.Members)
		{
			if (!(member is FieldDeclarationSyntax field) || !CanNumber(field, order))
			{
				members.Add(member);
				continue;
			}

			// An attribute goes on the declaration, not the variable, so one that
			// declares several fields has to become several declarations.
			foreach (var variable in field.Declaration.Variables)
			{
				var number = order.IndexOf(variable.Identifier.ValueText) + 1;

				members.Add(field
					.WithDeclaration(field.Declaration.WithVariables(
						SyntaxFactory.SingletonSeparatedList(variable)))
					.WithAttributeLists(field.AttributeLists.Add(FieldAttribute(number)))
					.WithTriviaFrom(field)
					.WithAdditionalAnnotations(Formatter.Annotation));
			}
		}

		var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);

		return document.WithSyntaxRoot(root!.ReplaceNode(
			declaration,
			declaration.WithMembers(SyntaxFactory.List(members))));
	}

	private static AttributeListSyntax FieldAttribute(int number) =>
		SyntaxFactory.AttributeList(
			SyntaxFactory.SingletonSeparatedList(
				SyntaxFactory.Attribute(
					SyntaxFactory.ParseName("EzMultiLib.Packets.FieldAttribute"),
					SyntaxFactory.AttributeArgumentList(
						SyntaxFactory.SingletonSeparatedList(
							SyntaxFactory.AttributeArgument(
								SyntaxFactory.LiteralExpression(
									SyntaxKind.NumericLiteralExpression,
									SyntaxFactory.Literal(number))))))))
			.WithAdditionalAnnotations(Simplifier.Annotation);

	private static bool CanNumber(FieldDeclarationSyntax field, List<string> order) =>
		field.Declaration.Variables.All(v => order.Contains(v.Identifier.ValueText));

	private static bool HasInheritedFields(INamedTypeSymbol type)
	{
		for (var b = type.BaseType; b != null && b.SpecialType != SpecialType.System_Object; b = b.BaseType)
		{
			if (b.GetMembers().OfType<IFieldSymbol>().Any(IsSerialized))
				return true;
		}

		return false;
	}

	private static bool IsSerialized(IFieldSymbol field) =>
		!field.IsStatic
		&& !field.IsConst
		&& !field.IsImplicitlyDeclared
		&& field.DeclaredAccessibility == Accessibility.Public;
}
