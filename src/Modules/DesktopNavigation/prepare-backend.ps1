param(
    [Parameter(Mandatory=$true)][string]$Destination,
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64'
)
$ErrorActionPreference = 'Stop'
$hashes = @{
    x64 = '0dbe8e0255c20e131cde536dcd0ae490d45989a7d360e26d0150dff1922ac420'
    arm64 = '81b2f386bd4776a2d9f5c44d8a9b5b3aebccd2eeffaa53435eac24aa38797c82'
}
$url = "https://github.com/TranslucentTB/TranslucentTB/releases/download/2026.2/TranslucentTB-portable-$Architecture.zip"
$archive = Join-Path ([System.IO.Path]::GetTempPath()) ("sumindsttb-" + [guid]::NewGuid().ToString('N') + '.zip')
try {
    Invoke-WebRequest -Uri $url -OutFile $archive
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $hashes[$Architecture]) { throw 'TranslucentTB 下载的 SHA-256 与固定版本不符。' }
    if (Test-Path -LiteralPath $Destination) { throw '目标目录已存在；请指定一个新的空位置。' }
    Expand-Archive -LiteralPath $archive -DestinationPath $Destination
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TranslucentTB-LICENSE.md') -Destination (Join-Path $Destination 'LICENSE.md')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TranslucentTB-NOTICE.txt') -Destination (Join-Path $Destination 'NOTICE.txt')
    Write-Output "已准备 TranslucentTB 2026.2 后端：$Destination"
} finally {
    Remove-Item -LiteralPath $archive -ErrorAction SilentlyContinue
}
