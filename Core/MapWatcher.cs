using System.Collections.Concurrent;

namespace EmbedItems.Core;

/// <summary>
/// Calls <c>onMapSaved</c> for every map saved below a folder, once the game has finished writing it.
/// Maps the tool writes itself are ignored for a few seconds, so embedding never triggers itself.
/// </summary>
public sealed class MapWatcher : IDisposable
{
    readonly FileSystemWatcher watcher;
    readonly ConcurrentDictionary<string, DateTime> pending = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, DateTime> ignoreUntil = new(StringComparer.OrdinalIgnoreCase);
    readonly Timer timer;
    readonly Action<string> onMapSaved;

    public MapWatcher(string folder, Action<string> onMapSaved)
    {
        this.onMapSaved = onMapSaved;
        watcher = new FileSystemWatcher(folder, "*.Map.Gbx")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        watcher.Changed += OnEvent;
        watcher.Created += OnEvent;
        watcher.Renamed += OnEvent;
        watcher.EnableRaisingEvents = true;
        timer = new Timer(_ => Tick(), null, 1000, 1000);
    }

    /// <summary>Ignore changes to a file the tool just wrote.</summary>
    public void Ignore(string path) => ignoreUntil[Path.GetFullPath(path)] = DateTime.UtcNow.AddSeconds(5);

    void OnEvent(object? sender, FileSystemEventArgs e)
    {
        var path = Path.GetFullPath(e.FullPath);
        if (ignoreUntil.TryGetValue(path, out var until) && DateTime.UtcNow < until) return;
        pending[path] = DateTime.UtcNow;
    }

    void Tick()
    {
        foreach (var (path, at) in pending.ToArray())
        {
            if (DateTime.UtcNow - at < TimeSpan.FromSeconds(2)) continue;   // still being written
            try { using var _ = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { pending[path] = DateTime.UtcNow; continue; }   // the game still has it open
            catch (UnauthorizedAccessException) { pending.TryRemove(path, out _); continue; }
            if (pending.TryRemove(path, out _)) onMapSaved(path);
        }
    }

    public void Dispose()
    {
        watcher.Dispose();
        timer.Dispose();
    }
}
