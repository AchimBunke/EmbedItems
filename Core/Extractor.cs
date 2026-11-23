using System.IO.Compression;
using GBX.NET;
using GBX.NET.Engines.Game;
using static EmbedItems.Core.PathUtils;

namespace EmbedItems.Core;

public sealed record ExtractOptions
{
    /// <summary>Overwrite files that already exist in the target folder (default: skip them).</summary>
    public bool Overwrite { get; init; }
}

public sealed record ExtractResult(List<string> Written, List<string> Skipped, List<string> Problems);

/// <summary>
/// Writes the items EmbedItems embedded in a map back to disk, at their original place in the Items folder
/// (read from the marker in each item's Description), with their files at the paths they have in the map.
/// The item's references are turned back into paths relative to its original folder ("..\..\Tex\x.dds").
/// </summary>
public static class Extractor
{
    public static ExtractResult Extract(string mapPath, string targetItemsFolder, TrackmaniaFolder tm, ExtractOptions options, Action<string> log)
    {
        GbxSetup.Init();
        var map = Gbx.ParseNode<CGameCtnChallenge>(mapPath);
        var result = new ExtractResult([], [], []);
        if (map.EmbeddedZipData is not { Length: > 0 } data)
        {
            log("The map has no embedded files.");
            return result;
        }

        var zip = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using (var archive = new ZipArchive(new MemoryStream(data)))
            foreach (var e in archive.Entries)
                if (!e.FullName.EndsWith('/') && ZipKey(e.FullName) is { } key)
                {
                    using var s = e.Open();
                    var ms = new MemoryStream();
                    s.CopyTo(ms);
                    zip.TryAdd(key, ms.ToArray());
                }

        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int ours = 0;
        foreach (var (key, bytes) in zip.Where(f => IsItem(f.Key)).ToList())
        {
            OriginalPathMarker.Info? marker;
            try { marker = OriginalPathMarker.Read(GbxFileUtils.ReadDescription(bytes)); }
            catch { marker = null; }
            if (marker is null)
            {
                log($"  Items\\{ToRef(key)}: not embedded by EmbedItems (no original path stored), skipped.");
                continue;
            }
            ours++;
            var original = marker.OriginalPath;
            log($"  {key} -> Items\\{ToRef(original)}");

            try
            {
                var refs = GbxFileUtils.ReadRefs(bytes);
                var targets = new List<string>();
                foreach (var r in refs)
                    if (Resolve(key, r.AncestorLevel, r.FilePath, tm).Target is { } t) targets.Add(t);
                    else throw new InvalidOperationException($"reference outside the Items folder: {r.FilePath}");

                var (item, _) = GbxFileUtils.SetDescription(bytes, OriginalPathMarker.Remove);
                if (targets.Count > 0)
                    item = GbxFileUtils.RewriteRefs(item, targets.Select(t => RelativeTo(Folder(original), t)).ToList());
                Write(original, item);

                // the item's files, as they are in the map
                var queue = new Queue<string>(targets);
                while (queue.Count > 0)
                {
                    var path = queue.Dequeue();
                    if (written.Contains(path) || result.Skipped.Contains(path)) continue;
                    if (!zip.TryGetValue(path, out var dep))
                    {
                        result.Problems.Add($"Items\\{ToRef(path)} (used by {FileName(original)}) is not in the map (probably removed by an editor save).");
                        written.Add(path);
                        continue;
                    }
                    Write(path, dep);
                    if (IsGbx(path))
                        foreach (var r in GbxFileUtils.ReadRefs(dep))
                            if (Resolve(path, r.AncestorLevel, r.FilePath, tm).Target is { } t) queue.Enqueue(t);
                }
            }
            catch (Exception ex) { result.Problems.Add($"{key}: {ex.Message}"); }
        }

        log($"Extracted {ours} item(s): {result.Written.Count} file(s) written, {result.Skipped.Count} skipped because they already exist.");
        foreach (var s in result.Skipped) log("  skipped (exists): Items\\" + ToRef(s));
        foreach (var p in result.Problems) log("  ! " + p);
        return result;

        void Write(string rel, byte[] bytes)
        {
            var path = Path.Combine(targetItemsFolder, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path) && !options.Overwrite)
            {
                result.Skipped.Add(rel);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            written.Add(rel);
            result.Written.Add(rel);
        }
    }
}
