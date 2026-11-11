param(
    [Parameter(Mandatory=$true)][string]$Package,
    [Parameter(Mandatory=$true)][string]$Signature
)
$ErrorActionPreference = 'Stop'
$keyPath = Join-Path $env:LOCALAPPDATA 'SuMind\signing\publisher-key.dpapi'
if (-not (Test-Path -LiteralPath $keyPath)) { throw '找不到当前用户的发布密钥；不要在原有公钥下另行生成密钥。' }
if (Test-Path -LiteralPath $Signature) { throw '签名目标文件已存在。' }
$rsa = [System.Security.Cryptography.RSA]::Create()
try {
    $keyBytes = [System.Security.Cryptography.ProtectedData]::Unprotect([System.IO.File]::ReadAllBytes($keyPath), $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
    try { $read = 0; $rsa.ImportPkcs8PrivateKey($keyBytes, [ref]$read) }
    finally { [System.Security.Cryptography.CryptographicOperations]::ZeroMemory($keyBytes) }
    $stream = [System.IO.File]::OpenRead((Resolve-Path -LiteralPath $Package))
    try { $signatureBytes = $rsa.SignData($stream, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pss) }
    finally { $stream.Dispose() }
    $output = [System.IO.File]::Open($Signature, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try { $output.Write($signatureBytes) } finally { $output.Dispose() }
    '签名已生成。'
} finally { $rsa.Dispose() }
