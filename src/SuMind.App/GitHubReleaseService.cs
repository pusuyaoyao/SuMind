using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace SuMind;

internal static class GitHubReleaseService
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SuMind-Desktop");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static async Task<string?> GetLatestReleaseAsync()
    {
        using var response = await Client.GetAsync("https://api.github.com/repos/pusuyaoyao/SuMind/releases/latest");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.GetProperty("tag_name").GetString()
            ?? throw new JsonException("GitHub 版本缺少标签。");
    }
}
