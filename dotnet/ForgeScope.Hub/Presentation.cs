using Microsoft.AspNetCore.WebUtilities;
using ForgeScope.Workbench;

namespace ForgeScope.Hub;

public static partial class Views
{
    public static string Theme(HttpContext ctx)
    {
        string? theme = ctx.Request.Query["theme"].FirstOrDefault() ?? ctx.Request.Cookies["ForgeScope.Theme"];
        return theme switch { "black" => "black", "light" => "light", _ => "dark" };
    }
    public static string SettingsUrl(HttpContext ctx, string language, string theme)
    {
        var query = ctx.Request.Query.Where(p => p.Key is not ("lang" or "theme"))
            .SelectMany(p => p.Value.Select(v => new KeyValuePair<string, string?>(p.Key, v))).ToList();
        query.Add(new("lang", Localization.Language(language))); query.Add(new("theme", theme is "black" or "light" ? theme : "dark"));
        return QueryHelpers.AddQueryString(ctx.Request.Path.Value ?? "/", query);
    }
    static string ErrorHint(Page p, string message)
    {
        if (message.Contains("TS2304", StringComparison.Ordinal) || message.Contains("CS0103", StringComparison.Ordinal) || message.Contains("NameError", StringComparison.Ordinal))
            return p.T("Check whether the name is declared, imported and available in this scope.", "Confira se o nome está declarado, importado e disponível neste escopo.", "Revisa si el nombre está declarado, importado y disponible en este ámbito.");
        if (message.Contains("CS0117", StringComparison.Ordinal) || message.Contains("cannot find symbol", StringComparison.OrdinalIgnoreCase) || message.Contains("undefined:", StringComparison.Ordinal))
            return p.T("Compare the referenced name and signature with the declaration at this commit.", "Compare o nome e a assinatura usados com a declaração neste commit.", "Compara el nombre y la firma utilizados con la declaración en este commit.");
        if (message.Contains("ModuleNotFoundError", StringComparison.Ordinal) || message.Contains("TS2307", StringComparison.Ordinal))
            return p.T("Check the import path, installed dependencies and the environment used by CI.", "Confira o caminho do import, as dependências instaladas e o ambiente usado pelo CI.", "Revisa la ruta de importación, las dependencias instaladas y el entorno del CI.");
        if (message.Contains("syntax", StringComparison.OrdinalIgnoreCase))
            return p.T("Inspect the syntax at the reported line and the preceding lines.", "Inspecione a sintaxe na linha informada e nas linhas anteriores.", "Inspecciona la sintaxis de la línea indicada y de las anteriores.");
        return p.T("Open the original execution and review the failing step before changing the code.", "Abra a execução original e revise a etapa que falhou antes de alterar o código.", "Abre la ejecución original y revisa el paso fallido antes de modificar el código.");
    }
    static string ErrorPanel(Page p, string title, string evidence, string path, string line, string source, string? url = null, bool diagnostic = true)
    {
        string location = path.Length == 0 ? p.T("No source location reported", "Localização no código não informada", "Ubicación en el código no informada") : path + (line.Length == 0 ? "" : ":" + line);
        return $"<section class=\"error-panel\"><div class=\"error-panel-heading\"><span class=\"error-dot\" aria-hidden=\"true\"></span><h3>{H(title)}</h3><span class=\"badge\">{H(source)}</span></div><dl class=\"error-facts\"><div><dt>{p.T("File and line", "Arquivo e linha", "Archivo y línea")}</dt><dd><code>{H(location)}</code></dd></div></dl><h4>{(diagnostic ? p.T("Original diagnostic", "Diagnóstico original", "Diagnóstico original") : p.T("Reported status", "Estado informado", "Estado informado"))}</h4><pre class=\"diagnostic\">{H(evidence)}</pre><div class=\"next-check\"><strong>{p.T("What to check", "O que verificar", "Qué revisar")}</strong><p>{H(ErrorHint(p, evidence))}</p></div><small>{p.T("Guidance, not a verified root cause. Original log messages are preserved in their source language.", "Orientação, não uma causa raiz comprovada. As mensagens originais dos logs são preservadas no idioma de origem.", "Orientación, no una causa raíz comprobada. Los mensajes originales se conservan en su idioma de origen.")}</small>{(url is null ? "" : $"<p><a href=\"{H(url)}\">{p.T("Open source at commit", "Abrir código no commit", "Abrir código en el commit")}</a></p>")}</section>";
    }
    public static string ErrorPage(HttpContext ctx, int status)
    {
        var p = new Page(ctx, "");
        string message = status switch
        {
            403 => p.T("This form expired or access was denied. Reload the page and try again.", "Este formulário expirou ou o acesso foi negado. Atualize a página e tente novamente.", "Este formulario caducó o se denegó el acceso. Actualiza la página e inténtalo de nuevo."),
            404 => p.T("This item was not found or is not available to your account.", "Este item não foi encontrado ou não está disponível para sua conta.", "No se encontró este elemento o no está disponible para tu cuenta."),
            409 => p.T("The action could not be completed. Review the data and reload the page before trying again.", "Não foi possível concluir a ação. Confira os dados e atualize a página antes de tentar novamente.", "No se pudo completar la acción. Revisa los datos y actualiza la página antes de volver a intentarlo."),
            503 => p.T("The service is temporarily unavailable. Please try again shortly.", "O serviço está temporariamente indisponível. Tente novamente em instantes.", "El servicio no está disponible temporalmente. Inténtalo de nuevo en unos instantes."),
            _ => p.T("Some request data is invalid. Review the form and try again.", "Alguns dados da solicitação são inválidos. Revise o formulário e tente novamente.", "Algunos datos de la solicitud no son válidos. Revisa el formulario e inténtalo de nuevo.")
        };
        p.Body.Append($"<section class=\"card\"><span class=\"badge\">{status}</span><h1>{p.T("Unable to complete this request", "Não foi possível concluir esta solicitação", "No se pudo completar esta solicitud")}</h1><p>{message}</p><a class=\"button\" href=\"/?lang={p.Lang}\">{p.T("Back to overview", "Voltar à visão geral", "Volver al resumen")}</a></section>");
        return p.ToString();
    }
}
