namespace EmbedItems.Core;

/// <summary>Plain-language description of a scan result, shared by the UI and the command line.</summary>
public static class PlanText
{
    public static string StateLabel(ItemState state) => state switch
    {
        ItemState.WillEmbed => "will embed",
        ItemState.WillUpdate => "will update",
        ItemState.WillRestoreFiles => "files missing",
        ItemState.UpToDate => "embedded",
        ItemState.NoReferences => "no extra files",
        ItemState.EmbeddedByOthers => "embedded by game",
        ItemState.NotFound => "not found",
        ItemState.CannotEmbed => "CAN'T EMBED",
        _ => state.ToString(),
    };

    public static IEnumerable<string> Describe(MapPlan plan)
    {
        yield return $"{Path.GetFileName(plan.MapPath)}  (map name \"{plan.MapName}\")";
        foreach (var w in plan.Warnings) yield return "  ! " + w;
        if (plan.Items.Count == 0) yield return "  No custom items are placed in this map.";

        foreach (var item in plan.Items)
        {
            yield return $"  [{StateLabel(item.State)}] Items\\{PathUtils.ToRef(item.OriginalPath)}  (placed {item.PlacedCount}x)";
            yield return "      " + item.Summary;
            foreach (var n in item.Notes) yield return "      - " + n;
        }
        if (plan.StaleFiles.Count > 0)
            yield return $"  {plan.StaleFiles.Count} file(s) in the map are no longer used by any embedded item and will be removed.";
    }

    /// <summary>One line for folder scan lists.</summary>
    public static string Short(MapPlan plan)
    {
        var parts = new List<string>();
        void Add(int n, string what) { if (n > 0) parts.Add($"{n} {what}"); }
        Add(plan.Items.Count(i => i.State == ItemState.WillEmbed), "to embed");
        Add(plan.Items.Count(i => i.State == ItemState.WillUpdate), "outdated");
        Add(plan.Items.Count(i => i.State == ItemState.WillRestoreFiles), "missing files");
        Add(plan.Items.Count(i => i.State == ItemState.CannotEmbed), "can't embed");
        Add(plan.Items.Count(i => i.State == ItemState.UpToDate), "embedded");
        if (!plan.Validated) parts.Add("not validated");
        return parts.Count == 0 ? "nothing to embed" : string.Join(", ", parts);
    }
}
