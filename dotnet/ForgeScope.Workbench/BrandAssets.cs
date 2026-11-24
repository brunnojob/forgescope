namespace ForgeScope.Workbench;

public static class BrandAssets
{
    public static readonly string[] Names = ["forgescope-github.png", "forgescope-logo.png", "favicon-32.png", "apple-touch-icon.png", "favicon.ico"];
    public static IResult Get(string name)
    {
        if (!Names.Contains(name, StringComparer.Ordinal)) return Results.NotFound();
        using var stream = typeof(BrandAssets).Assembly.GetManifestResourceStream("ForgeScope.Brand." + name);
        if (stream is null) return Results.NotFound();
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        return Results.File(bytes.ToArray(), name.EndsWith(".ico", StringComparison.Ordinal) ? "image/x-icon" : "image/png");
    }
    public const string Head = """
        <link rel="icon" href="/favicon.ico" sizes="16x16 32x32 48x48">
        <link rel="icon" type="image/png" sizes="32x32" href="/assets/branding/favicon-32.png">
        <link rel="apple-touch-icon" sizes="180x180" href="/assets/branding/apple-touch-icon.png">
        <meta name="theme-color" content="#0b1118">
        """;
}
