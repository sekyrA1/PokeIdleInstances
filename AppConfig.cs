using System.Text.Json;

namespace PokeIdleMulti;

/// <summary>
/// Armazena e persiste as configurações do launcher em config.json ao lado do executável.
/// </summary>
public sealed class AppConfig
{
    private static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "config.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string TargetUrl { get; set; } = "https://pokeidle.io/app";
    public string UrlExtension { get; set; } = "/app";
    public string[] InstanceNames { get; set; } = ["Conta 1", "Conta 2", "Conta 3", "Conta 4"];
    public bool IsGridMode { get; set; } = false;
    public bool GlobalEcoMode { get; set; } = false;
    public bool GlobalUltraMode { get; set; } = false;
    public bool GlobalHuntOnlyMode { get; set; } = false;
    public bool GlobalNoSpritesMode { get; set; } = false;
    public bool LoadHuntExtension { get; set; } = true;
    public bool AutoEcoOnInactiveTabs { get; set; } = true;
    public bool GlobalMuted { get; set; } = true;
    public int ActiveTabIndex { get; set; } = 0;

    /// <summary>
    /// Carrega a configuração do disco ou cria valores padrão seguros se não existir.
    /// </summary>
    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    return loaded;
                }
            }
        }
        catch
        {
            // Fallback para configuração padrão caso o JSON esteja corrompido
        }

        var defaults = new AppConfig();
        defaults.Save();
        return defaults;
    }

    /// <summary>
    /// Garante exatamente 4 nomes válidos, índice ativo no intervalo [0, 3] e URL válida.
    /// </summary>
    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(TargetUrl))
            TargetUrl = "https://pokeidle.io/app";

        if (string.IsNullOrWhiteSpace(UrlExtension))
            UrlExtension = "/app";
        else
            UrlExtension = UrlExtension.Trim();

        var normalized = new string[4];
        for (int i = 0; i < 4; i++)
        {
            var candidate = InstanceNames is { Length: > 0 } && i < InstanceNames.Length
                ? InstanceNames[i]
                : null;
            normalized[i] = string.IsNullOrWhiteSpace(candidate) ? $"Conta {i + 1}" : candidate.Trim();
        }
        InstanceNames = normalized;

        if (ActiveTabIndex < 0 || ActiveTabIndex >= 4)
            ActiveTabIndex = 0;
    }

    /// <summary>
    /// Resolve a URL final unindo a origem de TargetUrl com UrlExtension.
    /// Caso a extensão aponte para /hunt, converte para /app mantendo os parâmetros e sinaliza huntRouteRequested.
    /// </summary>
    public string GetResolvedTargetUrl(out bool huntRouteRequested)
    {
        Normalize();
        string baseOrigin = "https://pokeidle.io";
        if (Uri.TryCreate(TargetUrl, UriKind.Absolute, out var baseUri))
            baseOrigin = baseUri.GetLeftPart(UriPartial.Authority);

        string ext = UrlExtension;
        string combined;
        if (ext.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            ext.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            combined = ext;
        }
        else if (ext.StartsWith('?'))
        {
            combined = $"{baseOrigin}/app{ext}";
        }
        else
        {
            if (!ext.StartsWith('/'))
                ext = "/" + ext;
            combined = baseOrigin + ext;
        }

        if (TryRewriteHuntUrl(combined, out var rewritten))
        {
            huntRouteRequested = true;
            return rewritten;
        }

        huntRouteRequested = false;
        return combined;
    }

    /// <summary>
    /// Reescreve URLs com caminho /hunt (ex: https://pokeidle.io/hunt?hunt=x) para /app?hunt=x.
    /// </summary>
    public static bool TryRewriteHuntUrl(string url, out string rewrittenUrl)
    {
        rewrittenUrl = url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (!uri.Host.EndsWith("pokeidle.io", StringComparison.OrdinalIgnoreCase))
            return false;

        string path = uri.AbsolutePath.TrimEnd('/');
        if (!string.Equals(path, "/hunt", StringComparison.OrdinalIgnoreCase))
            return false;

        var builder = new UriBuilder(uri) { Path = "/app" };
        rewrittenUrl = builder.Uri.AbsoluteUri;
        return true;
    }

    /// <summary>
    /// Localiza a pasta da extensão "/hunt for PokeIdle" (localmente em Extensions/hunt ou no Chrome/Edge).
    /// </summary>
    public static string? GetHuntExtensionDirectory()
    {
        string localExt = Path.Combine(AppContext.BaseDirectory, "Extensions", "hunt");
        if (File.Exists(Path.Combine(localExt, "manifest.json")))
            return localExt;

        const string chromeStoreId = "mhnplloljgchpfpcpbhmcokmnefodjnh";
        string[] browserExtRoots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "User Data", "Default", "Extensions", chromeStoreId),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Edge", "User Data", "Default", "Extensions", chromeStoreId)
        ];

        foreach (string root in browserExtRoots)
        {
            if (!Directory.Exists(root)) continue;
            var latest = Directory.GetDirectories(root)
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(d => File.Exists(Path.Combine(d, "manifest.json")));
            if (latest is not null)
                return latest;
        }

        return null;
    }

    /// <summary>
    /// Salva a configuração atual em config.json.
    /// </summary>
    public void Save()
    {
        try
        {
            Normalize();
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // Ignora falha silenciosa de escrita (ex: pasta somente leitura)
        }
    }
}
