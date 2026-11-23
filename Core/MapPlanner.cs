using System.IO.Compression;
using GBX.NET;
using GBX.NET.Engines.Game;
using static EmbedItems.Core.PathUtils;

namespace EmbedItems.Core;

/// <summary>
/// Works out what embedding a map would do, without changing anything (this is also the "scan").
///
/// How embedded items have to look, found by in-game tests:
///   - An embedded file only loads files BELOW its own folder in the map. "..\", ancestor levels, absolute paths
///     and "ContentLoaded\" references all fail. A missing .Gbx dependency makes the item disappear; a missing
///     texture only makes it black.
///   - So an item that uses files elsewhere in the Items folder is moved to the Items ROOT (name + postfix),
///     where every file in the Items folder is below it. Its files keep their paths, so items share them.
///   - Dependencies (Texture.Gbx, ...) can't be moved without losing their original path, so one that points
///     above its own folder makes the item impossible to embed.
///   - The item's author must never change: an item whose author was edited after it was built doesn't show in
///     play mode. Placed objects get the item's author instead.
///   - Saving a map in the editor drops the extra files and rewrites the references to "ContentLoaded\Items\...".
///     Embedding again restores them.
/// </summary>
public static class MapPlanner
{
    public static MapPlan Plan(string mapPath, TrackmaniaFolder tm, EmbedOptions options)
    {
        GbxSetup.Init();
        var gbx = Gbx.Parse<CGameCtnChallenge>(mapPath);
        var map = gbx.Node;

        var plan = new MapPlan
        {
            MapPath = mapPath,
            MapName = map.MapName,
            Validated = map.AuthorTime is not null,
            Gbx = gbx,
        };
        var ctx = new Context(tm, ReadZip(map), options);

        // one plan per original item; an original and its embedded copy placed in the same map share it
        var byOriginal = new Dictionary<string, ItemPlan>(StringComparer.OrdinalIgnoreCase);
        var groups = (map.AnchoredObjects ?? [])
            .Where(o => o.ItemModel.Id is { } id && IsItem(id))
            .GroupBy(o => Norm(o.ItemModel.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            // an original whose embedded copy was already planned (or the other way round): don't plan twice
            var item = byOriginal.TryGetValue(group.Key, out var known) ? known : PlanPlaced(group.Key, ctx);
            if (byOriginal.TryGetValue(item.OriginalPath, out var existing))
                item = existing;
            else
            {
                byOriginal[item.OriginalPath] = item;
                plan.Items.Add(item);
            }
            item.PlacedIds.Add(group.Key);
            item.PlacedCount += group.Count();
        }

        // files an older version of an embedded item used, that nothing uses anymore
        var stillNeeded = new HashSet<string>(plan.Items.SelectMany(i => i.NeededFiles).Concat(ctx.UsedByOthers), StringComparer.OrdinalIgnoreCase);
        foreach (var old in plan.Items.SelectMany(i => i.OldFiles))
            if (!stillNeeded.Contains(old) && ctx.Zip.ContainsKey(old))
                plan.StaleFiles.Add(old);

        if (!plan.Validated)
            plan.Warnings.Add("The map is not validated (no author time). Validate it in the editor BEFORE embedding: " +
                              "validating or saving an embedded map in the editor removes the embedded files again.");
        if (map.Blocks?.Any(b => b.Name?.EndsWith(".Block.Gbx", StringComparison.OrdinalIgnoreCase) == true) == true)
            plan.Warnings.Add("The map uses custom blocks. Blocks are not supported yet and are left unchanged.");

        return plan;
    }

    /// <summary>Files of the map's embedded zip, by Items-relative path.</summary>
    static Dictionary<string, byte[]> ReadZip(CGameCtnChallenge map)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (map.EmbeddedZipData is not { Length: > 0 } data) return files;

        using var zip = new ZipArchive(new MemoryStream(data));
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/') || ZipKey(entry.FullName) is not { } key) continue;
            using var s = entry.Open();
            var ms = new MemoryStream();
            s.CopyTo(ms);
            files.TryAdd(key, ms.ToArray());
        }
        return files;
    }

    sealed class Context(TrackmaniaFolder tm, Dictionary<string, byte[]> zip, EmbedOptions options)
    {
        public TrackmaniaFolder Tm { get; } = tm;
        public Dictionary<string, byte[]> Zip { get; } = zip;
        public EmbedOptions Options { get; } = options;

        /// <summary>Files referenced by items that are embedded by someone else (never removed).</summary>
        public HashSet<string> UsedByOthers { get; } = new(StringComparer.OrdinalIgnoreCase);

        readonly Dictionary<string, byte[]?> diskCache = new(StringComparer.OrdinalIgnoreCase);

        public byte[]? Disk(string rel)
        {
            if (!diskCache.TryGetValue(rel, out var bytes))
            {
                var path = Tm.DiskPath(rel);
                diskCache[rel] = bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            return bytes;
        }

        public byte[]? FromZip(string rel) => Zip.GetValueOrDefault(rel);
    }

    static ItemPlan PlanPlaced(string placedId, Context ctx)
    {
        if (ctx.FromZip(placedId) is { } zipBytes)
        {
            if (ReadMarker(zipBytes) is { } marker)
                return PlanOurs(placedId, zipBytes, marker, ctx);

            // Embedded by the game or another tool. The game embeds items that use files without those files
            // (it only stores "ContentLoaded\..." references), so such an item is broken for other players.
            // If the original is still in the Items folder, embed it properly instead.
            var refs = TryReadRefs(zipBytes);
            if (refs.Count > 0 && ctx.Disk(placedId) is not null)
            {
                var fixedPlan = PlanFromDisk(placedId, ctx, ours: null);
                if (fixedPlan.State == ItemState.WillEmbed)
                {
                    fixedPlan.OldZipKey = placedId;
                    fixedPlan.Notes.Add("It was embedded by the game without its files (broken for other players); it is embedded again properly.");
                }
                return fixedPlan;
            }

            foreach (var r in refs)
                if (Resolve(placedId, r.AncestorLevel, r.FilePath, ctx.Tm).Target is { } t)
                    ctx.UsedByOthers.Add(t);
            return Simple(placedId, ItemState.EmbeddedByOthers, "Already embedded by the game or another tool. Left unchanged.");
        }

        if (ctx.Disk(placedId) is not null)
        {
            // The original placed again in a map that already has its embedded copy (e.g. the embedded map was
            // edited and the item placed from the item browser): treat it like that copy.
            var name = EmbeddedName(placedId);
            if (ctx.FromZip(name) is { } copy && ReadMarker(copy) is { } m && m.OriginalPath.Equals(placedId, StringComparison.OrdinalIgnoreCase))
                return PlanOurs(name, copy, m, ctx);
            return PlanFromDisk(placedId, ctx, ours: null);
        }

        return Simple(placedId, ItemState.NotFound, "Neither embedded in the map nor found in your Items folder (stock item or missing file). Left unchanged.");
    }

    record Ours(string ZipKey, byte[] Bytes, OriginalPathMarker.Info Marker);

    /// <summary>An item EmbedItems embedded before (found by the marker in its Description).</summary>
    static ItemPlan PlanOurs(string zipKey, byte[] zipBytes, OriginalPathMarker.Info marker, Context ctx)
    {
        var ours = new Ours(zipKey, zipBytes, marker);
        bool onDisk = ctx.Disk(marker.OriginalPath) is not null;

        if (ctx.Options.OverwriteFromDisk && onDisk)
        {
            var fromDisk = PlanFromDisk(marker.OriginalPath, ctx, ours);
            if (fromDisk.State != ItemState.CannotEmbed)
                return fromDisk;

            var kept = PlanRestore(ours, ctx);
            kept.Notes.Insert(0, $"The version in your Items folder can't be embedded ({fromDisk.Summary}). The embedded version is kept.");
            return kept;
        }

        var restore = PlanRestore(ours, ctx);
        if (!onDisk)
            restore.Notes.Insert(0, $"The original (Items\\{ToRef(marker.OriginalPath)}) is not in your Items folder; the embedded version is kept.");
        return restore;
    }

    /// <summary>Embed (or re-embed) the item from the Items folder, moved to the Items root.</summary>
    static ItemPlan PlanFromDisk(string originalPath, Context ctx, Ours? ours)
    {
        var bytes = ctx.Disk(originalPath)!;
        var plan = new ItemPlan { OriginalPath = originalPath, EmbeddedName = EmbeddedName(originalPath) };
        if (ours is not null) CollectOldFiles(ours, ctx, plan);

        List<GbxFileUtils.Ref> refs;
        try { refs = GbxFileUtils.ReadRefs(bytes); }
        catch (Exception ex) { return Fail(plan, $"Can't read the item file: {ex.Message}"); }

        if (refs.Count == 0 && ours is null)
        {
            plan.State = ItemState.NoReferences;
            plan.Summary = "Uses no extra files; the game embeds it by itself when you save the map. Left unchanged.";
            return plan;
        }

        // every reference, relative to where the item is on disk
        var targets = new List<string>();
        foreach (var r in refs)
        {
            if (Resolve(originalPath, r.AncestorLevel, r.FilePath, ctx.Tm).Target is not { } t)
                return Fail(plan, $"It uses a file outside the Items folder ({r.FilePath}).");
            targets.Add(t);
        }

        var rewritten = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!CollectFiles(targets, preferZip: false, ctx, plan, rewritten))
            return plan;

        string diskHash = GbxFileUtils.Hash(bytes);
        if (ours is not null && IsUpToDate(ours, diskHash, plan, rewritten, ctx))
        {
            plan.EmbeddedName = ours.ZipKey;
            plan.Ident = GbxFileUtils.ReadIdent(ours.Bytes);
            plan.FilesToWrite.Clear();
            plan.State = ItemState.UpToDate;
            plan.Summary = $"Embedded as {ours.ZipKey}, up to date.";
            return plan;
        }

        try
        {
            var (item, ident) = GbxFileUtils.SetDescription(bytes, d => OriginalPathMarker.Apply(d, originalPath, diskHash));
            // the item now sits in the Items root, so each reference is simply the file's Items-relative path
            plan.ItemBytes = targets.Count > 0 ? GbxFileUtils.RewriteRefs(item, targets.Select(ToRef).ToList()) : item;
            plan.Ident = ident;
        }
        catch (Exception ex) { return Fail(plan, $"Can't rewrite the item file: {ex.Message}"); }

        if (ours is not null && !ours.ZipKey.Equals(plan.EmbeddedName, StringComparison.OrdinalIgnoreCase))
            plan.OldZipKey = ours.ZipKey;

        NoteRewritten(plan, rewritten);
        plan.State = ours is null ? ItemState.WillEmbed : ItemState.WillUpdate;
        plan.Summary = ours is null
            ? $"Will be embedded as {plan.EmbeddedName} together with {plan.FileCount} file(s)."
            : $"The version in your Items folder changed; the embedded copy {plan.EmbeddedName} will be updated.";
        return plan;
    }

    /// <summary>Keep the embedded item, but put back missing files and fix references the editor rewrote.</summary>
    static ItemPlan PlanRestore(Ours ours, Context ctx)
    {
        var plan = new ItemPlan { OriginalPath = ours.Marker.OriginalPath, EmbeddedName = ours.ZipKey };
        CollectOldFiles(ours, ctx, plan);

        List<GbxFileUtils.Ref> refs;
        try
        {
            refs = GbxFileUtils.ReadRefs(ours.Bytes);
            plan.Ident = GbxFileUtils.ReadIdent(ours.Bytes);
        }
        catch (Exception ex) { return Fail(plan, $"Can't read the embedded item: {ex.Message}"); }

        var targets = new List<string>();
        bool refsOk = true;
        foreach (var r in refs)
        {
            var (t, canonical) = Resolve(ours.ZipKey, r.AncestorLevel, r.FilePath, ctx.Tm);
            if (t is null) return Fail(plan, $"The embedded item uses a file outside the Items folder ({r.FilePath}).");
            targets.Add(t);
            refsOk &= canonical;
        }

        var rewritten = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!CollectFiles(targets, preferZip: true, ctx, plan, rewritten))
            return plan;

        if (!refsOk)
        {
            try { plan.ItemBytes = GbxFileUtils.RewriteRefs(ours.Bytes, targets.Select(ToRef).ToList()); }
            catch (Exception ex) { return Fail(plan, $"Can't fix the references of the embedded item: {ex.Message}"); }
        }

        if (plan.ItemBytes is null && plan.FilesToWrite.Count == 0)
        {
            plan.State = ItemState.UpToDate;
            plan.Summary = $"Embedded as {ours.ZipKey}, complete.";
        }
        else
        {
            NoteRewritten(plan, rewritten);
            plan.State = ItemState.WillRestoreFiles;
            plan.Summary = $"Embedded as {ours.ZipKey}, but {plan.FilesToWrite.Count} file(s) are missing from the map " +
                           "(it was probably saved in the editor). They will be added again from your Items folder.";
        }
        return plan;
    }

    /// <summary>
    /// Collects every file the item needs, recursively, into plan.NeededFiles / plan.FilesToWrite.
    /// Returns false (plan marked CannotEmbed) when a dependency can't work embedded.
    /// </summary>
    static bool CollectFiles(List<string> start, bool preferZip, Context ctx, ItemPlan plan, HashSet<string> rewritten)
    {
        var queue = new Queue<string>(start);
        while (queue.Count > 0)
        {
            var path = queue.Dequeue();
            if (!plan.NeededFiles.Add(path)) continue;

            var inZip = ctx.FromZip(path);
            var bytes = preferZip ? inZip ?? ctx.Disk(path) : ctx.Disk(path) ?? inZip;
            if (bytes is null)
            {
                if (IsGbx(path))
                {
                    Fail(plan, $"It needs Items\\{ToRef(path)}, which is neither in the map nor in your Items folder. The game would drop the item.");
                    return false;
                }
                plan.Notes.Add($"Items\\{ToRef(path)} is missing (neither in the map nor in your Items folder); that texture will show black.");
                continue;
            }

            if (IsGbx(path))
            {
                List<GbxFileUtils.Ref> refs;
                try { refs = GbxFileUtils.ReadRefs(bytes); }
                catch (Exception ex)
                {
                    plan.Notes.Add($"Can't read the references of Items\\{ToRef(path)} ({ex.Message}); embedded as it is.");
                    refs = [];
                }

                var folder = Folder(path);
                var newPaths = new List<string>();
                bool rewrite = false;
                foreach (var r in refs)
                {
                    var (t, canonical) = Resolve(path, r.AncestorLevel, r.FilePath, ctx.Tm);
                    if (t is null)
                    {
                        Fail(plan, $"Items\\{ToRef(path)} uses a file outside the Items folder ({r.FilePath}).");
                        return false;
                    }
                    if (!IsBelow(t, folder))
                    {
                        Fail(plan, $"Items\\{ToRef(path)} references {r.FilePath}, which is above its own folder. In a map the game only " +
                                   "loads files below each file's folder, so this item can't be embedded. Fix that reference " +
                                   "(e.g. keep the file it uses next to it or in a subfolder).");
                        return false;
                    }
                    rewrite |= !canonical;
                    newPaths.Add(RelativeTo(folder, t));
                    queue.Enqueue(t);
                }

                // e.g. an absolute "H:\...\Items\Tex\Image\x.dds": same file, written as "Image\x.dds"
                if (rewrite)
                {
                    bytes = GbxFileUtils.RewriteRefs(bytes, newPaths);
                    rewritten.Add(path);
                }
            }

            plan.FileCount++;
            bool unchangedInMap = inZip is not null && !rewritten.Contains(path) && bytes.AsSpan().SequenceEqual(inZip);
            if (!unchangedInMap)
                plan.FilesToWrite[path] = bytes;
        }
        return true;
    }

    /// <summary>
    /// The embedded copy matches the disk version: same disk file (hash in the marker), references not touched
    /// by the editor, and every file present. Rewritten dependencies can't be compared byte by byte (GBX.NET's
    /// output contains a timestamp), so for those being present counts.
    /// </summary>
    static bool IsUpToDate(Ours ours, string diskHash, ItemPlan plan, HashSet<string> rewritten, Context ctx)
    {
        if (ours.Marker.Hash != diskHash) return false;
        try
        {
            foreach (var r in GbxFileUtils.ReadRefs(ours.Bytes))
                if (!Resolve(ours.ZipKey, r.AncestorLevel, r.FilePath, ctx.Tm).Canonical) return false;
        }
        catch { return false; }
        return plan.FilesToWrite.Keys.All(f => rewritten.Contains(f) && ctx.Zip.ContainsKey(f));
    }

    /// <summary>Files the currently embedded version uses (to find files that become unused).</summary>
    static void CollectOldFiles(Ours ours, Context ctx, ItemPlan plan)
    {
        var queue = new Queue<(string Owner, byte[] Bytes)>();
        queue.Enqueue((ours.ZipKey, ours.Bytes));
        while (queue.Count > 0)
        {
            var (owner, bytes) = queue.Dequeue();
            List<GbxFileUtils.Ref> refs;
            try { refs = GbxFileUtils.ReadRefs(bytes); }
            catch { continue; }
            foreach (var r in refs)
            {
                if (Resolve(owner, r.AncestorLevel, r.FilePath, ctx.Tm).Target is not { } t || !plan.OldFiles.Add(t)) continue;
                if (IsGbx(t) && ctx.FromZip(t) is { } dep) queue.Enqueue((t, dep));
            }
        }
    }

    static void NoteRewritten(ItemPlan plan, HashSet<string> rewritten)
    {
        foreach (var path in rewritten.Where(plan.FilesToWrite.ContainsKey))
            plan.Notes.Add($"Items\\{ToRef(path)} used absolute or indirect paths; in the map they become plain relative paths.");
    }

    static OriginalPathMarker.Info? ReadMarker(byte[] item)
    {
        try { return OriginalPathMarker.Read(GbxFileUtils.ReadDescription(item)); }
        catch { return null; }   // not readable as an item: treat it as someone else's
    }

    static List<GbxFileUtils.Ref> TryReadRefs(byte[] bytes)
    {
        try { return GbxFileUtils.ReadRefs(bytes); }
        catch { return []; }
    }

    /// <summary>"Testing/Trees/Tree.Item.Gbx" -> "Tree_3fa2c1d4.Item.Gbx" (postfix from the original path).</summary>
    public static string EmbeddedName(string originalPath)
    {
        var name = FileName(originalPath);
        var stem = name[..^".Item.Gbx".Length];
        return $"{stem}_{GbxFileUtils.PathHash(originalPath)}.Item.Gbx";
    }

    static ItemPlan Simple(string path, ItemState state, string summary) =>
        new() { OriginalPath = path, State = state, Summary = summary };

    static ItemPlan Fail(ItemPlan plan, string reason)
    {
        plan.State = ItemState.CannotEmbed;
        plan.Summary = reason;
        plan.FilesToWrite.Clear();
        plan.ItemBytes = null;
        return plan;
    }
}
