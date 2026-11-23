using EmbedItems.Core;

namespace EmbedItems.Tests;

public class PathUtilsTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("EmbedItemsTests").FullName;
    readonly TrackmaniaFolder tm;

    public PathUtilsTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "Items"));
        tm = new TrackmaniaFolder(root);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Theory]
    // plain relative reference straight down: already in the form the game needs
    [InlineData("A/B/Tree.Item.Gbx", 0, @"Tex\x.dds", "A/B/Tex/x.dds", true)]
    // ".." (how GBX.NET writes references to files higher up)
    [InlineData("A/B/Tree.Item.Gbx", 0, @"..\..\_Assets\x.dds", "_Assets/x.dds", false)]
    // ancestor level N = start N folders above the owner's folder (Nadeo pak items: level 1, "Media\...")
    [InlineData("Stadium/Items/Fall.Item.Gbx", 1, @"Media\Tree.Gbx", "Stadium/Media/Tree.Gbx", false)]
    // written by the game when it saves a map with embedded items
    [InlineData("Tree_1234.Item.Gbx", 2, @"ContentLoaded\Items\_Assets\x.dds", "_Assets/x.dds", false)]
    public void Resolve_finds_the_items_relative_target(string owner, int level, string refPath, string expected, bool canonical)
    {
        var (target, isCanonical) = PathUtils.Resolve(owner, level, refPath, tm);
        Assert.Equal(expected, target);
        Assert.Equal(canonical, isCanonical);
    }

    [Theory]
    [InlineData("A/Tree.Item.Gbx", 0, @"..\..\x.dds")]   // above the Items folder
    [InlineData("A/Tree.Item.Gbx", 3, @"x.dds")]
    public void Resolve_returns_null_outside_the_items_folder(string owner, int level, string refPath)
    {
        Assert.Null(PathUtils.Resolve(owner, level, refPath, tm).Target);
    }

    [Fact]
    public void Resolve_maps_absolute_paths_into_the_items_folder()
    {
        var abs = Path.Combine(root, "Items", "_Assets", "Image", "x.dds");
        Assert.Equal("_Assets/Image/x.dds", PathUtils.Resolve("_Assets/x.Texture.Gbx", 0, abs, tm).Target);
        Assert.Null(PathUtils.Resolve("_Assets/x.Texture.Gbx", 0, @"Z:\Elsewhere\x.dds", tm).Target);
    }

    [Theory]
    [InlineData("A/B", "A/C/x.dds", @"..\C\x.dds")]
    [InlineData("", "A/x.dds", @"A\x.dds")]
    [InlineData("Testing/Trees", "_Assets/Tex/x.dds", @"..\..\_Assets\Tex\x.dds")]
    public void RelativeTo(string folder, string path, string expected) =>
        Assert.Equal(expected, PathUtils.RelativeTo(folder, path));

    [Theory]
    [InlineData("Items/A/Tree.Item.Gbx", "A/Tree.Item.Gbx")]
    [InlineData("C:/Users/x/Documents/Trackmania/Items/A/Tree.Item.Gbx", "A/Tree.Item.Gbx")]
    [InlineData("EmbedItems/manifest.json", null)]
    public void ZipKey(string entry, string? expected) => Assert.Equal(expected, PathUtils.ZipKey(entry));

    [Fact]
    public void EmbeddedName_is_stable_and_differs_per_folder()
    {
        var a = MapPlanner.EmbeddedName("A/Tree.Item.Gbx");
        Assert.Equal(a, MapPlanner.EmbeddedName("a/Tree.Item.Gbx"));   // folder case doesn't matter (Windows paths)
        Assert.NotEqual(a, MapPlanner.EmbeddedName("B/Tree.Item.Gbx"));
        Assert.Matches(@"^Tree_[0-9a-f]{8}\.Item\.Gbx$", a);
    }
}
