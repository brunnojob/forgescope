namespace ForgeScope.Hub;

public static partial class Views
{
    public static string Inbox(HttpContext ctx, string token, List<Alert> alerts, List<RepoWatch> watches)
    {
        var p = new Page(ctx, token); var b = p.Body;
        string filter = ctx.Request.Query["filter"].ToString(), repo = ctx.Request.Query["repository"].ToString(), q = ctx.Request.Query["q"].ToString();
        string Link(string f = "", string r = "") => $"/notifications?lang={p.Lang}&amp;filter={Uri.EscapeDataString(f)}&amp;repository={Uri.EscapeDataString(r)}";
        b.Append("<style>.inbox-layout{display:grid;grid-template-columns:230px 1fr;gap:24px}.inbox-filters a{display:block;padding:10px 0}.inbox-row{border:1px solid var(--line);border-bottom:0;padding:18px;background:var(--panel)}.inbox-row:last-child{border-bottom:1px solid var(--line)}.inbox-row.unread{border-left:4px solid var(--accent)}.inbox-row h2{font-size:17px;margin:10px 0}.inbox-row form{display:inline-block;margin:8px 8px 0 0}.inbox-row button{font-size:13px;padding:6px 10px}.inbox-row pre{max-height:400px;overflow:auto}.watch{border-top:1px solid var(--line);padding-top:12px}.watch form{margin:10px 0}select{padding:10px;background:var(--bg);color:var(--text);border:1px solid var(--line)}@media(max-width:1000px){.inbox-layout{grid-template-columns:1fr}.inbox-filters{display:flex;gap:12px;flex-wrap:wrap}}</style>");
        b.Append($"<div class=\"eyebrow\">FORGESCOPE</div><h1>{p.T("Error dashboard", "Painel de erros", "Panel de errores")}</h1><p>{p.T("CI failures across your repositories, with the diagnostic GitHub actually reported.", "Falhas de CI nos seus repositórios, com o diagnóstico que o GitHub realmente informou.", "Fallos de CI de tus repositorios, con el diagnóstico que GitHub realmente informó.")}</p><div class=\"inbox-layout\"><section class=\"inbox-filters\">");
        foreach (var (f, label) in new[] { ("", p.T("Inbox", "Caixa de entrada", "Bandeja de entrada")), ("unread", p.T("Unread", "Não lidas", "No leídas")), ("saved", p.T("Saved", "Salvas", "Guardadas")), ("done", p.T("Done", "Concluídas", "Completadas")) })
            b.Append($"<a href=\"{Link(f)}\"{(filter == f ? " aria-current=\"page\"" : "")}>{label} <span class=\"badge\">{alerts.Count(a => f switch { "unread" => !a.Read && !a.Done, "saved" => a.Saved, "done" => a.Done, _ => !a.Done })}</span></a>");
        b.Append($"<h3>{p.T("Monitored repositories", "Repositórios monitorados", "Repositorios monitoreados")}</h3>");
        foreach (var watch in watches)
            b.Append($"<div class=\"watch\"><a href=\"{Link(filter, watch.Repository)}\">{H(watch.Repository)}</a><small>{(watch.CheckedAt is { } at ? " · " + at.ToString("u") : "")}</small>{(watch.Error.Length > 0 ? $"<p role=\"status\">{p.T("Sync unavailable. Check access and retry.", "Sincronização indisponível. Confira o acesso e tente novamente.", "Sincronización no disponible. Revisa el acceso e inténtalo de nuevo.")}</p>" : "")}<form method=\"post\" action=\"/notifications/watch\">{p.Hidden()}<input type=\"hidden\" name=\"repository\" value=\"{H(watch.Repository)}\"><input type=\"hidden\" name=\"action\" value=\"stop\"><button>{p.T("Stop monitoring", "Parar monitoramento", "Dejar de monitorear")}</button></form></div>");
        b.Append($"<a href=\"/repositories?lang={p.Lang}\">+ {p.T("Add repository", "Adicionar repositório", "Añadir repositorio")}</a></section><section><form method=\"get\" action=\"/notifications\"><input type=\"hidden\" name=\"lang\" value=\"{p.Lang}\"><input type=\"hidden\" name=\"filter\" value=\"{H(filter)}\"><input type=\"hidden\" name=\"repository\" value=\"{H(repo)}\"><label>{p.T("Search notifications", "Buscar notificações", "Buscar notificaciones")}<input name=\"q\" value=\"{H(q)}\" maxlength=\"200\" placeholder=\"{p.T("Repository, error or file", "Repositório, erro ou arquivo", "Repositorio, error o archivo")}\"></label><button>{p.T("Search", "Buscar", "Buscar")}</button></form><form method=\"post\" action=\"/notifications/sync\">{p.Hidden()}<input type=\"hidden\" name=\"repository\" value=\"{H(repo)}\"><button>{p.T("Sync now", "Sincronizar agora", "Sincronizar ahora")}</button></form><p class=\"muted\">{p.T("Automatic checks every two minutes while your GitHub session is active and the server is running. Up to 20 recent runs per repository are inspected; the latest 500 alerts are shown. Refresh this page for new results.", "Verificação automática a cada dois minutos enquanto sua sessão GitHub estiver ativa e o servidor estiver em execução. São consultadas até 20 execuções recentes por repositório; os últimos 500 alertas são exibidos. Atualize a página para ver novos resultados.", "Verificación automática cada dos minutos mientras tu sesión GitHub esté activa y el servidor esté en ejecución. Se consultan hasta 20 ejecuciones recientes por repositorio; se muestran las últimas 500 alertas. Actualiza la página para ver nuevos resultados.")}</p>");
        var selected = alerts.Where(a => filter switch { "saved" => a.Saved, "done" => a.Done, "unread" => !a.Read && !a.Done, _ => !a.Done })
            .Where(a => repo.Length == 0 || a.Repository == repo).Where(a => q.Length == 0 || $"{a.Repository} {a.Workflow} {a.Job} {a.Step} {a.Message} {a.Path}".Contains(q, StringComparison.OrdinalIgnoreCase)).ToArray();
        b.Append("<section class=\"insights\" aria-label=\"" + p.T("Filtered results", "Resultados filtrados", "Resultados filtrados") + "\">");
        foreach (var (count, label) in new[] {
            (selected.Select(a => (a.Repository, a.Run, a.Attempt)).Distinct().Count(), p.T("Failed executions", "Execuções com falha", "Ejecuciones fallidas")),
            (selected.Count(a => a.HasSource && a.Line > 0), p.T("Diagnostics with file and line", "Diagnósticos com arquivo e linha", "Diagnósticos con archivo y línea")),
            (selected.Count(a => a.EvidenceKind == "step"), p.T("Failures without details", "Falhas sem detalhes", "Fallos sin detalles")),
            (selected.Select(a => a.Repository).Distinct().Count(), p.T("Repositories in this view", "Repositórios nesta visão", "Repositorios en esta vista")) })
            b.Append($"<div class=\"card\"><strong class=\"metric\">{count}</strong><span class=\"metric-caption\">{label}</span></div>");
        b.Append("</section><div class=\"breakdown\">");
        foreach (var group in selected.GroupBy(a => a.Repository).OrderByDescending(g => g.Count()).Take(8))
            b.Append($"<a class=\"badge\" href=\"{Link(filter, group.Key)}\">{H(group.Key)} · {group.Count()} {p.T("alerts", "alertas", "alertas")}</a>");
        b.Append("</div>");
        if (selected.Length == 0) b.Append($"<div class=\"card\"><h2>{p.T("No matching alerts", "Nenhum alerta correspondente", "No hay alertas coincidentes")}</h2><p>{p.T("Add a repository and synchronize. An empty inbox does not certify that your code is free of problems.", "Adicione um repositório e sincronize. Uma caixa vazia não certifica que seu código esteja livre de problemas.", "Añade un repositorio y sincroniza. Una bandeja vacía no certifica que tu código no tenga problemas.")}</p></div>");
        // Group generic process status behind the meaningful diagnostic from the same step.
        var groups = selected.GroupBy(a => (a.Repository, a.Run, a.Attempt, a.Job, a.Step));
        foreach (var group in groups)
        {
            var ordered = group.OrderBy(a => AlertEvidence.Generic(a.Message) ? 1 : 0).ThenBy(a => a.EvidenceKind == "step" ? 1 : 0).ToArray();
            var a = ordered[0];
            string message = a.EvidenceKind == "step" ? p.T("This execution failed, but no detailed diagnostic was available.", "Esta execução falhou, mas não havia diagnóstico detalhado disponível.", "Esta ejecución falló, pero no había un diagnóstico detallado disponible.") : a.Message;
            string preview = message.Split('\n')[0];
            b.Append($"<article class=\"inbox-row{(!a.Read ? " unread" : "")}\"><small>{H(a.Repository)} · {H(a.Workflow)} · #{a.Run} · {p.T("Attempt", "Tentativa", "Intento")} {a.Attempt}</small><h2>{H(a.Job)}{(a.Step.Length > 0 ? " · " + H(a.Step) : "")}</h2><p>{H(preview[..Math.Min(240, preview.Length)])}</p><details{(ctx.Request.Query["alert"] == a.Key || string.IsNullOrEmpty(ctx.Request.Query["alert"]) && a == selected[0] ? " open" : "")}><summary>{p.T("View error panel", "Ver painel do erro", "Ver panel del error")}</summary>");
            b.Append(ErrorPanel(p, p.T("Error details", "Detalhes do erro", "Detalles del error"), message, a.HasSource ? a.Path : "", a.HasSource ? a.Line?.ToString() ?? "" : "", a.EvidenceKind switch {
                "annotation" => p.T("GitHub annotation", "Anotação do GitHub", "Anotación de GitHub"),
                "log" => p.T("Execution diagnostic", "Diagnóstico da execução", "Diagnóstico de ejecución"),
                _ => p.T("Failure without detailed diagnostic", "Falha sem diagnóstico detalhado", "Fallo sin diagnóstico detallado") }, a.SourceUrl, a.EvidenceKind != "step"));
            foreach (var related in ordered.Skip(1))
                b.Append(ErrorPanel(p, p.T("Related diagnostic", "Diagnóstico relacionado", "Diagnóstico relacionado"), related.Message, related.HasSource ? related.Path : "", related.HasSource ? related.Line?.ToString() ?? "" : "", p.T("Same execution step", "Mesma etapa da execução", "Mismo paso de ejecución"), related.SourceUrl));
            b.Append($"<dl class=\"error-facts\"><div><dt>{p.T("Commit", "Commit", "Commit")}</dt><dd><code>{H(a.Commit)}</code></dd></div></dl><p><a href=\"{H(a.RunUrl)}\" target=\"_blank\" rel=\"noopener noreferrer\">{p.T("Open original execution", "Abrir execução original", "Abrir ejecución original")}</a></p></details>");
            foreach (var item in ordered)
            {
                if (ordered.Length > 1) b.Append($"<small>{H(item.Message.Split('\n')[0][..Math.Min(100, item.Message.Split('\n')[0].Length)])}</small> ");
            foreach (var (action, label) in new[] { (item.Read ? "unread" : "read", item.Read ? p.T("Mark unread", "Marcar não lida", "Marcar no leída") : p.T("Mark read", "Marcar lida", "Marcar leída")), (item.Saved ? "unsave" : "save", item.Saved ? p.T("Unsave", "Remover de salvas", "Quitar de guardadas") : p.T("Save", "Salvar", "Guardar")), (item.Done ? "reopen" : "done", item.Done ? p.T("Reopen", "Reabrir", "Reabrir") : p.T("Done", "Concluir", "Completar")) })
                b.Append($"<form method=\"post\" action=\"/notifications/{item.Key}/{action}\">{p.Hidden()}<button>{label}</button></form>");
            }
            b.Append("</article>");
        }
        b.Append("</section></div>"); return p.ToString();
    }
}
