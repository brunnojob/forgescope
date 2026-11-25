using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ForgeScope.Workbench;

namespace ForgeScope.Hub;

public static partial class Views
{
    public static string Language(HttpContext ctx) => Localization.Language(ctx.Request.Query["lang"].FirstOrDefault() ?? ctx.Request.Cookies["ForgeScope.Language"]);
    public static string H(object? value) => HtmlEncoder.Default.Encode(value?.ToString() ?? "");
    public static JsonElement[] Items(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray().ToArray() : [];
    public static string Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var text) ? text.ToString() : "";
    sealed class Page
    {
        public readonly string Lang;
        public readonly string Token;
        public readonly StringBuilder Body = new();
        public Page(HttpContext ctx, string token)
        {
            Lang = Language(ctx); Token = token;
            if (ctx.Request.Query.ContainsKey("lang")) ctx.Response.Cookies.Append("ForgeScope.Language", Lang, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = ctx.Request.IsHttps, MaxAge = TimeSpan.FromDays(365) });
            string theme = ctx.Request.Query["theme"].FirstOrDefault() ?? ctx.Request.Cookies["ForgeScope.Theme"] ?? "dark";
            theme = theme == "light" ? "light" : "dark";
            if (ctx.Request.Query.ContainsKey("theme")) ctx.Response.Cookies.Append("ForgeScope.Theme", theme, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = ctx.Request.IsHttps, MaxAge = TimeSpan.FromDays(365) });
            Body.Append($"<!doctype html><html lang=\"{Lang}\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>ForgeScope</title>{BrandAssets.Head}<style>{Css}</style></head><body class=\"{theme}\"><aside><a class=\"brand\" href=\"/?lang={Lang}\"><img src=\"/assets/branding/forgescope-logo.png\" width=\"32\" height=\"32\" alt=\"\">Forge<span>Scope</span></a><p>{T("Engineering workbench", "Central de engenharia", "Centro de ingeniería")}</p><nav><a href=\"/?lang={Lang}\">{T("Overview", "Visão geral", "Vista general")}</a><a href=\"/repositories?lang={Lang}\">{T("Repositories", "Repositórios", "Repositorios")}</a><a href=\"/notifications?lang={Lang}\">{T("Notifications", "Notificações", "Notificaciones")}</a><a href=\"/catalog?lang={Lang}\">RevScope</a><a href=\"https://github.com/brunnojob/forgescope\">GitHub</a></nav><p>{T("Repository monitoring · Any language", "Monitoramento de repositórios · Qualquer linguagem", "Monitoreo de repositorios · Cualquier lenguaje")}</p></aside><main><header><div class=\"languages\">");
            foreach (var (id, label) in new[] { ("en", "English"), ("pt-BR", "Português"), ("es", "Español") }) Body.Append($"<a href=\"{H(ctx.Request.Path)}?lang={id}&amp;theme={theme}\" lang=\"{id}\">{label}</a> ");
            Body.Append($"</div><a href=\"{H(ctx.Request.Path)}?lang={Lang}&amp;theme={(theme == "light" ? "dark" : "light")}\">{T("Switch theme", "Trocar tema", "Cambiar tema")}</a>");
            if (ctx.User.Identity?.IsAuthenticated == true) Body.Append($"<span>{H(ctx.User.Identity.Name)}</span><form method=\"post\" action=\"/logout\">{Hidden()}<button>{T("Sign out", "Sair", "Salir")}</button></form>");
            Body.Append("</header>");
        }
        public string T(string en, string pt, string es) => Lang switch { "pt-BR" => pt, "es" => es, _ => en };
        public string L(string text) => H(Localization.Get(text, Lang));
        public string Hidden() => $"<input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{H(Token)}\"><input type=\"hidden\" name=\"lang\" value=\"{Lang}\">";
        public string Form(Investigation value, string action) => $"<form method=\"post\" action=\"/investigations/{value.Id}/{action}\">{Hidden()}<input type=\"hidden\" name=\"version\" value=\"{value.Version}\"><input type=\"hidden\" name=\"digest\" value=\"{H(value.Revisions.LastOrDefault()?.Digest)}\">";
        public string State(string state) => state switch
        {
            "investigation" => T("Investigation", "Investigação", "Investigación"), "review" => T("Review pending", "Revisão pendente", "Revisión pendiente"),
            "pending" => T("Pending", "Pendente", "Pendiente"), "approved" => T("Approved", "Aprovado", "Aprobado"),
            "rejected" => T("Rejected", "Rejeitado", "Rechazado"), "queued" => T("Awaiting runner", "Aguardando executor", "Esperando ejecutor"),
            "running" => T("Tests running", "Testes em execução", "Pruebas en ejecución"), "validated" => T("Validated by runner", "Validado pelo executor", "Validado por el ejecutor"),
            "failed" => T("Validation failed", "Validação falhou", "Validación fallida"), "superseded" => T("Superseded", "Versão substituída", "Versión sustituida"),
            "cancelled" => T("Cancelled", "Cancelado", "Cancelado"), _ => H(state)
        };
        public override string ToString() => Body + "</main></body></html>";
    }
    public static string Dashboard(HttpContext ctx, string token, List<Investigation> values, bool privateRepos)
    {
        var p = new Page(ctx, token); var b = p.Body;
        b.Append($"<div class=\"eyebrow\">FORGESCOPE HUB</div><h1>{p.T("Follow a change to a tested result.", "Da mudança ao resultado testado.", "Del cambio al resultado probado.")}</h1><p class=\"muted\">{p.T("Evidence, review and validation in one workspace.", "Evidências, revisão e validação em um único ambiente.", "Evidencias, revisión y validación en un solo espacio.")}</p>");
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            b.Append($"<section class=\"card\"><h2>{p.T("Sign in with GitHub", "Entrar com GitHub", "Entrar con GitHub")}</h2><p>{p.T("Your investigations and reviews belong to your GitHub identity.", "Suas investigações e revisões ficam vinculadas à sua identidade GitHub.", "Tus investigaciones y revisiones se vinculan a tu identidad GitHub.")}</p><a class=\"button primary\" href=\"/auth/github?lang={p.Lang}\">{p.T("Continue with GitHub", "Continuar com GitHub", "Continuar con GitHub")}</a><p class=\"muted\">{(privateRepos ? p.T("This server requests repo scope for private repositories; GitHub displays the permissions before authorization.", "Este servidor solicita o escopo repo para repositórios privados; o GitHub apresenta as permissões antes da autorização.", "Este servidor solicita el alcance repo para repositorios privados; GitHub muestra los permisos antes de autorizar.") : p.T("Public repositories by default. No GitHub password is collected here.", "Repositórios públicos por padrão. Sua senha do GitHub não é coletada aqui.", "Repositorios públicos por defecto. Aquí no se recoge tu contraseña de GitHub."))}</p></section>");
            if (ctx.Request.Query["auth"] == "failed") b.Append($"<p role=\"alert\">{p.T("Sign-in failed or was cancelled. Try again.", "O login falhou ou foi cancelado. Tente novamente.", "El inicio de sesión falló o fue cancelado. Inténtalo de nuevo.")}</p>");
            return p.ToString();
        }
        b.Append($"<div class=\"actions\"><a class=\"button primary\" href=\"/notifications?lang={p.Lang}\">{p.T("Open notifications", "Abrir notificações", "Abrir notificaciones")}</a></div><section class=\"stats\">");
        foreach (var (count, label) in new[] { (values.Count, p.T("Recent investigations", "Investigações recentes", "Investigaciones recientes")), (values.Count(v => v.Stage == "review"), p.T("Pending reviews", "Revisões pendentes", "Revisiones pendientes")), (values.Count(v => v.Stage == "validated"), p.T("Validated", "Validados", "Validados")) }) b.Append($"<div class=\"card\"><strong>{count}</strong>{label}</div>");
        b.Append($"</section><div class=\"actions\"><a class=\"button primary\" href=\"/repositories?lang={p.Lang}\">{p.T("Analyze a repository", "Analisar um repositório", "Analizar un repositorio")}</a></div><section class=\"grid\">");
        foreach (var value in values) b.Append($"<article class=\"card\"><span class=\"badge\">{p.State(value.Stage)}</span><h2><a href=\"/investigations/{value.Id}?lang={p.Lang}\">{H(value.Title)}</a></h2><code>{H(value.Commit[..Math.Min(12, value.Commit.Length)])}</code><p>{Items(value.Report, "findings").Length} {p.T("review findings", "sinais para revisão", "señales para revisión")}</p><small>{value.UpdatedAt:u}</small></article>");
        b.Append("</section>");
        b.Append($"<details class=\"card\"><summary>{p.T("Import a local report", "Importar relatório local", "Importar informe local")}</summary><p>{p.T("For larger repositories. Review the report before uploading. Its contents and declared commit are supplied by you and are not independently verified.", "Para repositórios maiores. Revise o relatório antes do envio. O conteúdo e o commit declarado são fornecidos por você e não são verificados independentemente.", "Para repositorios grandes. Revisa el informe antes de subirlo. Tú proporcionas el contenido y el commit declarado, sin verificación independiente.")}</p><form method=\"post\" action=\"/import\">{p.Hidden()}<label>{p.T("Repository", "Repositório", "Repositorio")}<input name=\"repository\" placeholder=\"owner/repo\" required></label><label>Commit SHA<input name=\"commit\" minlength=\"40\" maxlength=\"40\" required></label><label>report.json<textarea name=\"report\" rows=\"6\" required></textarea></label><button>{p.T("Import", "Importar", "Importar")}</button></form></details>");
        return p.ToString();
    }
    public static string Repositories(HttpContext ctx, string token, JsonElement rows, int page)
    {
        var p = new Page(ctx, token); var b = p.Body;
        b.Append($"<h1>{p.T("Your repositories", "Seus repositórios", "Tus repositorios")}</h1><p>{p.T("Read from GitHub with your authorized permissions. Choose a repository to start an investigation.", "Consultados no GitHub com suas permissões autorizadas. Escolha um repositório para iniciar uma investigação.", "Consultados en GitHub con tus permisos autorizados. Elige un repositorio para iniciar una investigación.")}</p><section class=\"grid\">");
        foreach (var row in rows.EnumerateArray())
        {
            string name = Text(row, "full_name");
            b.Append($"<article class=\"card\"><h2>{H(name)}</h2><form method=\"post\" action=\"/notifications/watch\">{p.Hidden()}<input type=\"hidden\" name=\"repository\" value=\"{H(name)}\"><button>{p.T("Monitor CI failures", "Monitorar falhas de CI", "Monitorear fallos de CI")}</button></form><p>{H(Text(row, "description"))}</p><span class=\"badge\">{H(Text(row, "language"))}</span> ★ {H(Text(row, "stargazers_count"))}<details><summary>{p.T("Start investigation", "Iniciar investigação", "Iniciar investigación")}</summary><form method=\"post\" action=\"/investigations\">{p.Hidden()}<input type=\"hidden\" name=\"repository\" value=\"{H(name)}\"><label>{p.T("PR number (optional)", "Número do PR (opcional)", "Número de PR (opcional)")}<input type=\"number\" min=\"1\" name=\"pr\"></label><label>{p.T("GitHub Actions run ID (optional)", "ID da execução do GitHub Actions (opcional)", "ID de ejecución de GitHub Actions (opcional)")}<input type=\"number\" min=\"1\" name=\"run\"></label><label>{p.T("CI log (optional, processed temporarily)", "Log de CI (opcional, processado temporariamente)", "Log de CI (opcional, procesado temporalmente)")}<textarea name=\"log\" rows=\"4\" maxlength=\"2000000\"></textarea></label><p class=\"muted\">{p.T("The server reads a bounded source snapshot and stores derived evidence. Review sensitive content before submitting. Builds and tests do not run during analysis.", "O servidor lê uma cópia limitada do código e salva evidências derivadas. Revise conteúdo sensível antes do envio. Builds e testes não são executados na análise.", "El servidor lee una copia limitada del código y guarda evidencias derivadas. Revisa el contenido sensible antes de enviar. El análisis no ejecuta builds ni pruebas.")}</p><button>{p.T("Analyze", "Analisar", "Analizar")}</button></form></details></article>");
        }
        b.Append($"</section><nav class=\"actions\"><a href=\"/repositories?page={Math.Max(1, page - 1)}&amp;lang={p.Lang}\">{p.T("Previous", "Anterior", "Anterior")}</a><a href=\"/repositories?page={page + 1}&amp;lang={p.Lang}\">{p.T("Next", "Próximo", "Siguiente")}</a></nav>"); return p.ToString();
    }
    public static string Detail(HttpContext ctx, string token, Investigation value)
    {
        var p = new Page(ctx, token); var b = p.Body; var report = value.Report;
        b.Append($"<span class=\"badge\">{p.State(value.Stage)}</span><h1>{H(value.Title)}</h1><code>{H(value.Commit)}</code><div class=\"actions\"><a href=\"#evidence\">{p.T("Evidence", "Evidências", "Evidencias")}</a><a href=\"#review\">{p.T("Plan and review", "Plano e revisão", "Plan y revisión")}</a><a href=\"#validation\">{p.T("Validation", "Validação", "Validación")}</a><a href=\"#history\">{p.T("History", "Histórico", "Historial")}</a><a href=\"/investigations/{value.Id}/export\">JSON</a></div>");
        b.Append($"<section id=\"evidence\" class=\"card\"><h2>{p.T("Investigation evidence", "Evidências da investigação", "Evidencias de la investigación")}</h2><p>{p.T("Related files are leads to investigate; they do not prove the cause of a failure.", "Arquivos relacionados são pistas de investigação; não comprovam a causa de uma falha.", "Los archivos relacionados son pistas; no demuestran la causa de un fallo.")}</p>");
        if (value.Ci is { } ci)
        {
            b.Append($"<div class=\"chain\"><a href=\"https://github.com/{H(value.Repository)}/actions/runs/{ci.Id}\">GitHub Actions #{ci.Id}</a> · {H(ci.Status)} · {H(ci.Conclusion)}<p><code>{H(ci.Commit)}</code></p>");
            if (ci.Commit != value.Commit) b.Append($"<p>{p.T("This CI run uses a different commit. Its failures are context, not evidence against this exact snapshot.", "Esta execução de CI usa outro commit. Suas falhas são contexto, não evidência contra esta cópia exata.", "Esta ejecución de CI usa otro commit. Sus fallos son contexto, no evidencia contra esta copia exacta.")}</p>");
            b.Append("</div>");
        }
        if (report.TryGetProperty("impact", out var impact))
        {
            b.Append($"<h3>{p.T("Source dependency paths", "Caminhos de dependência", "Rutas de dependencia")}</h3>");
            foreach (var edge in Items(impact, "edges").Take(500)) b.Append($"<div class=\"chain\"><code>{H(Text(edge, "source"))}:{H(Text(edge, "line"))} → {H(Text(edge, "target"))}</code></div>");
            b.Append($"<h3>{p.T("Test candidates", "Testes candidatos", "Pruebas candidatas")}</h3>");
            foreach (var candidate in Items(impact, "test_candidates")) b.Append($"<p><code>{H(candidate)}</code></p>");
        }
        b.Append($"<h3>{p.T("Analysis status", "Estado da análise", "Estado del análisis")}</h3>");
        if (report.TryGetProperty("engines", out var engines) && engines.ValueKind == JsonValueKind.Object)
            foreach (var engine in engines.EnumerateObject()) b.Append($"<span class=\"badge\">{H(engine.Name)} · {p.L(Text(engine.Value, "status"))}</span> ");
        foreach (var finding in Items(report, "findings").Take(1000)) b.Append($"<details class=\"finding\"><summary><span class=\"badge\">{H(Text(finding, "engine"))} · {p.L(Text(finding, "priority"))}</span> {p.L(Text(finding, "title"))}</summary><code>{H(Text(finding, "path"))}:{H(Text(finding, "line"))}</code><pre>{p.L(Text(finding, "evidence"))}</pre><p>{p.L(Text(finding, "action"))}</p></details>");
        b.Append($"<details><summary>RevScope · {p.T("Artifacts and tools", "Artefatos e ferramentas", "Artefactos y herramientas")}</summary>");
        if (report.TryGetProperty("artifacts", out var artifacts)) foreach (var signal in Items(artifacts, "signals")) b.Append($"<div class=\"chain\"><code>{H(Text(signal, "path"))}</code><p>{p.L(Text(signal, "kind"))} · {p.L(Text(signal, "verification"))}</p><p>{p.L(Text(signal, "notes"))}</p></div>");
        foreach (var tool in Items(report, "tools")) b.Append($"<p><strong>{H(Text(tool, "name"))}</strong> {p.L(Text(tool, "summary"))}</p>");
        b.Append($"</details><details><summary>{p.T("Limits", "Limitações", "Límites")}</summary>");
        foreach (var limitation in Items(report, "limitations")) b.Append($"<p>{p.L(limitation.ToString())}</p>");
        b.Append($"</details></section><section class=\"card\" id=\"review\"><h2>PlanRelay · {p.T("Plan and review", "Plano e revisão", "Plan y revisión")}</h2><p>{p.T("Changing the plan or patch creates a pending revision. Comments and decisions stay with their original revision.", "Alterar o plano ou patch cria uma revisão pendente. Comentários e decisões permanecem na versão original.", "Cambiar el plan o parche crea una revisión pendiente. Los comentarios y decisiones permanecen en su versión original.")}</p>");
        b.Append(p.Form(value, "revise") + $"<label>{p.T("Repair plan", "Plano de correção", "Plan de corrección")}<textarea name=\"plan\" rows=\"8\" maxlength=\"64000\" required>{H(value.Revisions.LastOrDefault()?.Plan)}</textarea></label><label>{p.T("Reviewed unified patch", "Patch unificado para revisão", "Parche unificado para revisión")}<textarea name=\"patch\" rows=\"8\" maxlength=\"2000000\">{H(value.Revisions.LastOrDefault()?.Patch)}</textarea></label><button>{p.T("Save revision", "Salvar versão", "Guardar versión")}</button></form>");
        if (value.Revisions.Count > 0)
        {
            var current = value.Current;
            b.Append($"<h3>{p.T("Current revision", "Versão atual", "Versión actual")} {current.Number} · {p.State(current.Decision)}</h3><code>{H(current.Digest)}</code><ol class=\"source\">");
            foreach (var line in current.Plan.Split('\n')) b.Append($"<li><pre>{H(line)}</pre></li>"); b.Append("</ol>");
            foreach (var comment in current.Comments)
            {
                b.Append($"<div class=\"chain\"><strong>{p.T("Line", "Linha", "Línea")} {comment.Line}</strong> · {(comment.Blocker ? p.T("Blocker", "Bloqueio", "Bloqueo") : p.T("Comment", "Comentário", "Comentario"))} · {(comment.Resolved ? p.T("Resolved", "Resolvido", "Resuelto") : p.T("Open", "Aberto", "Abierto"))}<p>{H(comment.Body)}</p>");
                if (!comment.Resolved && current.Decision == "pending") b.Append(p.Form(value, "resolve") + $"<input type=\"hidden\" name=\"comment\" value=\"{comment.Id}\"><button>{p.T("Resolve", "Resolver", "Resolver")}</button></form>"); b.Append("</div>");
            }
            if (current.Decision == "pending")
            {
                b.Append(p.Form(value, "comment") + $"<label>{p.T("Line", "Linha", "Línea")}<input name=\"line\" type=\"number\" value=\"1\" min=\"1\" max=\"{current.Plan.Split('\n').Length}\" required></label><label>{p.T("Comment", "Comentário", "Comentario")}<textarea name=\"body\" rows=\"3\" maxlength=\"4000\" required></textarea></label><label><input type=\"checkbox\" name=\"blocker\" value=\"true\">{p.T("Blocks approval", "Bloqueia aprovação", "Bloquea la aprobación")}</label><button>{p.T("Add comment", "Comentar", "Comentar")}</button></form>");
                b.Append(p.Form(value, "decide") + $"<button name=\"decision\" value=\"approved\">{p.T("Approve revision", "Aprovar versão", "Aprobar versión")}</button> <button name=\"decision\" value=\"rejected\">{p.T("Request changes", "Solicitar alterações", "Solicitar cambios")}</button></form>");
            }
        }
        b.Append($"</section><section class=\"card\" id=\"validation\"><h2>{p.T("Validate a reviewed correction", "Validar uma correção revisada", "Validar una corrección revisada")}</h2><p>{p.T("Tests run in your trusted local checkout using the command you explicitly provide. A disposable Git worktree isolates edits, not execution privileges. Validation covers only those tests and is reported by your runner.", "Os testes rodam no seu checkout local confiável com o comando fornecido explicitamente por você. Um worktree descartável isola alterações, não permissões de execução. A validação cobre somente esses testes e é informada pelo seu executor.", "Las pruebas se ejecutan en tu checkout local confiable con el comando que proporcionas explícitamente. Un worktree desechable aísla los cambios, no los permisos de ejecución. La validación cubre esas pruebas y la informa tu ejecutor.")}</p>");
        if (value.Revisions.Count > 0 && value.Current.Decision == "approved")
        {
            b.Append(p.Form(value, "queue") + $"<button>{p.T("Queue reviewed patch", "Solicitar validação do patch", "Solicitar validación del parche")}</button></form>");
            b.Append(p.Form(value, "runner-token") + $"<button>{p.T("Create one-hour runner token", "Criar token de executor por uma hora", "Crear token de ejecutor por una hora")}</button></form>");
        }
        foreach (var job in value.Jobs.AsEnumerable().Reverse())
        {
            b.Append($"<div class=\"chain\"><strong>{p.State(job.State)}</strong><p><code>{H(job.Digest)}</code></p>");
            if (job.Result is { } result) b.Append($"<pre>{p.T("Before", "Antes", "Antes")}: exit={H(result.Before.ExitCode)} timeout={result.Before.TimedOut}\n{p.T("After", "Depois", "Después")}: exit={H(result.After.ExitCode)} timeout={result.After.TimedOut}</pre>");
            if (job.State is "queued" or "running") b.Append(p.Form(value, "cancel") + $"<input type=\"hidden\" name=\"job\" value=\"{job.Id}\"><button>{p.T("Cancel", "Cancelar", "Cancelar")}</button></form>"); b.Append("</div>");
        }
        b.Append($"</section><section class=\"card\" id=\"history\"><h2>{p.T("History", "Histórico", "Historial")}</h2>");
        foreach (var revision in value.Revisions.AsEnumerable().Reverse())
        {
            b.Append($"<details><summary>{p.T("Revision", "Versão", "Versión")} {revision.Number} · {p.State(revision.Decision)}</summary><code>{H(revision.Digest)}</code><pre>{H(revision.Plan)}</pre><pre>{H(revision.Patch)}</pre>");
            foreach (var comment in revision.Comments) b.Append($"<p>{comment.Line}: {H(comment.Body)} · {(comment.Resolved ? p.T("Resolved", "Resolvido", "Resuelto") : p.T("Open", "Aberto", "Abierto"))}</p>");
            b.Append("</details>");
        }
        foreach (var e in value.Events.AsEnumerable().Reverse()) b.Append($"<p><time>{e.At:u}</time> · {H(e.Actor)} · <code>{H(e.Action)}</code> · <code>{H(e.Digest[..Math.Min(12, e.Digest.Length)])}</code></p>");
        b.Append("</section>"); return p.ToString();
    }
    public static string Catalog(HttpContext ctx, string token)
    {
        var p = new Page(ctx, token); var b = p.Body;
        using var stream = typeof(Views).Assembly.GetManifestResourceStream("ForgeScope.Hub.tools.json")!;
        using var catalog = JsonDocument.Parse(stream);
        string query = ctx.Request.Query["q"].ToString();
        b.Append($"<h1>RevScope</h1><p>{p.T("Find a tool and understand its purpose before investigating an artifact.", "Encontre uma ferramenta e entenda sua finalidade antes de investigar um artefato.", "Encuentra una herramienta y entiende su finalidad antes de investigar un artefacto.")}</p><form method=\"get\"><input type=\"hidden\" name=\"lang\" value=\"{p.Lang}\"><label>{p.T("Search tools, platforms or categories", "Pesquisar ferramentas, plataformas ou categorias", "Buscar herramientas, plataformas o categorías")}<input name=\"q\" value=\"{H(query)}\" maxlength=\"200\"></label><button>{p.T("Search", "Pesquisar", "Buscar")}</button></form><section class=\"grid\">");
        foreach (var tool in catalog.RootElement.EnumerateArray().Where(t => (t.ToString() + Localization.Get(Text(t, "summary"), p.Lang)).Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            b.Append($"<article class=\"card\"><span class=\"badge\">{p.L(Text(tool, "category"))}</span><h2>{H(Text(tool, "name"))}</h2><p>{p.L(Text(tool, "summary"))}</p><p>{p.L(Text(tool, "bestFor"))}</p><p>{H(Text(tool, "platform"))} · {H(Text(tool, "license"))} · {p.L(Text(tool, "cost"))}</p>");
            foreach (var (field, label) in new[] { ("url", p.T("Official project", "Projeto oficial", "Proyecto oficial")), ("docs", p.T("Documentation", "Documentação", "Documentación")) })
                if (Uri.TryCreate(Text(tool, field), UriKind.Absolute, out var url) && url.Scheme == "https" && url.UserInfo.Length == 0) b.Append($"<p><a href=\"{H(url)}\">{label}</a></p>");
            b.Append("</article>");
        }
        b.Append("</section>"); return p.ToString();
    }
    public static string RunnerToken(HttpContext ctx, string token, Investigation value)
    {
        var p = new Page(ctx, ""); p.Body.Append($"<h1>{p.T("Runner token", "Token do executor", "Token del ejecutor")}</h1><p>{p.T("Shown once. Valid for one hour and only this investigation. Set it as FORGESCOPE_RUNNER_TOKEN in your local terminal; do not commit it.", "Exibido uma vez. Válido por uma hora e somente para esta investigação. Defina como FORGESCOPE_RUNNER_TOKEN no terminal local; não inclua no Git.", "Se muestra una vez. Válido por una hora y solo para esta investigación. Defínelo como FORGESCOPE_RUNNER_TOKEN en tu terminal local; no lo incluyas en Git.")}</p><pre>{H(token)}</pre><pre>dotnet ForgeScope.Hub.dll --runner {H(ctx.Request.Scheme)}://{H(ctx.Request.Host)} --repository {H(value.Repository)} --checkout /trusted/checkout --jar /path/forgescope.jar --test-command JSON_ARGV</pre><p>{p.T("The command handles one queued validation and exits. The checkout must be clean and match the investigation commit.", "O comando processa uma validação pendente e encerra. O checkout deve estar limpo e corresponder ao commit da investigação.", "El comando procesa una validación pendiente y termina. El checkout debe estar limpio y coincidir con el commit de la investigación.")}</p>"); return p.ToString();
    }
    const string Css = """
        :root{color-scheme:dark;--bg:#0b1118;--panel:#121d29;--text:#edf3f8;--muted:#99adbf;--line:#2b3d50;--accent:#97efc2}*{box-sizing:border-box}body{margin:0;font:15px system-ui;background:var(--bg);color:var(--text)}body.light{color-scheme:light;--bg:#f2f6f9;--panel:#fff;--text:#132235;--muted:#4f6477;--line:#ccdae4;--accent:#086644}aside{position:fixed;width:220px;inset:0 auto 0 0;border-right:1px solid var(--line);padding:30px 22px;background:var(--panel)}.brand{display:inline-flex;align-items:center;gap:4px;font-size:22px;font-weight:850;text-decoration:none}.brand span{color:var(--accent)}aside p{font-size:12px;color:var(--muted)}aside nav{display:grid;gap:22px;margin:48px 0}main{margin-left:220px;max-width:1700px;padding:25px 4vw 70px}header{display:flex;gap:15px;align-items:center;flex-wrap:wrap;margin-bottom:48px}.languages{margin-right:auto;display:flex;gap:12px}header form{margin:0}h1{font-size:clamp(30px,4vw,50px);letter-spacing:-1.5px;max-width:950px}h2{font-size:21px}h3{font-size:17px}p{line-height:1.7}.muted{color:var(--muted)}a{color:var(--accent);overflow-wrap:anywhere}.eyebrow{letter-spacing:3px;font-size:12px;color:var(--accent)}.stats,.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:18px;margin:26px 0}.card{background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:24px;margin:18px 0}.grid .card,.stats .card{margin:0}.stats strong{display:block;font-size:36px;margin-bottom:7px}.badge{display:inline-block;border:1px solid var(--line);border-radius:20px;padding:5px 10px;font-size:12px;color:var(--accent)}.actions{display:flex;gap:20px;flex-wrap:wrap;align-items:center;margin:30px 0}.button,button{display:inline-block;padding:11px 18px;background:var(--panel);color:var(--text);border:1px solid var(--line);border-radius:6px;text-decoration:none;cursor:pointer;font:inherit}.primary{background:var(--accent);color:var(--bg);font-weight:700}form{margin:24px 0}label{display:block;margin:16px 0}input:not([type=checkbox]),textarea{display:block;width:100%;margin-top:8px;background:var(--bg);color:var(--text);border:1px solid var(--line);border-radius:5px;padding:11px;font:inherit}input[type=checkbox]{margin-right:10px}textarea,code,pre{font-family:ui-monospace,monospace}pre{white-space:pre-wrap;overflow-wrap:anywhere;line-height:1.65}code{overflow-wrap:anywhere;color:var(--accent)}summary{cursor:pointer;line-height:1.7}.finding{border-top:1px solid var(--line);padding:16px 0}.chain{border-left:2px solid var(--accent);padding:9px 15px;margin:15px 0}.source{padding-left:40px}.source li{padding-left:12px}.source pre{margin:0}section{scroll-margin-top:20px}small{color:var(--muted)}@media(max-width:800px){aside{position:static;width:auto;border-right:0;border-bottom:1px solid var(--line);padding:20px}aside nav{display:flex;margin:20px 0;flex-wrap:wrap}aside p{display:none}main{margin:0;padding:25px 6vw}header{margin-bottom:25px}.languages{width:100%}.stats,.grid{grid-template-columns:1fr}}
        """;
}
