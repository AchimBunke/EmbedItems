namespace EmbedItems.Core;

/// <summary>
/// Paths inside the Items folder. Internally every path is "Items-relative" with forward slashes
/// ("Testing/Trees/Tree.Item.Gbx"); Gbx reference tables use backslashes.
/// </summary>
public static class PathUtils
{
    public static string Norm(string path) => path.Replace('\\', '/').Trim('/');

    public static string ToRef(string relPath) => relPath.Replace('/', '\\');

    /// <summary>Folder part of an Items-relative path ("" for a file in the Items root).</summary>
    public static string Folder(string relPath)
    {
        int i = relPath.LastIndexOf('/');
        return i < 0 ? "" : relPath[..i];
    }

    public static string FileName(string relPath) => relPath[(relPath.LastIndexOf('/') + 1)..];

    /// <summary>True if <paramref name="path"/> lies anywhere below <paramref name="folder"/> ("" = Items root).</summary>
    public static bool IsBelow(string path, string folder) =>
        folder.Length == 0 || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Relative path from a folder to a file, e.g. ("A/B", "A/C/x.dds") -> "..\C\x.dds".</summary>
    public static string RelativeTo(string folder, string path)
    {
        var f = folder.Length == 0 ? [] : folder.Split('/');
        var p = path.Split('/');
        int common = 0;
        while (common < f.Length && common < p.Length - 1 && string.Equals(f[common], p[common], StringComparison.OrdinalIgnoreCase))
            common++;
        return string.Join('\\', Enumerable.Repeat("..", f.Length - common).Concat(p.Skip(common)));
    }

    /// <summary>
    /// Items-relative key of a zip entry in a map. The game writes item entries either as "Items/..." or with
    /// the absolute path of the PC that embedded them ("C:/Users/x/Documents/Trackmania/Items/..."), so both are
    /// cut at the Items folder. Returns null for entries outside an Items folder (blocks, other extras).
    /// </summary>
    public static string? ZipKey(string entryFullName)
    {
        var p = Norm(entryFullName);
        if (p.StartsWith("Items/", StringComparison.OrdinalIgnoreCase)) return p["Items/".Length..];
        int i = p.IndexOf("/Items/", StringComparison.OrdinalIgnoreCase);
        return i < 0 ? null : p[(i + "/Items/".Length)..];
    }

    static bool IsRooted(string refPath) =>
        (refPath.Length >= 2 && char.IsAsciiLetter(refPath[0]) && refPath[1] == ':') || refPath.StartsWith(@"\\");

    /// <summary>
    /// Resolves one reference of the file at <paramref name="ownerRel"/> to an Items-relative path.
    /// Handles the forms seen in practice:
    ///   - relative, with ancestor level N = start N folders above the owner's folder, ".." allowed
    ///   - absolute disk paths that point into the Items folder (some exports write those)
    ///   - "ContentLoaded\Items\..." (written by the game when it saves a map with embedded items)
    /// Target is null if the reference leaves the Items folder. Canonical is true if the reference is already
    /// a plain relative path straight down from the owner's folder (level 0, no "..").
    /// </summary>
    public static (string? Target, bool Canonical) Resolve(string ownerRel, int ancestorLevel, string refPath, TrackmaniaFolder tm)
    {
        if (IsRooted(refPath))
            return (tm.ItemsRelative(refPath), false);

        var segments = Norm(refPath).Split('/', StringSplitOptions.RemoveEmptyEntries);

        int cl = Array.FindIndex(segments, s => s.Equals("ContentLoaded", StringComparison.OrdinalIgnoreCase));
        if (cl >= 0 && cl + 1 < segments.Length && segments[cl + 1].Equals("Items", StringComparison.OrdinalIgnoreCase))
            return (string.Join('/', segments.Skip(cl + 2)), false);

        var parts = Folder(Norm(ownerRel)).Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        bool canonical = ancestorLevel == 0;
        for (int i = 0; i < ancestorLevel; i++)
        {
            if (parts.Count == 0) return (null, false);
            parts.RemoveAt(parts.Count - 1);
        }
        foreach (var s in segments)
        {
            if (s == ".") { canonical = false; continue; }
            if (s == "..")
            {
                canonical = false;
                if (parts.Count == 0) return (null, false);
                parts.RemoveAt(parts.Count - 1);
            }
            else parts.Add(s);
        }
        return (string.Join('/', parts), canonical);
    }

    public static bool IsGbx(string path) => path.EndsWith(".gbx", StringComparison.OrdinalIgnoreCase);

    public static bool IsItem(string path) => path.EndsWith(".Item.Gbx", StringComparison.OrdinalIgnoreCase);
}
