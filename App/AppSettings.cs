using System.Text.Json;

namespace EmbedItems.App;

/// <summary>
/// The few things the app remembers between starts, in %AppData%\EmbedItems\settings.json.
/// Reading or writing failures are ignored: the app then just asks / checks again.
/// </summary>
sealed class AppSettings
{
    /// <summary>Check GitHub for a newer version at start. Null = not asked yet (asked once at the first start).</summary>
    public bool? CheckForUpdates { get; set; }

    /// <summary>Last automatic check; checks run at most once a day.</summary>
    public DateTime? LastUpdateCheck { get; set; }

    /// <summary>Latest version found by that check (shown again until the next check).</summary>
    public string? LatestVersion { get; set; }

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EmbedItems", "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception) { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { }
    }
}
