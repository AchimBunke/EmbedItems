using System.IO.Compression;
using GBX.NET;
using GBX.NET.Components;
using GBX.NET.Engines.Game;
using GBX.NET.LZO;

Gbx.LZO = new Lzo();

var flags = args.Where(a => a.StartsWith("--")).ToHashSet(StringComparer.OrdinalIgnoreCase);
var pos = args.Where(a => !a.StartsWith("--")).ToList();

bool watch = flags.Contains("--watch");
bool dryRun = flags.Contains("--dry-run");
bool verbose = flags.Contains("--verbose") || dryRun;
bool standardAnc = flags.Contains("--standard-ancestors");

if (args.Contains("-h") || flags.Contains("--help"))
{
    PrintHelp();
    return;
}

// No arguments = double-clicked, a single map = dropped onto the exe. Both come from Explorer,
// so keep the console window open at the end, otherwise it closes before anything can be read.
bool fromExplorer = args.Length == 0 || (args.Length == 1 && IsMap(args[0]));

// Accepted forms:
//   (nothing)                                  auto-detect folder, watch its Maps folder
//   <map>                                      auto-detect folder, embed once
//   <TrackmaniaFolder> [--watch] [mapsFolder]  watch
//   <TrackmaniaFolder> <map>                   embed once
string? userRootArg = null, mapToEmbed = null, mapsFolder = null;
if (pos.Count == 1 && IsMap(pos[0])) mapToEmbed = pos[0];
else if (pos.Count >= 1) userRootArg = pos[0];
if (pos.Count >= 2)
{
    if (watch || !IsMap(pos[1])) mapsFolder = pos[1];
    else mapToEmbed = pos[1];
}

var userRoot = userRootArg is not null ? Path.GetFullPath(userRootArg) : FindTrackmaniaFolder(mapToEmbed);
if (userRoot is null)
{
    Console.WriteLine("Could not find your Trackmania folder (the one that contains Items\\).");
    Console.WriteLine("Pass it as the first argument, e.g.  EmbedItems \"D:\\Documents\\Trackmania\"");
    Pause();
    return;
}
var itemsRoot = Path.Combine(userRoot, "Items");
if (!Directory.Exists(itemsRoot)) { Console.WriteLine($"No Items folder in {userRoot}"); Pause(); return; }
Console.WriteLine($"Trackmania folder: {userRoot}");

if (mapToEmbed is not null)
{
    try { Embed(Path.GetFullPath(mapToEmbed)); }
    catch (Exception ex) { Console.WriteLine($"{Path.GetFileName(mapToEmbed)}: {ex.Message}"); }
}
else
{
    Watch(Path.GetFullPath(mapsFolder ?? Path.Combine(userRoot, "Maps")));
}

Pause();
return;

// ---------------------------------------------------------------------------

void Embed(string mapPath)
{
    var gbx = Gbx.Parse<CGameCtnChallenge>(mapPath);
    var map = gbx.Node;

    // 1. custom items and blocks placed in the map (paths relative to the Items folder)
    var roots = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

    if (map.AnchoredObjects is { } objects)
        foreach (var o in objects)
            if (!string.IsNullOrEmpty(o.ItemModel.Id))
                roots.Add("Items/" + Norm(o.ItemModel.Id));

    if (map.Blocks is { } blocks)
        foreach (var b in blocks)
            if (b.Name is { } n && n.EndsWith(".Block.Gbx", StringComparison.OrdinalIgnoreCase))
                roots.Add("Blocks/" + Norm(n));

    var added = new List<string>();
    var addedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);   // references not found
    var outside = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    var notFoundRoots = new List<string>();   // placed, but no file under Items (stock item?)
    var completeItems = new List<string>();   // placed, everything already embedded

    var embeddedRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    map.UpdateEmbeddedZipData(zip =>
    {
        // what the map already contains is the baseline (case-insensitive, duplicates collapsed)
        var existing = zip.Entries
            .GroupBy(e => Norm(e.FullName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        bool InZip(string p) => existing.ContainsKey(p) || addedSet.Contains(p);
        bool Exists(string p) => InZip(p) || File.Exists(DiskPath(p));
        void Add(string p, string? entryPath = null)
        {
            entryPath ??= p;

            if (!dryRun)
                zip.CreateEntryFromFile(
                    DiskPath(p),
                    entryPath,
                    CompressionLevel.Optimal);

            addedSet.Add(entryPath);
            added.Add(entryPath);
        }

        var todo = new Queue<(string path, bool root)>(roots.Select(r => (r, true)));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (todo.Count > 0)
        {
            var (owner, isRoot) = todo.Dequeue();
            if (!seen.Add(owner)) continue;

            var bytes = ReadBytes(existing, addedSet, owner);
            if (bytes is null)
            {
                if (isRoot) notFoundRoots.Add(owner);
                continue;
            }

            GbxRefTable? rt = null;
            try { rt = Gbx.Parse(new MemoryStream(bytes), new GbxReadSettings { ReadRawBody = true }).RefTable; }
            catch (Exception ex) { Console.WriteLine($"  cannot read {owner}: {ex.Message}"); }

            // resolve every reference of this file
            var targets = new List<string>();
            if (rt is not null)
            {
                foreach (var f in rt.Files)
                {
                    var target = Resolve(owner, rt.AncestorLevel, f.FilePath);
                    if (target is null)
                    {
                        outside.Add($"{f.FilePath}  (from {owner}, leaves the Items folder)");
                        continue;
                    }
                    if (verbose)
                        Console.WriteLine($"  {owner}  level={rt.AncestorLevel}  {f.FilePath}\n      -> {target}  [{(InZip(target) ? "already embedded" : File.Exists(DiskPath(target)) ? "found on disk" : "not found")}]");

                    if (Exists(target)) targets.Add(target);
                    else missing.Add(target);          // stock game file, or genuinely missing
                }
            }

            if (isRoot)
            {
                // placed item/block: only embed it when it has external files
                int addedBefore = added.Count;

                if (!InZip(owner))
                {
                    string embeddedOwner = RenameRoot(owner);

                    if (!InZip(embeddedOwner))
                    {
                        embeddedRoots[owner] = embeddedOwner;
                        Add(owner, embeddedOwner);
                    }
                }

                foreach (var t in targets)
                    Handle(t);

                if (added.Count == addedBefore)
                    completeItems.Add(owner);
            }
            else
            {
                foreach (var t in targets) Handle(t);  // dependencies are always embedded
            }

            void Handle(string t)
            {
                if (!InZip(t)) Add(t);
                if (t.EndsWith(".gbx", StringComparison.OrdinalIgnoreCase))
                    todo.Enqueue((t, false));          // wrappers have their own references
            }
        }
    });

    int updatedIds = 0;

    if (!dryRun)
    {
        if (map.AnchoredObjects is { } anchoredObjects)
        {
            foreach (var o in anchoredObjects)
            {
                if (string.IsNullOrEmpty(o.ItemModel.Id))
                    continue;

                string key = "Items/" + Norm(o.ItemModel.Id);

                if (!embeddedRoots.TryGetValue(key, out var newPath))
                    continue;

                newPath = newPath["Items/".Length..];

                o.ItemModel = o.ItemModel with
                {
                    Id = o.ItemModel.Id.Contains('\\')
                        ? newPath.Replace('/', '\\')
                        : newPath
                };

                updatedIds++;
            }
        }

        if (map.Blocks is { } anchoredBlocks)
        {
            foreach (var b in anchoredBlocks)
            {
                if (string.IsNullOrEmpty(b.Name))
                    continue;

                string key = "Blocks/" + Norm(b.Name);

                if (!embeddedRoots.TryGetValue(key, out var newPath))
                    continue;

                newPath = newPath["Blocks/".Length..];

                b.Name = b.Name.Contains('\\')
                    ? newPath.Replace('/', '\\')
                    : newPath;

                updatedIds++;
            }
        }
    }

    foreach (var o in outside) Console.WriteLine($"  outside Items: {o}");
    foreach (var m in missing) Console.WriteLine($"  not found under Items (stock game file?): {m}");

    Console.WriteLine($"{Path.GetFileName(mapPath)}: placed custom models={roots.Count}  " +
                      $"not under Items={notFoundRoots.Count}  " +
                      $"already complete={completeItems.Count}  to add={added.Count}");
    if (verbose)
    {
        foreach (var s in notFoundRoots) Console.WriteLine("  not under Items  " + s);
        foreach (var c in completeItems) Console.WriteLine("  complete         " + c);
    }

    if (added.Count == 0 && updatedIds == 0) return;           // nothing new: do not rewrite the map

    foreach (var a in added) Console.WriteLine("  + " + a);
    if (dryRun) { Console.WriteLine("(dry run, map not modified)"); return; }

    // Case-insensitive on purpose: a plain Replace(".Map.Gbx", ...) leaves "x.map.gbx" unchanged
    // and the save would overwrite the original map.
    const string suffix = " (embedded)";
    var fileName = Path.GetFileName(mapPath);
    var stem = fileName[..^".Map.Gbx".Length];
    if (!stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) stem += suffix;   // input is already our output: update it
    var outPath = Path.Combine(Path.GetDirectoryName(mapPath)!, stem + ".Map.Gbx");

    if (!map.MapName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        map.MapName += suffix;

    gbx.Save(outPath);
    Console.WriteLine($"{Path.GetFileName(outPath)}: embedded {added.Count} file(s)");
}

string DiskPath(string zipPath) => Path.Combine(userRoot, zipPath.Replace('/', Path.DirectorySeparatorChar));

byte[]? ReadBytes(Dictionary<string, ZipArchiveEntry> existing, HashSet<string> addedSet, string path)
{
    if (!addedSet.Contains(path) && existing.TryGetValue(path, out var e))
    {
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
    var disk = DiskPath(path);
    return File.Exists(disk) ? File.ReadAllBytes(disk) : null;
}

// Resolves a reference to a path relative to the Items folder, or null if it would leave it.
// Default rule: AncestorLevel 1 = the owner's own folder. --standard-ancestors: level N = N folders up.
string? Resolve(string ownerZipPath, int ancestorLevel, string refPath)
{
    var parts = Norm(ownerZipPath).Split('/').SkipLast(1).ToList();
    int ups = standardAnc ? ancestorLevel : Math.Max(0, ancestorLevel - 1);

    for (int i = 0; i < ups; i++)
    {
        if (parts.Count == 0) return null;
        parts.RemoveAt(parts.Count - 1);
    }
    foreach (var seg in Norm(refPath).Split('/', StringSplitOptions.RemoveEmptyEntries))
    {
        if (seg == "..") { if (parts.Count == 0) return null; parts.RemoveAt(parts.Count - 1); }
        else if (seg != ".") parts.Add(seg);
    }
    return string.Join('/', parts);
}

string Norm(string p) => p.Replace('\\', '/').TrimStart('/');

static bool IsMap(string p) => p.EndsWith(".Map.Gbx", StringComparison.OrdinalIgnoreCase);

// The Trackmania user folder is the one that contains Items\ (and Maps\).
string? FindTrackmaniaFolder(string? mapPath)
{
    // A map saved by the game lives somewhere below that folder (…\Trackmania\Maps\My Maps\x.Map.Gbx).
    if (mapPath is not null)
        for (var dir = Path.GetDirectoryName(Path.GetFullPath(mapPath)); dir is not null; dir = Path.GetDirectoryName(dir))
            if (Directory.Exists(Path.Combine(dir, "Items")))
                return dir;

    // Default location. The game uses Documents\Trackmania2020 when Documents\Trackmania already
    // belongs to an older Trackmania, so check that first. MyDocuments also follows OneDrive redirection.
    var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    foreach (var name in new[] { "Trackmania2020", "Trackmania" })
    {
        var dir = Path.Combine(docs, name);
        if (Directory.Exists(Path.Combine(dir, "Items")))
            return dir;
    }
    return null;
}

void Pause()
{
    if (!fromExplorer || Console.IsInputRedirected) return;
    Console.WriteLine();
    Console.WriteLine("Press any key to exit.");
    Console.ReadKey(true);
}

static void PrintHelp() => Console.WriteLine("""
    EmbedItems                                         watch your Maps folder (same as double-clicking)
    EmbedItems <map.Map.Gbx>                           embed once (same as dropping a map on the exe)
    EmbedItems <TrackmaniaFolder> <map.Map.Gbx>        embed once
    EmbedItems <TrackmaniaFolder> [--watch] [mapsFolder]  embed after every save

    <TrackmaniaFolder>   folder that contains Items\ (default: Documents\Trackmania2020 or Documents\Trackmania)

    Starts from the custom items/blocks PLACED in the map, finds them under Items\ (or Blocks\),
    and embeds each one together with every file it references (meshes, textures, ...).
    The result is written next to the map as "<name> (embedded).Map.Gbx"; the original map is not changed.
    Entries that are already embedded in the map are never overwritten.

    --dry-run              print every resolved reference, change nothing
    --verbose              also list complete / not-found items
    --standard-ancestors   AncestorLevel N = N folders above the owner's folder
                           (default: level 1 = the owner's own folder, level 2 = one up, ...)
    """);

string RenameRoot(string path)
{
    int gbx = path.LastIndexOf(".Gbx", StringComparison.OrdinalIgnoreCase);
    if (gbx < 0)
        return path + " (embedded)";

    int classDot = path.LastIndexOf('.', gbx - 1);
    int insertAt = classDot >= 0 ? classDot : gbx;

    return path[..insertAt] + " (embedded)" + path[insertAt..];
}

void Watch(string mapsDir)
{
    if (!Directory.Exists(mapsDir)) { Console.WriteLine($"Maps folder not found: {mapsDir}"); return; }

    Console.WriteLine($"Watching {mapsDir}  (close this window or press Ctrl+C to stop)");
    Console.WriteLine("Save a map in the editor and an embedded copy is written next to it.");
    var last = new System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>();

    var w = new FileSystemWatcher(mapsDir, "*.Map.Gbx")
    {
        IncludeSubdirectories = true,
        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        EnableRaisingEvents = true
    };
    void OnEvent(object _, FileSystemEventArgs e)
    {
        if (Path.GetFileName(e.FullPath)
            .Contains(" (embedded).Map.Gbx", StringComparison.OrdinalIgnoreCase))
            return;

        last[e.FullPath] = DateTime.UtcNow;
    }
    w.Changed += OnEvent; w.Created += OnEvent; w.Renamed += OnEvent;

    while (true)
    {
        Thread.Sleep(1000);
        foreach (var (path, at) in last.ToArray())
        {
            if (DateTime.UtcNow - at < TimeSpan.FromSeconds(2)) continue;   // still being written
            last.TryRemove(path, out _);

            try { using var _ = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { last[path] = DateTime.UtcNow; continue; } // game still has it open

            try { Embed(path); }
            catch (Exception ex) { Console.WriteLine($"{Path.GetFileName(path)}: {ex.Message}"); }
        }
    }
}