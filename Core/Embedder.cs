using System.IO.Compression;

namespace EmbedItems.Core;

public static class Embedder
{
    const string Suffix = " (embedded)";

    /// <summary>Plans and embeds one map. Returns the written file, or null if nothing had to change.</summary>
    public static string? EmbedFile(string mapPath, TrackmaniaFolder tm, EmbedOptions options, Action<string> log)
    {
        var plan = MapPlanner.Plan(mapPath, tm, options);
        log($"Embedding {Path.GetFileName(mapPath)} ...");
        foreach (var w in plan.Warnings) log("  ! " + w);
        foreach (var item in plan.Items.Where(i => i.State == ItemState.CannotEmbed))
            log($"  ! {PlanText.ItemName(item)}: {PlanText.ItemText(item)}");
        return Apply(plan, options, log);
    }

    /// <summary>Carries out a plan from MapPlanner. Returns the written file, or null if nothing had to change.</summary>
    public static string? Apply(MapPlan plan, EmbedOptions options, Action<string> log)
    {
        bool rename = !string.IsNullOrWhiteSpace(options.MapName) && options.MapName != plan.MapName;
        if (!plan.NeedsWork && !rename)
        {
            log("  Nothing to do: everything that can be embedded already is.");
            return null;
        }

        var writes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var deletes = new HashSet<string>(plan.StaleFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan.Items.Where(i => i.Changes))
        {
            if (item.ItemBytes is not null) writes[item.EmbeddedName!] = item.ItemBytes;
            foreach (var (path, bytes) in item.FilesToWrite) writes[path] = bytes;
            if (item.OldZipKey is not null) deletes.Add(item.OldZipKey);
        }

        var map = plan.Gbx.Node;
        if (writes.Count > 0 || deletes.Count > 0)
        {
            map.UpdateEmbeddedZipData(zip =>
            {
                foreach (var entry in zip.Entries.ToList())
                    if (PathUtils.ZipKey(entry.FullName) is { } key && (writes.ContainsKey(key) || deletes.Contains(key)))
                        entry.Delete();
                // .dds textures are mostly uncompressed, so the smallest setting pays off for map size
                foreach (var (path, bytes) in writes)
                {
                    using var s = zip.CreateEntry("Items/" + path, CompressionLevel.SmallestSize).Open();
                    s.Write(bytes);
                }
            });
        }

        // Placed objects point to the embedded copy. The author comes from the item itself: the map's list of
        // embedded items (rebuilt by GBX.NET on save) takes it from there, and they have to match.
        var targets = new Dictionary<string, ItemPlan>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan.Items.Where(i => i.IsOurs))
            foreach (var id in item.PlacedIds) targets[id] = item;
        int moved = 0;
        foreach (var o in map.AnchoredObjects ?? [])
        {
            if (o.ItemModel.Id is not { } id || !targets.TryGetValue(PathUtils.Norm(id), out var item) || item.Ident is not { } ident)
                continue;
            o.ItemModel = o.ItemModel with { Id = item.EmbeddedName!, Collection = ident.Collection, Author = ident.Author };
            moved++;
        }

        if (rename) map.MapName = options.MapName!;

        var outPath = options.ReplaceOriginal ? plan.MapPath : OutputPath(plan.MapPath);
        plan.Gbx.Save(outPath);

        foreach (var item in plan.Items.Where(i => i.Changes))
        {
            log($"  {PlanText.ItemName(item)}: {PlanText.Done(item)}");
            foreach (var n in item.Notes) log("      - " + n);
        }
        if (deletes.Count > 0) log($"  Removed {deletes.Count} file(s) no longer used.");
        log($"Saved {Path.GetFileName(outPath)} ({moved} placed object(s) use embedded items).");
        log("  Share this file as it is. Saving it in the editor removes the embedded files again (then just embed it once more).");
        return outPath;
    }

    /// <summary>"Map.Map.Gbx" -> "Map (embedded).Map.Gbx". A map that already is an embedded copy is updated in place.</summary>
    public static string OutputPath(string mapPath)
    {
        var name = Path.GetFileName(mapPath);
        // case-insensitive on purpose: a plain Replace would leave "x.map.gbx" and overwrite the original
        var stem = name.EndsWith(".Map.Gbx", StringComparison.OrdinalIgnoreCase) ? name[..^".Map.Gbx".Length] : Path.GetFileNameWithoutExtension(name);
        if (!stem.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)) stem += Suffix;
        return Path.Combine(Path.GetDirectoryName(mapPath)!, stem + ".Map.Gbx");
    }
}
