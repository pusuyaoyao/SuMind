using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SuMind.Packages;

if (args.Length == 3 && args[0] == "keygen")
{
    try
    {
        using var rsa = RSA.Create(3072);
        using (var secret = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(secret)) writer.Write(rsa.ExportPkcs8PrivateKeyPem());
        using (var publicFile = new FileStream(args[2], FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(publicFile)) writer.Write(rsa.ExportSubjectPublicKeyInfoPem());
        Console.WriteLine("密钥已生成。私钥必须存放在受保护位置，不能提交到仓库。");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
}
if (args.Length == 4 && args[0] == "sign")
{
    try
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(args[2]));
        if (rsa.KeySize < 3072) throw new CryptographicException("签名密钥长度不足。");
        using var package = File.OpenRead(args[1]);
        byte[] signature = rsa.SignData(package, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        using var output = new FileStream(args[3], FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(signature);
        Console.WriteLine("签名已生成。");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
}
if (args.Length == 4 && args[0] == "verify")
{
    try
    {
        using var input = File.OpenRead(args[1]);
        PackageSignature.Verify(input, File.ReadAllBytes(args[2]), File.ReadAllText(args[3]));
        Console.WriteLine("签名验证成功。");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
}
if (args.Length == 5 && args[0] == "extract")
{
    try
    {
        using var input = File.OpenRead(args[1]);
        var extracted = PackageReader.Extract(input, args[2], args[3], args[4]);
        Console.WriteLine($"已读取 {extracted.Id} {extracted.Version}，共 {extracted.Files.Length} 个文件。");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
}
if (args.Length != 5 || args[0] != "pack")
{
    Console.Error.WriteLine("用法: pack <拓展ID> <版本> <输入目录> <输出.suminds>");
    Console.Error.WriteLine("      extract <包路径> <新目录> <预期ID> <预期SHA-256>");
    Console.Error.WriteLine("      keygen <私钥.pem> <公钥.pem> | sign <包> <私钥.pem> <签名文件> | verify <包> <签名文件> <公钥.pem>");
    return 1;
}
try
{
    string id = args[1], version = args[2];
    if (string.IsNullOrWhiteSpace(id) || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') || !Version.TryParse(version, out _))
        throw new ArgumentException("拓展 ID 或版本无效。");
    string source = Path.GetFullPath(args[3]);
    string target = Path.GetFullPath(args[4]);
    if (target.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("输出文件不能位于输入目录内。");
    if (!Directory.Exists(source) || (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("输入目录不存在或是链接。");
    var paths = new List<string>();
    var pending = new Stack<string>();
    pending.Push(source);
    while (pending.TryPop(out var directory))
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attr = File.GetAttributes(entry);
            if ((attr & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("输入目录不能包含链接。");
            if ((attr & FileAttributes.Directory) != 0) pending.Push(entry); else paths.Add(entry);
        }
    }
    paths.Sort(StringComparer.Ordinal);
    if (paths.Count is 0 or > 1024) throw new ArgumentException("文件数量无效。");
    var files = paths.Select(p =>
    {
        using var stream = File.OpenRead(p);
        if (stream.Length > 128L * 1024 * 1024) throw new ArgumentException("单个文件过大。");
        return new PackageFile(Path.GetRelativePath(source, p).Replace('\\', '/'), stream.Length, Convert.ToHexString(SHA256.HashData(stream)));
    }).ToArray();
    var manifest = JsonSerializer.SerializeToUtf8Bytes(new PackageManifest(id, version, files));
    if (manifest.Length > 1024 * 1024 || files.Sum(f => f.Size) + manifest.Length + 12 > 512L * 1024 * 1024) throw new ArgumentException("包过大。");
    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    using (var writer = new BinaryWriter(output, Encoding.UTF8))
    {
        writer.Write("SUMINDS1"u8);
        writer.Write(manifest.Length);
        writer.Write(manifest);
        foreach (string path in paths) { using var file = File.OpenRead(path); file.CopyTo(output); }
    }
    using var result = File.OpenRead(target);
    Console.WriteLine($"已生成 {target}\nSHA-256: {Convert.ToHexString(SHA256.HashData(result)).ToLowerInvariant()}");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
