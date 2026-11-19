using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text;

namespace ForgeScope.Workbench;

public static class Localization
{
    static readonly JsonDocument Catalog=JsonDocument.Parse(ReadResource("i18n.json"));
    public static string Language(string? language)=>language?.ToLowerInvariant() switch {"pt" or "pt-br"=>"pt-BR","es" or "es-es"=>"es",_=>"en"};
    public static string ReadResource(string name)
    {
        using var stream=typeof(Localization).Assembly.GetManifestResourceStream("ForgeScope."+name)??throw new InvalidOperationException("Missing presentation resource: "+name);
        using var reader=new StreamReader(stream);return reader.ReadToEnd();
    }
    public static bool Contains(string source)=>Catalog.RootElement.TryGetProperty(source,out _);
    public static string Get(string source,string language)
    {
        var lang=Language(language);if(lang=="en")return source;
        if(Catalog.RootElement.TryGetProperty(source,out var row)&&row.TryGetProperty(lang,out var value))return value.GetString()!;
        if(System.Text.RegularExpressions.Regex.IsMatch(source,@"^\d+ lines$"))return source.Replace("lines",lang=="es"?"líneas":"linhas",StringComparison.Ordinal);
        return source;
    }
    static readonly string[] Titles=["Overview","Potential impact","Tests and engines","References and calls","Review the evidence","Know the limits"];
    static readonly string[] Descriptions=[
        "Start here: these four counts summarize this report. They describe the supplied example, not a quality or security score.",
        "Follow the paths from changed Java and C# files to their dependents. A path is a lead to investigate, not proof that something broke.",
        "Check the suggested tests and the status of every analyzer. Skipped or unavailable analysis must not be mistaken for a clean result.",
        "Inspect unresolved references and source-resolved calls. File paths, method names and original compiler messages stay unchanged in every language.",
        "Open Evidence & action on a finding to see its context and a suggested next step. Review prompts are not confirmed vulnerabilities.",
        "Read the limitations and compiler diagnostics before drawing conclusions. Export JSON, SARIF or Markdown to continue your investigation."];
    public static string Tour(int step,string lang)
    {
        string T(string s)=>HtmlEncoder.Default.Encode(Get(s,lang));
        var b=new StringBuilder($"<aside class=\"tour\" id=\"tour-{step}\" tabindex=\"-1\" aria-label=\"{T("Guided demonstration")}\"><span class=\"eyebrow\">{T("Step")} {step} {T("of")} 6</span><h2>{T(Titles[step-1])}</h2><p>{T(Descriptions[step-1])}</p><nav>");
        if(step>1)b.Append($"<a class=\"button\" href=\"#tour-{step-1}\">{T("Previous")}</a>");
        b.Append($"<a class=\"button primary\" href=\"{(step==6?"#overview":"#tour-"+(step+1))}\">{T(step==6?"Finish tour":"Next")}</a><a href=\"#overview\">{T("Close tour")}</a></nav></aside>");return b.ToString();
    }
}
