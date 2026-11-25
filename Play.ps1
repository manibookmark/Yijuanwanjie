[CmdletBinding()]
param(
    [string]$PlayerArchivePath,
    [switch]$InstallOnly
)

$ErrorActionPreference = 'Stop'
$release = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'PlayableRelease.json') -Raw | ConvertFrom-Json
if ($release.tag -cnotmatch '^v[0-9][0-9A-Za-z._-]*$' -or
    $release.assetName -cnotmatch '^[0-9A-Za-z._-]+\.zip$' -or
    $release.sha256 -notmatch '^[0-9A-Fa-f]{64}$' -or
    $release.executable -notmatch '\.exe$') {
    throw 'PlayableRelease.json must specify a release tag, ZIP asset name, SHA-256, and executable path.'
}

function Get-PlayerFilePath {
    param([string]$Root, [string]$EntryName)

    $parts = $EntryName -split '[/\\]'
    foreach ($part in $parts) {
        if (-not $part -or $part -match '[<>:"|?*\x00-\x1f]|[ .]$' -or
            $part -match '^(CON|PRN|AUX|NUL|COM[0-9\u00B9\u00B2\u00B3]|LPT[0-9\u00B9\u00B2\u00B3]|CONIN\$|CONOUT\$)(\.|$)') {
            throw "Invalid player path '$EntryName'."
        }
    }

    $prefix = $Root.TrimEnd('\') + '\'
    $path = [IO.Path]::GetFullPath($prefix + ($parts -join '\'))
    if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Player path escapes its install directory: '$EntryName'."
    }
    return $path
}

function Assert-PlayerArchiveHash {
    param([IO.Stream]$Stream, [string]$ExpectedHash)

    $actual = (Get-FileHash -InputStream $Stream -Algorithm SHA256).Hash
    if ($actual -ne $ExpectedHash) {
        throw "Player archive SHA-256 mismatch. Expected $ExpectedHash, received $actual."
    }
    $Stream.Position = 0
}

if (-not $env:LOCALAPPDATA) {
    throw 'The Windows player requires a LocalAppData directory.'
}

$cache = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Yijuanwanjie\Player'))
$hash = $release.sha256.ToLowerInvariant()
$install = Join-Path $cache "$($release.tag)-$hash"
$player = Get-PlayerFilePath -Root $install -EntryName $release.executable
$archiveStream = $null

try {
    if ($PlayerArchivePath) {
        $localArchive = Get-Item -LiteralPath $PlayerArchivePath
        $archiveStream = [IO.File]::OpenRead($localArchive.FullName)
        Assert-PlayerArchiveHash -Stream $archiveStream -ExpectedHash $hash
    }

    if (-not [IO.File]::Exists($player)) {
        $staging = Join-Path $cache ('.install-' + [Guid]::NewGuid().ToString('N'))
        $payload = Join-Path $staging 'Player'
        [IO.Directory]::CreateDirectory($payload) | Out-Null

        try {
            if ($null -eq $archiveStream) {
                $download = Join-Path $staging 'player.zip'
                $releaseBase = "https://github.com/manibookmark/Yijuanwanjie/releases/download/$($release.tag)"
                . (Join-Path $PSScriptRoot 'tools/assets/Receive-ReleaseArchive.ps1')
                Receive-ReleaseArchive -BaseUrl $releaseBase -AssetName $release.assetName -Destination $download `
                    -Sha256 $hash -Parts $release.parts
                $archiveStream = [IO.File]::OpenRead($download)
            }

            Write-Host "Installing $($release.tag)..."
            Add-Type -AssemblyName System.IO.Compression
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $archive = [IO.Compression.ZipArchive]::new($archiveStream, [IO.Compression.ZipArchiveMode]::Read, $true)
            try {
                foreach ($entry in $archive.Entries) {
                    $entryPath = $entry.FullName.TrimEnd('/', '\')
                    $destination = Get-PlayerFilePath -Root $payload -EntryName $entryPath
                    if ($entryPath.Length -ne $entry.FullName.Length) {
                        [IO.Directory]::CreateDirectory($destination) | Out-Null
                        continue
                    }
                    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
                    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $false)
                }
            }
            finally {
                $archive.Dispose()
            }

            $stagedPlayer = Get-PlayerFilePath -Root $payload -EntryName $release.executable
            if (-not [IO.File]::Exists($stagedPlayer)) {
                throw "The player archive does not contain '$($release.executable)'."
            }

            try {
                [IO.Directory]::Move($payload, $install)
            }
            catch [IO.IOException] {
                if (-not [IO.File]::Exists($player)) {
                    throw
                }
            }
        }
        finally {
            if ($null -ne $archiveStream) {
                $archiveStream.Dispose()
                $archiveStream = $null
            }
            if ([IO.Directory]::Exists($staging) -and
                [IO.Path]::GetFullPath($staging).StartsWith(($cache + '\'), [StringComparison]::OrdinalIgnoreCase)) {
                [IO.Directory]::Delete($staging, $true)
            }
        }
    }
}
finally {
    if ($null -ne $archiveStream) {
        $archiveStream.Dispose()
    }
}

if ($InstallOnly) {
    Write-Output $player
    return
}

Start-Process -FilePath $player -WorkingDirectory ([IO.Path]::GetDirectoryName($player))
