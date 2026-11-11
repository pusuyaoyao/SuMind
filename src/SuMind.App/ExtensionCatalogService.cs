using System.Net;
using System.Net.Http;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace SuMind;

// Catalog data is untrusted metadata. A matching hash only detects corruption;
// packages MUST also be verified with a public key trusted by the application.
internal sealed record ExtensionCatalog(int SchemaVersion, ExtensionCatalogItem[] Extensions);
internal sealed record ExtensionCatalogItem(string Id, string Name, string Description, string Version,
    string MinHostVersion, string Architecture, string PackageUrl, string Sha256, long Size, string Signature);

internal static class ExtensionCatalogService
{
    public const string CatalogUrl = "https://raw.githubusercontent.com/pusuyaoyao/SuMind/main/catalog/extensions.json";
    private const int MaxCatalogBytes = 1024 * 1024;
    private const long MaxPackageBytes = 512L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, MaxDepth = 16 };
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SuMind-Desktop");
        return client;
    }

    public static async Task<ExtensionCatalog?> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(CatalogUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxCatalogBytes) throw new InvalidDataException("拓展目录过大。");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memory.Length + count > MaxCatalogBytes) throw new InvalidDataException("拓展目录过大。");
            await memory.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        var catalog = JsonSerializer.Deserialize<ExtensionCatalog>(memory.ToArray(), JsonOptions) ?? throw new InvalidDataException("拓展目录为空。");
        if (catalog.SchemaVersion != 1 || catalog.Extensions is null || catalog.Extensions.Length > 256)
            throw new InvalidDataException("拓展目录版本或数量无效。");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog.Extensions)
        {
            Validate(item);
            if (!ids.Add(item.Id + ":" + item.Architecture)) throw new InvalidDataException("拓展目录包含重复的 ID 和架构。");
        }
        return catalog;
    }

    public static async Task DownloadPackageAsync(ExtensionCatalogItem item, string destinationPath, CancellationToken cancellationToken = default)
    {
        Validate(item);
        using var response = await GetReleaseAssetAsync(new Uri(item.PackageUrl), cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length != item.Size)
            throw new InvalidDataException("拓展包长度与目录不符。");
        bool created = false;
        try
        {
            await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
            created = true;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[65536];
            long total = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += count;
                if (total > item.Size || total > MaxPackageBytes) throw new InvalidDataException("拓展包超过预期大小。");
                hash.AppendData(buffer, 0, count);
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
            if (total != item.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(item.Sha256)))
                throw new InvalidDataException("拓展包长度或 SHA-256 校验失败。");
        }
        catch
        {
            if (created) { try { File.Delete(destinationPath); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            throw;
        }
    }

    public static async Task<bool> MatchesLocalPackageAsync(ExtensionCatalogItem item, string path, CancellationToken cancellationToken = default)
    {
        Validate(item);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (input.Length != item.Size) return false;
        byte[] digest = await SHA256.HashDataAsync(input, cancellationToken);
        return CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(item.Sha256));
    }

    internal static void Validate(ExtensionCatalogItem? item)
    {
        if (item is null || string.IsNullOrEmpty(item.Id) || item.Id.Length > 80 || !item.Id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 80 || item.Description is null || item.Description.Length > 300
            || !System.Version.TryParse(item.Version, out _) || !System.Version.TryParse(item.MinHostVersion, out _)
            || item.Architecture is not ("win-x64" or "win-arm64" or "win-x86") || item.Size <= 0 || item.Size > MaxPackageBytes
            || item.Sha256 is null || item.Sha256.Length != 64 || !item.Sha256.All(Uri.IsHexDigit)
            || string.IsNullOrEmpty(item.Signature) || item.Signature.Length > 2048
            || !Uri.TryCreate(item.PackageUrl, UriKind.Absolute, out var url) || !IsReleaseUrl(url))
            throw new InvalidDataException("拓展目录条目无效。");
        try { if (Convert.FromBase64String(item.Signature).Length == 0) throw new FormatException(); }
        catch (FormatException) { throw new InvalidDataException("拓展签名格式无效。"); }
    }

    private static bool IsReleaseUrl(Uri url) => url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort
        && string.IsNullOrEmpty(url.UserInfo) && string.IsNullOrEmpty(url.Query) && string.IsNullOrEmpty(url.Fragment)
        && url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && url.AbsolutePath.StartsWith("/pusuyaoyao/SuMind/releases/download/", StringComparison.Ordinal)
        && url.AbsolutePath.EndsWith(".suminds", StringComparison.OrdinalIgnoreCase);

    private static async Task<HttpResponseMessage> GetReleaseAssetAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (int i = 0; i < 5; i++)
        {
            var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is < 300 or >= 400) return response;
            Uri? location = response.Headers.Location;
            response.Dispose();
            if (location is null) throw new HttpRequestException("GitHub 返回了无效的下载重定向。");
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
            if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)
                || !(uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                  || uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                  || uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
                throw new HttpRequestException("拓展包重定向到了不受信任的服务器。");
        }
        throw new HttpRequestException("拓展包重定向次数过多。");
    }
}

