using System.Text.RegularExpressions;
using ForgeScope.Workbench;

namespace ForgeScope.Hub;

public static class AuthNavigation
{
    static bool Page(string path) => path is "/" or "/repositories" or "/notifications" or "/catalog" ||
        Regex.IsMatch(path, "^/investigations/[a-fA-F0-9-]{36}$", RegexOptions.None, TimeSpan.FromSeconds(1));
    public static string ReturnTo(string? value, string language)
    {
        // Only known local HTML pages can be OAuth destinations. Never accept API/auth routes or external origins.
        if (value is { Length: > 0 and <= 2048 } && !value.Contains('\\') && !value.Contains('#') && !value.Any(char.IsControl) && Page(value.Split('?')[0]))
            return value;
        return "/?lang=" + Localization.Language(language);
    }
    public static bool BrowserPage(HttpRequest request) => HttpMethods.IsGet(request.Method) && Page(request.Path.Value ?? "") &&
        request.Headers.Accept.Any(v => v?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);
    public static string LoginUrl(HttpRequest request, string language) => "/auth/github?lang=" + Localization.Language(language) +
        "&returnTo=" + Uri.EscapeDataString(ReturnTo(request.Path + request.QueryString, language));
}
