using System.Text;
using System.Text.Json;

namespace ForgeScope.Hub;

public static class HubExports
{
    public static string Markdown(Investigation value)
    {
        static string Text(string s) => s.Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
        var b = new StringBuilder("# ForgeScope investigation\n\n");
        b.AppendLine(Text(value.Title)).AppendLine().Append("Source: ").AppendLine(value.Origin).Append("Snapshot: ").AppendLine(value.Commit).AppendLine();
        foreach (var finding in Views.Items(value.Report, "findings"))
        {
            b.Append("## ").AppendLine(Text(Views.Text(finding,"title"))).AppendLine();
            b.AppendLine(Text(Views.Text(finding,"engine") + " · " + Views.Text(finding,"path") + ":" + Views.Text(finding,"line"))).AppendLine();
            foreach (var line in Views.Text(finding,"evidence").Split('\n')) b.Append("> ").AppendLine(Text(line));
            b.AppendLine().AppendLine(Text(Views.Text(finding,"action"))).AppendLine();
        }
        return b.ToString();
    }
    public static byte[] Sarif(Investigation value)
    {
        var results = new List<object>();
        foreach (var finding in Views.Items(value.Report,"findings"))
        {
            string path = Views.Text(finding,"path"); int.TryParse(Views.Text(finding,"line"),out int line);
            var physical = new Dictionary<string,object> { ["artifactLocation"] = new { uri=string.Join('/',path.Split('/').Select(Uri.EscapeDataString)), uriBaseId="%SRCROOT%" } };
            if (line > 0) physical["region"] = new { startLine=line };
            results.Add(new { ruleId=Views.Text(finding,"engine")+"/"+Views.Text(finding,"rule"),
                level=Views.Text(finding,"priority") switch { "high" or "error"=>"error", "medium"=>"warning", _=>"note" },
                message=new { text=Views.Text(finding,"title")+"\n"+Views.Text(finding,"evidence")+"\n"+Views.Text(finding,"action") },
                locations=GitHub.SafePath(path) ? new object[] { new { physicalLocation=physical } } : [] });
        }
        return JsonSerializer.SerializeToUtf8Bytes(new { version="2.1.0", runs=new[] { new { tool=new { driver=new { name="ForgeScope", informationUri="https://github.com/brunnojob/forgescope" } }, results } } });
    }
}
