namespace EmbedItems.Core;

/// <summary>The Trackmania user folder: the one that contains Items\ and Maps\.</summary>
public sealed class TrackmaniaFolder
{
    public string Root { get; }
    public string ItemsPath { get; }
    public string MapsPath => Path.Combine(Root, "Maps");

    // The Items folder as written in absolute references. Documents\Trackmania is often a symlink to another
    // drive, and exports then contain the link target (H:\...\Trackmania\Items\...), so both spellings count.
    readonly List<string> itemsSpellings = [];

    public TrackmaniaFolder(string root)
    {
        Root = Path.GetFullPath(root);
        ItemsPath = Path.Combine(Root, "Items");
        AddSpelling(ItemsPath);
        AddSpelling(Path.Combine(ResolveLinks(Root), "Items"));
        AddSpelling(ResolveLinks(ItemsPath));
    }

    void AddSpelling(string path)
    {
        var p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!itemsSpellings.Contains(p, StringComparer.OrdinalIgnoreCase)) itemsSpellings.Add(p);
    }

    static string ResolveLinks(string dir)
    {
        try { return new DirectoryInfo(dir).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? dir; }
        catch (IOException) { return dir; }
    }

    public string DiskPath(string relPath) => Path.Combine(ItemsPath, relPath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Items-relative path of an absolute disk path, or null if it is not inside the Items folder.</summary>
    public string? ItemsRelative(string absolutePath)
    {
        string full;
        try { full = Path.GetFullPath(absolutePath); }
        catch (ArgumentException) { return null; }
        foreach (var items in itemsSpellings)
            if (full.StartsWith(items + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return PathUtils.Norm(full[(items.Length + 1)..]);
        return null;
    }

    /// <summary>
    /// Finds the Trackmania folder: first upwards from a map (maps saved by the game live below it),
    /// then the default locations. The game uses Documents\Trackmania2020 when Documents\Trackmania already
    /// belongs to an older Trackmania, so that one is checked first. MyDocuments follows OneDrive redirection.
    /// </summary>
    public static TrackmaniaFolder? Find(string? nearPath = null)
    {
        if (nearPath is not null)
            for (var dir = Path.GetDirectoryName(Path.GetFullPath(nearPath)); dir is not null; dir = Path.GetDirectoryName(dir))
                if (Directory.Exists(Path.Combine(dir, "Items")))
                    return new TrackmaniaFolder(dir);

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var name in new[] { "Trackmania2020", "Trackmania" })
        {
            var dir = Path.Combine(docs, name);
            if (Directory.Exists(Path.Combine(dir, "Items")))
                return new TrackmaniaFolder(dir);
        }
        return null;
    }
}
