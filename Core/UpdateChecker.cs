using System.Net;
using System.Reflection;

namespace EmbedItems.Core;

/// <summary>
/// Finds out whether a newer release exists on GitHub. Only reads; never downloads anything.
/// Uses the redirect of the "latest release" page (…/releases/latest -> …/releases/tag/v2.1.0) instead of the GitHub
/// API: no JSON, and not counted against the API's 60 requests per hour per IP (users behind one shared IP).
/// </summary>
public static class UpdateChecker
{
    public const string LatestReleaseUrl = "https://github.com/AchimBunke/EmbedItems/releases/latest";

    public static string ReleaseUrl(Version version) =>
        $"https://github.com/AchimBunke/EmbedItems/releases/tag/v{version.ToString(3)}";

    /// <summary>
    /// Version of the running program, or null for a local build. Release builds get their version from the tag
    /// (release.yml passes -p:Version=2.1.0); local builds stay at the 0.0.0 set in the project file.
    /// </summary>
    public static Version? CurrentVersion()
    {
        var v = Assembly.GetEntryAssembly()?.GetName().Version;
        return v is null || v.Major + v.Minor + v.Build <= 0 ? null : new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }

    /// <summary>Version from a release tag or its page URL ("v2.1.0", ".../releases/tag/v2.1.0"); null if there is none.</summary>
    public static Version? ParseTag(string? tagOrUrl)
    {
        if (string.IsNullOrEmpty(tagOrUrl)) return null;
        var tag = tagOrUrl.TrimEnd('/');
        tag = tag[(tag.LastIndexOf('/') + 1)..];
        if (tag.StartsWith('v') || tag.StartsWith('V')) tag = tag[1..];
        return Version.TryParse(tag, out var v) ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : null;
    }

    public static bool IsNewer(Version latest, Version current) => latest > current;

    /// <summary>
    /// Latest released version, or null if GitHub can't be reached or answers unexpectedly (callers stay silent then).
    /// </summary>
    public static async Task<Version?> GetLatestAsync(CancellationToken cancel = default)
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EmbedItems");   // GitHub rejects requests without one
            using var response = await client.GetAsync(LatestReleaseUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
            return response.StatusCode is HttpStatusCode.Found or HttpStatusCode.MovedPermanently
                ? ParseTag(response.Headers.Location?.ToString())
                : null;
        }
        catch (Exception) when (!cancel.IsCancellationRequested)
        {
            return null;   // offline, timeout, blocked: not worth bothering the user
        }
    }
}
