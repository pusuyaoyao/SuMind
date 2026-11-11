param(
    [Parameter(Mandatory=$true)][string]$Package,
    [Parameter(Mandatory=$true)][string]$PackageUrl,
    [string]$IconUrl,
    [string]$Signature,
    [switch]$Preview
)
$ErrorActionPreference = 'Stop'
$reader = [System.IO.BinaryReader]::new([System.IO.File]::OpenRead((Resolve-Path -LiteralPath $Package)))
try {
    if ([System.Text.Encoding]::ASCII.GetString($reader.ReadBytes(8)) -ne 'SUMINDS1') { throw '拓展包格式无效。' }
    $length = $reader.ReadInt32()
    if ($length -le 0 -or $length -gt 1048576) { throw '清单长度无效。' }
    $bytes = $reader.ReadBytes($length)
    if ($bytes.Length -ne $length) { throw '拓展清单不完整。' }
    $manifest = [System.Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
} finally { $reader.Dispose() }
if (-not $manifest.Id -or -not $manifest.Name -or -not $manifest.Author -or -not $manifest.Version -or -not $manifest.MinHostVersion -or -not $manifest.Architecture) { throw '拓展包缺少展示信息。' }
if (-not $Preview -and -not $Signature) { throw '正式包必须提供签名文件。' }
$iconHash = $null
if ($manifest.IconPath) {
    if (-not $IconUrl) { throw '包包含图标，必须提供同一发布版本中的图标地址。' }
    $matches = @($manifest.Files | Where-Object Path -eq $manifest.IconPath)
    if ($matches.Count -ne 1) { throw '包内图标信息无效。' }
    $iconHash = $matches[0].Sha256.ToLowerInvariant()
} elseif ($IconUrl) { throw '包内没有图标。' }
$signatureValue = if ($Signature) { [Convert]::ToBase64String([System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Signature))) } else { '' }
[ordered]@{
    id=$manifest.Id; name=$manifest.Name; description=$manifest.Description; version=$manifest.Version; author=$manifest.Author
    minHostVersion=$manifest.MinHostVersion; architecture=$manifest.Architecture; packageUrl=$PackageUrl
    sha256=(Get-FileHash -LiteralPath $Package -Algorithm SHA256).Hash.ToLowerInvariant()
    size=(Get-Item -LiteralPath $Package).Length; signature=$signatureValue; isPreview=[bool]$Preview
    iconUrl=$IconUrl; iconSha256=$iconHash
} | ConvertTo-Json -Depth 6
