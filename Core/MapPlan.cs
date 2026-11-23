using GBX.NET;
using GBX.NET.Engines.Game;

namespace EmbedItems.Core;

public sealed record EmbedOptions
{
    /// <summary>Replace items embedded by EmbedItems with the current version from the Items folder (when found).</summary>
    public bool OverwriteFromDisk { get; init; } = true;

    /// <summary>Save over the original map instead of writing "&lt;name&gt; (embedded).Map.Gbx" next to it.</summary>
    public bool ReplaceOriginal { get; init; }

    /// <summary>New map name (the name shown in game). Null or empty keeps the current name.</summary>
    public string? MapName { get; init; }
}

public enum ItemState
{
    /// <summary>From the Items folder; will be moved to the Items root and embedded with its files.</summary>
    WillEmbed,
    /// <summary>Embedded by EmbedItems before; will be replaced by the newer version from the Items folder.</summary>
    WillUpdate,
    /// <summary>Embedded by EmbedItems, but files are missing (map saved in the editor); they will be added again.</summary>
    WillRestoreFiles,
    /// <summary>Embedded by EmbedItems and complete.</summary>
    UpToDate,
    /// <summary>Uses no extra files; the game embeds such items by itself.</summary>
    NoReferences,
    /// <summary>Embedded by the game or another tool; left alone.</summary>
    EmbeddedByOthers,
    /// <summary>Neither embedded nor in the Items folder (stock item, or missing).</summary>
    NotFound,
    /// <summary>Can't work when embedded (see the summary); left unchanged.</summary>
    CannotEmbed,
}

/// <summary>What happens to one placed item model.</summary>
public sealed class ItemPlan
{
    /// <summary>Path relative to the Items folder where the item lives on disk (or came from).</summary>
    public required string OriginalPath { get; init; }

    /// <summary>File name in the Items root of the map, e.g. "Tree_3fa2c1d4.Item.Gbx".</summary>
    public string? EmbeddedName { get; set; }

    public ItemState State { get; set; }
    public string Summary { get; set; } = "";
    public List<string> Notes { get; } = [];

    /// <summary>How many objects in the map use this item.</summary>
    public int PlacedCount { get; set; }

    /// <summary>Number of files (besides the item) it uses.</summary>
    public int FileCount { get; set; }

    // --- used when embedding -------------------------------------------------------------------------------

    /// <summary>Item ids of placed objects that should point to the embedded item afterwards.</summary>
    internal HashSet<string> PlacedIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>New item file to write at EmbeddedName (null = keep the one in the map).</summary>
    internal byte[]? ItemBytes { get; set; }

    /// <summary>Ident of the embedded item; placed objects must carry the same author.</summary>
    internal Ident? Ident { get; set; }

    /// <summary>Files to write into the map (Items-relative path -> content).</summary>
    internal Dictionary<string, byte[]> FilesToWrite { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All files the item needs after embedding (to know which old files nobody uses anymore).</summary>
    internal HashSet<string> NeededFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Files the previously embedded version used.</summary>
    internal HashSet<string> OldFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Map entry to remove because the item is now embedded under EmbeddedName.</summary>
    internal string? OldZipKey { get; set; }

    public bool Changes => State is ItemState.WillEmbed or ItemState.WillUpdate or ItemState.WillRestoreFiles;

    /// <summary>True once the item is (or will be) embedded by EmbedItems.</summary>
    internal bool IsOurs => Changes || State == ItemState.UpToDate;
}

/// <summary>Result of scanning a map: what embedding would do. Embedder.Apply carries it out.</summary>
public sealed class MapPlan
{
    public required string MapPath { get; init; }
    public required string MapName { get; init; }

    /// <summary>False if the map has no author time. Such maps must be validated in the editor before playing.</summary>
    public bool Validated { get; init; }

    public List<ItemPlan> Items { get; } = [];
    public List<string> Warnings { get; } = [];

    internal Gbx<CGameCtnChallenge> Gbx { get; init; } = null!;   // always set by MapPlanner

    /// <summary>Map entries no item needs anymore (files of an older version of an embedded item).</summary>
    internal HashSet<string> StaleFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Some placed objects still point to the original item instead of the embedded copy.</summary>
    internal bool NeedsRedirect => Items.Any(i => i.IsOurs && i.PlacedIds.Any(id => !id.Equals(i.EmbeddedName, StringComparison.OrdinalIgnoreCase)));

    public bool NeedsWork => Items.Any(i => i.Changes) || NeedsRedirect || StaleFiles.Count > 0;

    public bool HasNotEmbedded => Items.Any(i => i.State == ItemState.WillEmbed);
    public bool HasMissingFiles => Items.Any(i => i.State == ItemState.WillRestoreFiles);
    public bool HasOutdated => Items.Any(i => i.State == ItemState.WillUpdate);
}
