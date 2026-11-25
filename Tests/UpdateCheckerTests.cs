using EmbedItems.Core;

namespace EmbedItems.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("https://github.com/AchimBunke/EmbedItems/releases/tag/v2.1.0", "2.1.0")]
    [InlineData("https://github.com/AchimBunke/EmbedItems/releases/tag/v2.1.0/", "2.1.0")]
    [InlineData("v10.0.3", "10.0.3")]
    [InlineData("2.1", "2.1.0")]   // short tags count as .0
    [InlineData("https://github.com/AchimBunke/EmbedItems/releases", null)]
    [InlineData("vNext", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseTag(string? input, string? expected) =>
        Assert.Equal(expected, UpdateChecker.ParseTag(input)?.ToString(3));

    [Theory]
    [InlineData("2.1.0", "2.0.0", true)]
    [InlineData("2.10.0", "2.9.0", true)]   // numeric, not text order
    [InlineData("2.1.0", "2.1.0", false)]
    [InlineData("2.0.0", "2.1.0", false)]
    public void IsNewer(string latest, string current, bool expected) =>
        Assert.Equal(expected, UpdateChecker.IsNewer(Version.Parse(latest), Version.Parse(current)));

    [Fact]
    public void ReleaseUrl_points_to_the_tag_page() =>
        Assert.Equal("https://github.com/AchimBunke/EmbedItems/releases/tag/v2.1.0", UpdateChecker.ReleaseUrl(new Version(2, 1, 0)));
}
