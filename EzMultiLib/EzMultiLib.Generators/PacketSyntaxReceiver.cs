using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
internal sealed class PacketSyntaxReceiver : ISyntaxReceiver
{
	public List<TypeDeclarationSyntax> Candidates { get; }
		= new List<TypeDeclarationSyntax>();

	public void OnVisitSyntaxNode(SyntaxNode node)
	{
		if (node is TypeDeclarationSyntax typeDecl)
		{
			Candidates.Add(typeDecl);
		}
	}
}
