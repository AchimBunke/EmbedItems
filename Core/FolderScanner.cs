namespace EmbedItems.Core;

[Flags]
public enum ScanFilter
{
    None = 0,
    /// <summary>Maps with items that can be embedded but aren't yet.</summary>
    NotEmbedded = 1,
    /// <summary>Maps whose embedded items lost files (saved in the editor).</summary>
    MissingFiles = 2,
    /// <summary>Maps whose embedded items are older than the version in the Items folder.</summary>
    Outdated = 4,
    All = NotEmbedded | MissingFiles | Outdated,
}

/// <summary>One map found by a folder scan.</summary>
public sealed record MapScanEntry(string MapPath, string MapName, string Summary, bool NeedsWork, string? Error);

public static class FolderScanner
{
    /// <summary>
    /// Scans every map below <paramref name="folder"/> and returns the ones matching <paramref name="filter"/>
    /// (plus maps that couldn't be read). Maps are scanned in parallel; each scan only reads files.
    /// </summary>
    public static List<MapScanEntry> Scan(string folder, TrackmaniaFolder tm, EmbedOptions options, ScanFilter filter,
        Action<string> log, CancellationToken cancel = default)
    {
        GbxSetup.Init();   // once, before the parallel scans
        var maps = Directory.EnumerateFiles(folder, "*.Map.Gbx", SearchOption.AllDirectories).ToList();
        log($"Scanning {maps.Count} map(s) in {folder} ...");

        var found = new System.Collections.Concurrent.ConcurrentBag<MapScanEntry>();
        int checkedCount = 0;
        int step = Math.Max(10, maps.Count / 20);   // about 20 progress lines, at most one per 10 maps
        // Load-balanced: each thread takes the next map when it's free. The default for a list hands every
        // thread a fixed block up front, so one block of big maps kept a single thread busy long after the rest.
        var partitioner = System.Collections.Concurrent.Partitioner.Create(maps, loadBalance: true);
        Parallel.ForEach(partitioner, new ParallelOptions { CancellationToken = cancel }, path =>
        {
            try
            {
                var plan = MapPlanner.Plan(path, tm, options);
                bool match = (filter.HasFlag(ScanFilter.NotEmbedded) && plan.HasNotEmbedded)
                          || (filter.HasFlag(ScanFilter.MissingFiles) && plan.HasMissingFiles)
                          || (filter.HasFlag(ScanFilter.Outdated) && plan.HasOutdated);
                if (match) found.Add(new MapScanEntry(path, plan.MapName, PlanText.Short(plan), plan.NeedsWork, null));
            }
            catch (Exception ex)
            {
                found.Add(new MapScanEntry(path, Path.GetFileName(path), "can't be read", false, ex.Message));
            }
            int done = Interlocked.Increment(ref checkedCount);
            if (done % step == 0 && done < maps.Count) log($"  checked {done}/{maps.Count} maps, {found.Count(m => m.Error is null)} match so far ...");
        });

        var result = found.OrderBy(m => m.MapPath, StringComparer.OrdinalIgnoreCase).ToList();
        log($"Checked {maps.Count} map(s), {result.Count(m => m.Error is null)} match.");
        foreach (var m in result.Where(m => m.Error is not null)) log($"  ! {m.MapPath}: {m.Error}");
        return result;
    }
}
