param(
    [Parameter(Mandatory = $true)]
    [string]$SourceRoot,
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$ManifestPath
)

$ErrorActionPreference = 'Stop'
$resolvedSourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$resolvedProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
if (-not $ManifestPath) {
    $ManifestPath = Join-Path $resolvedProjectRoot 'external-assets.manifest.json'
}
$manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $ManifestPath | ConvertFrom-Json

$verifiedFiles = foreach ($asset in $manifest.assets) {
    $relativePath = $asset.path.Replace('/', [IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::IsPathRooted($relativePath) -or ($relativePath -split '[\\/]' -contains '..') -or
        $relativePath.Contains(':') -or $relativePath.EndsWith('.meta', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Invalid archive resource path: $($asset.path)"
    }
    $sourcePath = Join-Path $resolvedSourceRoot $relativePath
    $destinationPath = Join-Path $resolvedProjectRoot $relativePath

    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Archive resource is missing: $($asset.path)"
    }
    if ($null -ne $asset.bytes -and (Get-Item -LiteralPath $sourcePath).Length -ne $asset.bytes) {
        throw "Resource size does not match the manifest: $($asset.path)"
    }

    $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $sourcePath).Hash.ToLowerInvariant()
    if ($sourceHash -ne $asset.sha256.ToLowerInvariant()) {
        throw "Resource SHA-256 does not match the manifest: $($asset.path)"
    }

    [pscustomobject]@{ RelativePath = $asset.path; Source = $sourcePath; Destination = $destinationPath }
}

foreach ($file in $verifiedFiles) {
    $destinationDirectory = Split-Path -Parent $file.Destination
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    Copy-Item -LiteralPath $file.Source -Destination $file.Destination -Force
    Write-Host "Restored $($file.RelativePath)"
}
