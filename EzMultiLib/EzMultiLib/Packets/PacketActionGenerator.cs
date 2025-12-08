using System.Text;

namespace EzMultiLib.Packets
{
	public static class PacketActionGenerator
	{
		// First iteration. I think Im gonna rewrite this to be a [Generator] which could run before compile
		public static void GeneratePackAction()
		{
			string outputPath = Path.Combine(AppContext.BaseDirectory,"PacketAction.g.cs");

			var type = typeof(IPacket);
			var classes = AppDomain.CurrentDomain.GetAssemblies()
				.SelectMany(s => s.GetTypes())
				.Where(t => type.IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
				.ToList();

			var sb = new StringBuilder();

			sb.AppendLine("using EzMultiLib;");
			sb.AppendLine("using EzMultiLib.Packets;");
			sb.AppendLine("using System;");
			sb.AppendLine();
			sb.AppendLine("public static class PacketAction");
			sb.AppendLine("{");

			// Generate actions
			foreach (var c in classes)
			{
				sb.AppendLine($"    public static Action<Client, {c.Name}> On{c.Name};");
			}

			sb.AppendLine();
			sb.AppendLine("    public static void AcceptPacket<T>(Client c, T pkt) where T : IPacket");
			sb.AppendLine("    {");
			sb.AppendLine("        switch (pkt)");
			sb.AppendLine("        {");

			foreach (var c in classes)
			{
				sb.AppendLine($"            case {c.Name} typed:");
				sb.AppendLine($"                On{c.Name}?.Invoke(c, typed);");
				sb.AppendLine("                break;");
			}

			sb.AppendLine("            default:");
			sb.AppendLine("                break;");
			sb.AppendLine("        }");
			sb.AppendLine("    }");

			sb.AppendLine("}");

			File.WriteAllText(outputPath, sb.ToString());
		}
	}
}
