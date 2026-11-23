using EmbedItems.Core;

// Command line front end. Same features as the UI: scan, embed, folder scan, extract, watch.

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintHelp();
    // double-clicked in Explorer: keep the window open so the help can be read
    if (args.Length == 0 && !Console.IsInputRedirected)
    {
        Console.WriteLine("Press any key to exit.");
        Console.ReadKey(true);
    }
    return 0;
}

var command = args[0].ToLowerInvariant();
var positional = new List<string>();
var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
for (int i = 1; i < args.Length; i++)
{
    var a = args[i];
    if (!a.StartsWith("--")) { positional.Add(a); continue; }
    // options with a value
    if (a is "--tm" or "--map-name" or "--target" or "--filter")
    {
        if (i + 1 >= args.Length) return Error($"{a} needs a value.");
        options[a] = args[++i];
    }
    else options[a] = null;
}

var tm = options.TryGetValue("--tm", out var tmArg) ? new TrackmaniaFolder(tmArg!) : TrackmaniaFolder.Find(positional.FirstOrDefault());
if (tm is null || !Directory.Exists(tm.ItemsPath))
    return Error("Could not find your Trackmania folder (the one that contains Items\\). Pass it with --tm \"D:\\Documents\\Trackmania\".");

var embedOptions = new EmbedOptions
{
    OverwriteFromDisk = !options.ContainsKey("--no-overwrite"),
    ReplaceOriginal = options.ContainsKey("--replace"),
    MapName = options.GetValueOrDefault("--map-name"),
};

Console.WriteLine($"Trackmania folder: {tm.Root}");
try
{
    switch (command)
    {
        case "scan":
            foreach (var map in RequireMaps()) foreach (var line in PlanText.Describe(MapPlanner.Plan(map, tm, embedOptions))) Console.WriteLine(line);
            return 0;

        case "embed":
            foreach (var map in RequireMaps()) EmbedOne(map);
            Console.WriteLine("Done.");
            return 0;

        case "scan-folder":
        {
            if (positional.Count != 1 || !Directory.Exists(positional[0])) return Error("scan-folder needs one existing folder.");
            var found = FolderScanner.Scan(positional[0], tm, embedOptions, ParseFilter(options.GetValueOrDefault("--filter")), Console.WriteLine);
            foreach (var m in found.Where(m => m.Error is null)) Console.WriteLine($"  {m.MapPath}  [{m.Summary}]");
            if (options.ContainsKey("--embed"))
                foreach (var m in found.Where(m => m.Error is null && m.NeedsWork)) EmbedOne(m.MapPath);
            Console.WriteLine("Done.");
            return 0;
        }

        case "extract":
        {
            var map = RequireMaps().Single();
            var target = options.GetValueOrDefault("--target") ?? tm.ItemsPath;
            Console.WriteLine($"Extracting into {target}");
            var result = Extractor.Extract(map, target, tm, new ExtractOptions { Overwrite = options.ContainsKey("--overwrite") }, Console.WriteLine);
            return result.Problems.Count == 0 ? 0 : 1;
        }

        case "watch":
        {
            var folder = positional.FirstOrDefault() ?? tm.MapsPath;
            if (!Directory.Exists(folder)) return Error($"Folder not found: {folder}");
            Console.WriteLine($"Watching {folder}. Every map you save is embedded. Press Ctrl+C to stop.");
            MapWatcher? watcher = null;
            watcher = new MapWatcher(folder, path =>
            {
                lock (Console.Out)
                {
                    try { if (Embedder.EmbedFile(path, tm, embedOptions, Console.WriteLine) is { } written) watcher?.Ignore(written); }
                    catch (Exception ex) { Console.WriteLine($"{Path.GetFileName(path)}: {ex.Message}"); }
                }
            });
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }

        default:
            return Error($"Unknown command \"{args[0]}\". Run EmbedItemsCli --help.");
    }
}
catch (ArgumentException ex) { return Error(ex.Message); }

void EmbedOne(string map)
{
    try { Embedder.EmbedFile(map, tm, embedOptions, Console.WriteLine); }
    catch (Exception ex) { Console.WriteLine($"{Path.GetFileName(map)}: {ex.Message}"); }
    Console.WriteLine();
}

List<string> RequireMaps()
{
    if (positional.Count == 0) throw new ArgumentException($"{command} needs at least one map file.");
    foreach (var p in positional)
        if (!File.Exists(p)) throw new ArgumentException($"Map not found: {p}");
    return positional.Select(Path.GetFullPath).ToList();
}

static ScanFilter ParseFilter(string? value)
{
    if (value is null) return ScanFilter.All;
    var filter = ScanFilter.None;
    foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        filter |= part.ToLowerInvariant() switch
        {
            "not-embedded" => ScanFilter.NotEmbedded,
            "missing" => ScanFilter.MissingFiles,
            "outdated" => ScanFilter.Outdated,
            "all" => ScanFilter.All,
            _ => throw new ArgumentException($"Unknown filter \"{part}\" (use not-embedded, missing, outdated, all)."),
        };
    return filter;
}

static int Error(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}

static void PrintHelp() => Console.WriteLine("""
    EmbedItemsCli - embeds custom items and the files they use into Trackmania maps.

    EmbedItemsCli scan <map> [<map> ...]                    show what embedding would do, change nothing
    EmbedItemsCli embed <map> [<map> ...] [embed options]   embed
    EmbedItemsCli scan-folder <folder> [--filter <list>] [--embed] [embed options]
                                                            list maps below a folder that need embedding;
                                                            --embed also embeds them
    EmbedItemsCli extract <map> [--target <ItemsFolder>] [--overwrite]
                                                            write the embedded items back to their original
                                                            place in the Items folder (existing files are skipped)
    EmbedItemsCli watch [<mapsFolder>] [embed options]      embed every map you save (default: your Maps folder)

    Embed options:
      --no-overwrite        keep items embedded earlier as they are (only put back missing files),
                            instead of updating them from your Items folder
      --replace             save over the original map instead of "<name> (embedded).Map.Gbx"
      --map-name <name>     new map name (default: unchanged)

    --filter <list>         comma separated: not-embedded, missing, outdated, all (default: all)
    --tm <folder>           your Trackmania folder (the one containing Items\), if auto-detection fails

    What embedding does: items that use extra files (textures, meshes, ...) are copied into the map's
    Items root as "<name>_<id>.Item.Gbx", together with those files at their usual paths. Their original
    path is stored in the item's description, so they can be updated or extracted later.
    Validate the map BEFORE embedding, and don't save the embedded map in the editor (the game removes
    the extra files again; embed it once more if you did).
    """);
