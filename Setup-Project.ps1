#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$AssetArchivePath,
    [string]$RuntimeMediaArchivePath,
    [switch]$OpenEditor,
    [switch]$RestoreEngineResources,
    [string]$EditorPath
)

$ErrorActionPreference = 'Stop'
$releaseArchiveName = 'assets-2026.11.25.zip'
$releaseBaseUrl = 'https://github.com/manibookmark/Yijuanwanjie/releases/download/v2026.11.25'
$releaseArchiveSha256 = 'ccb671b40dc2aadd38f0d9c43efeef8627134fcc266d1f08e822cee67c96d443'
$projectRoot = $PSScriptRoot

function Test-InstalledAssets([object[]]$Assets) {
    foreach ($asset in $Assets) {
        $localPath = Join-Path $projectRoot $asset.path
        if (-not (Test-Path -LiteralPath $localPath -PathType Leaf) -or
            (Get-Item -LiteralPath $localPath).Length -ne $asset.bytes -or
            (Get-FileHash -LiteralPath $localPath -Algorithm SHA256).Hash -ine $asset.sha256) {
            return $false
        }
    }
    return $true
}

function Find-TuanjieEditor {
    if ($EditorPath) {
        return (Resolve-Path -LiteralPath $EditorPath).Path
    }
    if ($env:TUANJIE_EDITOR_PATH) {
        return (Resolve-Path -LiteralPath $env:TUANJIE_EDITOR_PATH).Path
    }

    $editorVersion = '2022.3.62t14'
    $hubRoots = @(Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Tuanjie\Hub\Editor')
    if ($env:APPDATA) {
        $secondaryPathFile = Join-Path $env:APPDATA 'TuanjieHub\secondaryInstallPath.json'
        if (Test-Path -LiteralPath $secondaryPathFile -PathType Leaf) {
            try {
                $secondaryRoot = Get-Content -LiteralPath $secondaryPathFile -Raw -Encoding UTF8 | ConvertFrom-Json
                if ($secondaryRoot -is [string] -and $secondaryRoot) { $hubRoots += $secondaryRoot }
            }
            catch {
                Write-Verbose 'The secondary Tuanjie Hub installation preference could not be read.'
            }
        }
    }

    foreach ($hubRoot in $hubRoots) {
        $candidate = Join-Path $hubRoot "$editorVersion\Editor\Tuanjie.exe"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw 'Install Tuanjie 1.10.2 (2022.3.62t14) or pass -EditorPath to its Tuanjie.exe.'
}

function Import-TuanjieResources([string]$Editor, [string]$RequiredFont) {
    $logName = 'Yijuanwanjie-engine-setup-' + [Guid]::NewGuid().ToString('N') + '.log'
    $logPath = Join-Path ([IO.Path]::GetTempPath()) $logName
    $arguments = @(
        '-batchmode', '-nographics',
        '-projectPath', ('"{0}"' -f $projectRoot),
        '-executeMethod', 'WanjieScroll.EditorTools.PublicProjectSetup.RestoreEngineResources',
        '-logFile', ('"{0}"' -f $logPath)
    )
    Write-Host 'Importing TextMesh Pro Essential Resources from Tuanjie...'
    $process = Start-Process -FilePath $Editor -WorkingDirectory $projectRoot -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0 -or
        -not (Test-Path -LiteralPath $RequiredFont -PathType Leaf) -or
        -not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
        throw "TextMesh Pro Essential Resources import failed (Editor exit $($process.ExitCode)). See $logPath"
    }
    Write-Host "TextMesh Pro Essential Resources restored. Editor log: $logPath"
}

function Restore-ReleaseAssets {
    param(
        [string]$ManifestPath,
        [string]$ArchiveName,
        [string]$ArchiveSha256,
        [string]$OfflineArchivePath,
        [object[]]$Parts
    )

    if ($ArchiveName -notmatch '\A[A-Za-z0-9][A-Za-z0-9._-]*\.zip\z' -or $ArchiveSha256 -notmatch '\A[a-f0-9]{64}\z') {
        throw "An archive name and SHA-256 are required for $ManifestPath."
    }
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (Test-InstalledAssets $manifest.assets) { return }

    $tempRoot = (Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).Path
    $tempName = 'Yijuanwanjie-assets-' + [Guid]::NewGuid().ToString('N')
    $workDirectory = Join-Path $tempRoot $tempName
    New-Item -ItemType Directory -Path $workDirectory | Out-Null
    try {
        if ($OfflineArchivePath) {
            $archive = (Resolve-Path -LiteralPath $OfflineArchivePath).Path
        }
        else {
            $archive = Join-Path $workDirectory $ArchiveName
            . (Join-Path $projectRoot 'tools/assets/Receive-ReleaseArchive.ps1')
            Receive-ReleaseArchive -BaseUrl $releaseBaseUrl -AssetName $ArchiveName `
                -Destination $archive -Sha256 $ArchiveSha256 -Parts $Parts
        }

        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ine $ArchiveSha256) {
            throw "Archive SHA-256 does not match the pinned release. Download $ArchiveName from the v2026.11.25 release and retry."
        }

        Write-Host "Verifying resources from $ArchiveName..."
        $sourceRoot = Join-Path $workDirectory 'unpacked'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($archive, $sourceRoot)
        $restoreScript = Join-Path $projectRoot 'tools/assets/restore-external-assets.ps1'
        & $restoreScript -SourceRoot $sourceRoot -ProjectRoot $projectRoot -ManifestPath $ManifestPath
    }
    finally {
        $resolvedWorkDirectory = Get-Item -LiteralPath $workDirectory
        if ($resolvedWorkDirectory.Parent.FullName -eq $tempRoot.TrimEnd('\', '/') -and
            $resolvedWorkDirectory.Name -eq $tempName) {
            Remove-Item -LiteralPath $resolvedWorkDirectory.FullName -Recurse -Force
        }
    }
}

$externalPartsPath = Join-Path $projectRoot 'AssetsReleaseParts.json'
$externalParts = @()
if (Test-Path -LiteralPath $externalPartsPath -PathType Leaf) {
    $externalParts = [object[]](Get-Content -LiteralPath $externalPartsPath -Raw -Encoding UTF8 | ConvertFrom-Json)
}
Restore-ReleaseAssets -ManifestPath (Join-Path $projectRoot 'external-assets.manifest.json') `
    -ArchiveName $releaseArchiveName -ArchiveSha256 $releaseArchiveSha256 `
    -OfflineArchivePath $AssetArchivePath -Parts $externalParts

$runtimeManifestPath = Join-Path $projectRoot 'runtime-media.manifest.json'
if (Test-Path -LiteralPath $runtimeManifestPath -PathType Leaf) {
    $runtimeReleasePath = Join-Path $projectRoot 'RuntimeMediaRelease.json'
    $runtimeRelease = Get-Content -LiteralPath $runtimeReleasePath -Raw -Encoding UTF8 | ConvertFrom-Json
    Restore-ReleaseAssets -ManifestPath $runtimeManifestPath -ArchiveName $runtimeRelease.assetName `
        -ArchiveSha256 $runtimeRelease.sha256 -OfflineArchivePath $RuntimeMediaArchivePath -Parts $runtimeRelease.parts
}
elseif ($RuntimeMediaArchivePath) {
    throw 'This checkout has no runtime-media.manifest.json for -RuntimeMediaArchivePath.'
}

Write-Host 'External resources are ready.'
$requiredFont = Join-Path $projectRoot 'Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset'
$needsEngineResources = -not (Test-Path -LiteralPath $requiredFont -PathType Leaf)
if ($OpenEditor -or ($RestoreEngineResources -and $needsEngineResources)) {
    $editor = Find-TuanjieEditor
    if ($needsEngineResources) { Import-TuanjieResources -Editor $editor -RequiredFont $requiredFont }
}
if ($OpenEditor) {
    $loadingScene = (Resolve-Path -LiteralPath (Join-Path $projectRoot 'Assets/Scenes/Loading.unity')).Path
    Start-Process -FilePath $editor -WorkingDirectory $projectRoot -ArgumentList @('-openfile', ('"{0}"' -f $loadingScene))
    Write-Host 'Click Play after the Editor finishes importing.'
}
elseif ($needsEngineResources -and -not $RestoreEngineResources) {
    Write-Host 'Run again with -OpenEditor to restore TextMesh Pro and open the game, or use -RestoreEngineResources to restore TextMesh Pro without opening the Editor.'
}
else {
    Write-Host 'Open Assets/Scenes/Loading.unity in Tuanjie 1.10.2, then click Play. Use -OpenEditor to open it now.'
}
