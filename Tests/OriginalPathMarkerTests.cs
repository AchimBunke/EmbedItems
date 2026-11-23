using EmbedItems.Core;

namespace EmbedItems.Tests;

public class OriginalPathMarkerTests
{
    [Fact]
    public void Apply_keeps_the_existing_description_and_Read_finds_the_marker()
    {
        var d = OriginalPathMarker.Apply("My tree", "Testing/Trees/Tree.Item.Gbx", "abc123");
        Assert.Equal("My tree\n[EmbedItems] Items\\Testing\\Trees\\Tree.Item.Gbx #abc123", d);

        var info = OriginalPathMarker.Read(d);
        Assert.Equal("Testing/Trees/Tree.Item.Gbx", info!.OriginalPath);
        Assert.Equal("abc123", info.Hash);
    }

    [Fact]
    public void Apply_replaces_an_old_marker()
    {
        var once = OriginalPathMarker.Apply("x", "A/Old.Item.Gbx", "1");
        var twice = OriginalPathMarker.Apply(once, "A/New.Item.Gbx", "2");
        Assert.Equal("x\n[EmbedItems] Items\\A\\New.Item.Gbx #2", twice);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("N/A\r\n[EmbedItems] Items\\A\\T.Item.Gbx #1", "N/A")]
    [InlineData("[EmbedItems] Items\\A\\T.Item.Gbx #1", "")]
    public void Remove_restores_the_original_description(string? description, string expected) =>
        Assert.Equal(expected, OriginalPathMarker.Remove(description));

    [Fact]
    public void Read_returns_null_without_marker() => Assert.Null(OriginalPathMarker.Read("just a description"));
}
