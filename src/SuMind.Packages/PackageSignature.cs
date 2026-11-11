using System.Security.Cryptography;

namespace SuMind.Packages;

/// <summary>Detached RSA-PSS/SHA-256 signature. Trust comes from the caller's pinned public key.</summary>
public static class PackageSignature
{
    public static void Verify(Stream package, ReadOnlySpan<byte> signature, string trustedPublicKeyPem)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!package.CanRead || !package.CanSeek) throw new ArgumentException("拓展包必须支持读取和定位。", nameof(package));
        if (package.Length > 512L * 1024 * 1024) throw new InvalidDataException("拓展包过大。");
        using var rsa = RSA.Create();
        rsa.ImportFromPem(trustedPublicKeyPem);
        if (rsa.KeySize < 3072) throw new CryptographicException("发布公钥长度不足。");
        package.Position = 0;
        if (!rsa.VerifyData(package, signature.ToArray(), HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new CryptographicException("拓展包签名无效。");
        package.Position = 0;
    }
}
