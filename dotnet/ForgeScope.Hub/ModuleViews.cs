namespace ForgeScope.Hub;

public static partial class Views
{
    static void Modules(Page p)
    {
        p.Body.Append($"<section id=\"modules\"><h2>{p.T("What ForgeScope brings together", "O que o ForgeScope reúne", "Qué reúne ForgeScope")}</h2><p>{p.T("One workspace with focused modules. Each result names its module, evidence and limits. An indication is a review lead, not a confirmed defect or an automatically applied fix.", "Um ambiente com módulos especializados. Cada resultado identifica o módulo, a evidência e as limitações. Uma indicação orienta a revisão; não representa um defeito confirmado nem uma correção aplicada automaticamente.", "Un espacio con módulos especializados. Cada resultado identifica el módulo, la evidencia y los límites.")}</p><div class=\"grid module-grid\">");
        foreach (var m in new[] {
            ("PatchScope", "Review changed files and patch risk signals before merging.", "Revisa arquivos alterados e sinais de risco do patch antes do merge.", "Revisa archivos cambiados y señales de riesgo antes de fusionar."),
            ("CIFuse", "Groups recognizable CI diagnostics so repeated messages become reviewable evidence.", "Agrupa diagnósticos reconhecíveis de CI para transformar mensagens repetidas em evidências de revisão.", "Agrupa diagnósticos reconocibles de CI para revisar la evidencia."),
            ("RepoRelief", "Checks repository documentation, local links, maintenance markers and conventional test names.", "Verifica documentação, links locais, marcadores de manutenção e nomes convencionais de testes do repositório.", "Revisa documentación, enlaces, marcadores y nombres de pruebas."),
            ("DevRescue", "Explains recognizable failures and suggests the next checks. Suggestions require review and do not execute fixes.", "Explica falhas reconhecíveis e sugere próximas verificações. As sugestões exigem revisão e não executam correções.", "Explica fallos reconocibles y sugiere comprobaciones; no ejecuta correcciones."),
            ("RevScope", "Recommends investigation tools from artifact evidence, such as a packet viewer for a capture or a decompiler for a binary.", "Recomenda ferramentas a partir de evidências de artefatos: um visualizador para capturas ou um decompilador para binários, por exemplo.", "Recomienda herramientas según los artefactos: visores de capturas o descompiladores de binarios."),
            ("Exports", "Exports the full investigation as JSON, and its findings as Markdown or SARIF.", "Exporta a investigação completa em JSON e seus achados em Markdown ou SARIF.", "Exporta la investigación como JSON y sus hallazgos como Markdown o SARIF.") })
            p.Body.Append($"<article class=\"card\" id=\"module-{m.Item1.ToLowerInvariant()}\"><h3>{m.Item1}</h3><p>{p.T(m.Item2,m.Item3,m.Item4)}</p></article>");
        p.Body.Append("</div></section>");
    }
    static void Recommendations(Page p, IEnumerable<Investigation> investigations, bool catalog = false)
    {
        var b = p.Body; int count = 0;
        b.Append($"<section id=\"recommendations\" class=\"card\"><h2>{p.T("Recommended next steps", "Próximos passos recomendados", "Siguientes pasos recomendados")}</h2><p>{p.T("Recommendations are based on saved analysis findings and artifact metadata. Tools are suggested, never launched, installed or sent your source automatically.", "As recomendações usam achados das análises salvas e metadados de artefatos. As ferramentas são indicadas; não são abertas, instaladas nem recebem seu código automaticamente.", "Las recomendaciones usan hallazgos guardados y metadatos. No se ejecutan, instalan ni reciben código automáticamente.")}</p>");
        using var resource = typeof(Views).Assembly.GetManifestResourceStream("ForgeScope.Hub.tools.json")!;
        using var tools = System.Text.Json.JsonDocument.Parse(resource);
        foreach (var value in investigations.Take(8))
        {
            string reportUrl = $"/investigations/{value.Id}?lang={p.Lang}";
            foreach (var signal in value.Report.TryGetProperty("artifacts", out var artifacts) ? Items(artifacts, "signals").Take(8) : [])
                foreach (var id in Items(signal, "tool_ids"))
                {
                    var tool = tools.RootElement.EnumerateArray().FirstOrDefault(t => Text(t, "id") == id.ToString());
                    if (tool.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    b.Append($"<article class=\"notice-row\"><span class=\"badge\">RevScope</span><h3>{H(Text(tool,"name"))} · {H(value.Title)}</h3><p><code>{H(Text(signal,"path"))}</code> · {p.L(Text(signal,"kind"))}</p><p>{p.L(Text(signal,"verification"))}. {p.L(Text(tool,"bestFor"))}</p><p>{p.L(Text(signal,"notes"))}</p><a href=\"{H(reportUrl)}#evidence\">{p.T("View supporting evidence", "Ver evidência da indicação", "Ver evidencia")}</a> · <a href=\"{H(Text(tool,"url"))}\" target=\"_blank\" rel=\"noopener noreferrer\">{p.T("Official tool", "Ferramenta oficial", "Herramienta oficial")}</a></article>");
                    count++;
                }
            foreach (var engine in Items(value.Report, "findings").Select(f => Text(f,"engine")).Distinct().Where(e => e is "patchscope" or "cifuse" or "reporelief" or "devrescue").Take(4))
            {
                string name = engine switch { "patchscope" => "PatchScope", "cifuse" => "CIFuse", "reporelief" => "RepoRelief", _ => "DevRescue" };
                b.Append($"<article class=\"notice-row\"><span class=\"badge\">{name}</span><h3>{H(value.Title)}</h3><p>{p.T("This module reported findings in your saved analysis. Review the evidence and proposed next checks.", "Este módulo encontrou sinais na sua análise salva. Confira a evidência e as próximas verificações propostas.", "Este módulo encontró señales en tu análisis. Revisa la evidencia y los siguientes pasos.")}</p><a href=\"{H(reportUrl)}#evidence\">{p.T("Open findings", "Abrir achados", "Abrir hallazgos")}</a></article>"); count++;
            }
        }
        if (count == 0) b.Append($"<p>{p.T("Analyze a project first to receive evidence-based recommendations. The catalog below remains available for manual exploration.", "Analise um projeto para receber recomendações com evidências. O catálogo continua disponível para exploração manual.", "Analiza un proyecto para recibir recomendaciones. El catálogo está disponible para exploración manual.")}</p><a href=\"/?lang={p.Lang}#start\">{p.T("Analyze a project", "Analisar um projeto", "Analizar un proyecto")}</a>");
        if (!catalog) b.Append($"<p><a href=\"/catalog?lang={p.Lang}\">{p.T("Explore RevScope recommendations and tools", "Explorar recomendações e ferramentas do RevScope", "Explorar recomendaciones y herramientas")}</a></p>");
        b.Append("</section>");
    }
}
