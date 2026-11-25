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
        if (message.Contains("429", StringComparison.Ordinal) && (message.Contains("HTTP", StringComparison.OrdinalIgnoreCase) || message.Contains("rate", StringComparison.OrdinalIgnoreCase)))
            return p.T("The remote service reported a request limit (HTTP 429). Check dependency caching and retry with backoff; respect Retry-After when supplied. This does not by itself prove a source-code bug.", "O serviço remoto informou um limite de requisições (HTTP 429). Confira o cache de dependências e novas tentativas com espera; respeite Retry-After quando informado. Isso, sozinho, não comprova um bug no código.", "El servicio remoto informó un límite de solicitudes (HTTP 429). Revisa la caché y los reintentos con espera; respeta Retry-After si está disponible. Esto no demuestra un fallo de código.");
        if (message.Contains("No artifacts named", StringComparison.OrdinalIgnoreCase))
            return p.T("The deployment could not find the expected artifact. Check that the upload step succeeded, the artifact name matches and both steps refer to the same workflow run.", "O deploy não encontrou o artefato esperado. Confira se o envio terminou com sucesso, se o nome corresponde e se as duas etapas usam a mesma execução do workflow.", "El despliegue no encontró el artefacto esperado. Revisa la carga, el nombre y que ambos pasos usen la misma ejecución.");
        if (AlertEvidence.Generic(message))
            return p.T("This exit code only confirms that the process failed. Read the related diagnostics in this panel; if none were supplied, the cause is still unknown.", "Este código de saída apenas confirma que o processo falhou. Leia os diagnósticos relacionados neste painel; se nenhum foi fornecido, a causa ainda é desconhecida.", "Este código de salida solo confirma que el proceso falló. Lee los diagnósticos relacionados; si no se proporcionaron, la causa sigue siendo desconocida.");
        if (message.Contains("TS2304", StringComparison.Ordinal) || message.Contains("CS0103", StringComparison.Ordinal) || message.Contains("NameError", StringComparison.Ordinal))
            return p.T("Check whether the name is declared, imported and available in this scope.", "Confira se o nome está declarado, importado e disponível neste escopo.", "Revisa si el nombre está declarado, importado y disponible en este ámbito.");
        if (message.Contains("CS0117", StringComparison.Ordinal) || message.Contains("cannot find symbol", StringComparison.OrdinalIgnoreCase) || message.Contains("undefined:", StringComparison.Ordinal))
            return p.T("Compare the referenced name and signature with the declaration at this commit.", "Compare o nome e a assinatura usados com a declaração neste commit.", "Compara el nombre y la firma utilizados con la declaración en este commit.");
        if (message.Contains("ModuleNotFoundError", StringComparison.Ordinal) || message.Contains("TS2307", StringComparison.Ordinal))
            return p.T("Check the import path, installed dependencies and the environment used by CI.", "Confira o caminho do import, as dependências instaladas e o ambiente usado pelo CI.", "Revisa la ruta de importación, las dependencias instaladas y el entorno del CI.");
        if (message.Contains("syntax", StringComparison.OrdinalIgnoreCase))
            return p.T("Inspect the syntax at the reported line and the preceding lines.", "Inspecione a sintaxe na linha informada e nas linhas anteriores.", "Inspecciona la sintaxis de la línea indicada y de las anteriores.");
        return p.T("Review the reported diagnostic and failing step. Confirm the cause before changing code; the original execution is available as supporting evidence.", "Confira o diagnóstico informado e a etapa que falhou. Confirme a causa antes de alterar o código; a execução original está disponível como evidência complementar.", "Abre la ejecución original y revisa el paso fallido antes de modificar el código.");
    }
    static string ErrorPanel(Page p, string title, string evidence, string path, string line, string source, string? url = null, bool diagnostic = true)
    {
        string location = path.Length == 0 ? p.T("No source location reported", "Localização no código não informada", "Ubicación en el código no informada") : path + (line.Length == 0 ? "" : ":" + line);
        return $"<section class=\"error-panel\"><div class=\"error-panel-heading\"><span class=\"error-dot\" aria-hidden=\"true\"></span><h3>{H(title)}</h3><span class=\"badge\">{H(source)}</span></div><dl class=\"error-facts\"><div><dt>{p.T("File and line", "Arquivo e linha", "Archivo y línea")}</dt><dd><code>{H(location)}</code></dd></div></dl><h4>{(diagnostic ? p.T("Original diagnostic", "Diagnóstico original", "Diagnóstico original") : p.T("Reported status", "Estado informado", "Estado informado"))}</h4><pre class=\"diagnostic\">{H(evidence)}</pre><div class=\"next-check\"><strong>{p.T("What to check", "O que verificar", "Qué revisar")}</strong><p>{H(ErrorHint(p, evidence))}</p></div><small>{p.T("Guidance, not a verified root cause. Original diagnostics are preserved in their source language.", "Orientação, não uma causa raiz comprovada. Os diagnósticos originais são preservados no idioma de origem.", "Orientación, no una causa raíz comprobada. Los mensajes originales se conservan en su idioma de origen.")}</small>{(url is null ? "" : $"<p><a href=\"{H(url)}\" target=\"_blank\" rel=\"noopener noreferrer\">{p.T("Open source at commit", "Abrir código no commit", "Abrir código en el commit")}</a></p>")}</section>";
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
