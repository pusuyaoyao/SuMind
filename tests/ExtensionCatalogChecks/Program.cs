using SuMind;

var item = new ExtensionCatalogItem("desktop-navigation", "桌面导航", "任务栏", "1.0.0", "1.0.0", "win-x64",
    "https://github.com/pusuyaoyao/SuMind/releases/download/desktop-navigation-v1.0.0/navigation.suminds",
    new string('a', 64), 100, Convert.ToBase64String(new byte[] { 1 }));
ExtensionCatalogService.Validate(item);
foreach (var invalid in new[] {
    item with { PackageUrl = "http://github.com/pusuyaoyao/SuMind/releases/download/x/x.suminds" },
    item with { PackageUrl = "https://github.com/other/repo/releases/download/x/x.suminds" },
    item with { PackageUrl = "https://github.com/pusuyaoyao/SuMind/releases/download/x/x.suminds?redirect=other" },
    item with { Sha256 = new string('z', 64) }, item with { Size = 0 }, item with { Signature = "bad" },
    item with { Id = "../bad" }, item with { Architecture = "any" }
})
{
    try { ExtensionCatalogService.Validate(invalid); throw new Exception("An invalid catalog entry was accepted."); }
    catch (InvalidDataException) { }
}
Console.WriteLine("Catalog entry validation passed.");
string localFile = Path.GetTempFileName();
try
{
    byte[] bytes = "local package sample"u8.ToArray();
    await File.WriteAllBytesAsync(localFile, bytes);
    var localItem = item with { Size = bytes.Length, Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) };
    if (!await ExtensionCatalogService.MatchesLocalPackageAsync(localItem, localFile)) throw new Exception("Matching local file was rejected.");
    if (await ExtensionCatalogService.MatchesLocalPackageAsync(localItem with { Sha256 = new string('0', 64) }, localFile)) throw new Exception("Wrong digest was accepted.");
    if (await ExtensionCatalogService.MatchesLocalPackageAsync(localItem with { Size = bytes.Length + 1 }, localFile)) throw new Exception("Wrong size was accepted.");
    Console.WriteLine("Local package matching passed.");
}
finally { File.Delete(localFile); }

