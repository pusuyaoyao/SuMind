using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SuMind.Packages;

string root = Path.Combine(Path.GetTempPath(), "suminds-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    using RSA signer = RSA.Create(3072);
    using RSA other = RSA.Create(3072);
    string publicKey = signer.ExportSubjectPublicKeyInfoPem();
    string installRoot = Path.Combine(root, "installed");
    string id = "desktop-navigation";

    (string file, string hash, byte[] signature) MakePackage(string version, string name, string content)
    {
        byte[] data = Encoding.UTF8.GetBytes(content);
        var manifest = new PackageManifest(id, version, [new PackageFile(name, data.Length, Convert.ToHexString(SHA256.HashData(data)))], "桌面导航", "任务栏", "pusu11", null, "1.0.0", "win-x64");
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(manifest);
        string file = Path.Combine(root, Guid.NewGuid().ToString("N") + ".suminds");
        using (var stream = File.Create(file))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8))
        {
            writer.Write("SUMINDS1"u8); writer.Write(json.Length); writer.Write(json); writer.Write(data);
        }
        byte[] bytes = File.ReadAllBytes(file);
        return (file, Convert.ToHexString(SHA256.HashData(bytes)), signer.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
    }
    void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    void Reject(Action action, string message) { try { action(); } catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or CryptographicException or JsonException) { return; } throw new Exception(message); }

    var first = MakePackage("1.0.0", "bin/worker.exe", "v1");
    var installed = PackageInstaller.Install(first.file, installRoot, id, "1.0.0", first.hash, first.signature, publicKey);
    Assert(File.ReadAllText(Path.Combine(installed.Directory, "bin", "worker.exe")) == "v1", "installed content mismatch");
    Assert(PackageInstaller.GetInstalled(installRoot, id)?.Version == "1.0.0", "active version mismatch");
    var second = MakePackage("2.0.0", "bin/worker.exe", "v2");
    using (var locked = new FileStream(Path.Combine(installRoot, ".install.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        Reject(() => PackageInstaller.Install(second.file, installRoot, id, "2.0.0", second.hash, second.signature, publicKey), "concurrent install accepted");
    Reject(() => PackageInstaller.Install(second.file, installRoot, id, "2.0.0", second.hash, second.signature, other.ExportSubjectPublicKeyInfoPem()), "untrusted key accepted");
    Reject(() => PackageInstaller.Install(second.file, installRoot, id, "2.0.1", second.hash, second.signature, publicKey), "mismatched version accepted");
    Reject(() => PackageInstaller.Install(second.file, installRoot, id, "2.0.0", new string('0', 64), second.signature, publicKey), "bad hash accepted");
    Reject(() => PackageInstaller.Install(second.file, installRoot, id, "2.0.0", second.hash, second.signature, publicKey, new PackageMetadata("桌面导航", "任务栏", "wrong", "1.0.0", "win-x64")), "mismatched author accepted");
    Assert(PackageInstaller.GetInstalled(installRoot, id)?.Version == "1.0.0", "failed install changed active version");
    var malicious = MakePackage("3.0.0", "../escape.txt", "escape");
    Reject(() => PackageInstaller.Install(malicious.file, installRoot, id, "3.0.0", malicious.hash, malicious.signature, publicKey), "traversal accepted");
    Assert(!File.Exists(Path.Combine(root, "escape.txt")), "escaped installation directory");
    PackageInstaller.Install(second.file, installRoot, id, "2.0.0", second.hash, second.signature, publicKey, new PackageMetadata("桌面导航", "任务栏", "pusu11", "1.0.0", "win-x64"));
    Assert(PackageInstaller.GetInstalled(installRoot, id)?.Version == "2.0.0", "upgrade failed");
    PackageInstaller.ActivateVersion(installRoot, id, "1.0.0");
    Assert(PackageInstaller.GetInstalled(installRoot, id)?.Version == "1.0.0", "rollback failed");
    Reject(() => PackageInstaller.ActivateVersion(installRoot, "../bad", "1.0.0"), "unsafe ID accepted");
    Assert(!Directory.EnumerateDirectories(Path.Combine(installRoot, id, "versions"), ".staging-*").Any(), "staging directory leaked");
    string settings = Path.Combine(root, "settings.json");
    File.WriteAllText(settings, "keep");
    Assert(PackageInstaller.Uninstall(installRoot, id), "uninstall failed");
    Assert(PackageInstaller.GetInstalled(installRoot, id) is null, "extension still installed");
    Assert(File.ReadAllText(settings) == "keep", "uninstall deleted settings");
    Assert(!PackageInstaller.Uninstall(installRoot, id), "uninstall should be idempotent");
    if (args.Length == 3)
    {
        var packageBytes = File.ReadAllBytes(args[0]);
        var expected = new PackageMetadata("桌面导航", "管理 Windows 任务栏外观，Dock 功能仍在规划。", "pusu11", "1.0.0", "win-x64", "83814626a6197013428379bf024442be01423e2e4359e35714b9fb071196096f");
        var live = PackageInstaller.Install(args[0], Path.Combine(root, "release-test"), id, "0.1.1", Convert.ToHexString(SHA256.HashData(packageBytes)), File.ReadAllBytes(args[1]), File.ReadAllText(args[2]), expected);
        Assert(File.Exists(Path.Combine(live.Directory, "DesktopNavigation.exe")), "released worker missing");
        Assert(File.Exists(Path.Combine(live.Directory, "assets", "icon.png")), "released icon missing");
        Console.WriteLine("Release package signature, metadata and installation passed.");
    }
    Console.WriteLine("Package install, signature, traversal, upgrade and rollback checks passed.");
    return 0;
}
finally { Directory.Delete(root, recursive: true); }
