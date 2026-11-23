namespace EmbedItems.Core;

/// <summary>
/// The line EmbedItems adds to an embedded item's Description:
///   [EmbedItems] Items\Testing\Trees\Tree.Item.Gbx #3fa2c1d4e5f60718
/// It records where the item came from (so Extract can put it back and Embed can find it on disk again)
/// and a hash of the disk file it was built from (so a scan can tell whether the disk version changed).
/// The Description survives saving the map in the editor and doesn't affect the item in game.
/// </summary>
public static class OriginalPathMarker
{
    const string Prefix = "[EmbedItems] Items\\";

    public record Info(string OriginalPath, string Hash);

    /// <summary>Description with the marker for <paramref name="originalPath"/> (Items-relative), replacing an old one.</summary>
    public static string Apply(string? description, string originalPath, string hash)
    {
        var rest = Remove(description);
        var line = $"{Prefix}{PathUtils.ToRef(originalPath)} #{hash}";
        return rest.Length == 0 ? line : rest + "\n" + line;
    }

    public static Info? Read(string? description)
    {
        if (description is null) return null;
        foreach (var line in description.Split('\n'))
        {
            var l = line.TrimEnd('\r');
            if (!l.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            var body = l[Prefix.Length..];
            int hash = body.LastIndexOf(" #", StringComparison.Ordinal);
            return hash < 0
                ? new Info(PathUtils.Norm(body), "")
                : new Info(PathUtils.Norm(body[..hash]), body[(hash + 2)..]);
        }
        return null;
    }

    /// <summary>The Description without the marker line (what the item had before embedding).</summary>
    public static string Remove(string? description)
    {
        if (string.IsNullOrEmpty(description)) return "";
        var lines = description.Split('\n').Where(l => !l.TrimEnd('\r').StartsWith(Prefix, StringComparison.Ordinal));
        return string.Join('\n', lines).TrimEnd('\n', '\r');
    }
}
