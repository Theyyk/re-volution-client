param(
    [string]$ManifestUrl = $env:REVOLUTION_REMOTE_DEV_MANIFEST_URL
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManifestUrl)) {
    $config = Join-Path $root 'remote-dev-url.txt'
    if (Test-Path -LiteralPath $config -PathType Leaf) {
        $ManifestUrl = (Get-Content -LiteralPath $config -Raw).Trim()
    }
}
if ([string]::IsNullOrWhiteSpace($ManifestUrl)) {
    throw 'Set REVOLUTION_REMOTE_DEV_MANIFEST_URL or place remote-dev-url.txt next to this script.'
}
$uri = $null
if (-not [Uri]::TryCreate($ManifestUrl, [UriKind]::Absolute, [ref]$uri) -or
    $uri.Scheme -cne 'https' -or
    $uri.AbsolutePath -cne '/api/v1/dev/channel/remote' -or
    -not [string]::IsNullOrEmpty($uri.UserInfo) -or
    -not [string]::IsNullOrEmpty($uri.Query) -or
    -not [string]::IsNullOrEmpty($uri.Fragment)) {
    throw 'Remote DEV manifest URL must be an HTTPS /api/v1/dev/channel/remote endpoint.'
}
$client = Join-Path $root 'REvolutionClient_dev.exe'
if (-not (Test-Path -LiteralPath $client -PathType Leaf)) {
    throw 'REvolutionClient_dev.exe was not found next to this script.'
}
$env:REVOLUTION_CLIENT_DEV_MANIFEST_URL = $uri.AbsoluteUri
Start-Process -FilePath $client -WorkingDirectory $root
