[CmdletBinding()]
param([string]$OutputPath)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
if (-not $OutputPath) {
    $OutputPath = Join-Path $root 'Builds/Launcher/WanjieScroll-Play.exe'
}
$exe = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The Windows x64 .NET Framework C# compiler is unavailable.'
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($exe)) | Out-Null

$arguments = @(
    '/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001',
    "/out:$exe",
    ('/resource:{0},Play.ps1' -f (Join-Path $root 'Play.ps1')),
    ('/resource:{0},PlayableRelease.json' -f (Join-Path $root 'PlayableRelease.json')),
    ('/resource:{0},Receive-ReleaseArchive.ps1' -f (Join-Path $root 'tools/assets/Receive-ReleaseArchive.ps1')),
    (Join-Path $PSScriptRoot 'WindowsLauncher.cs')
)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Windows launcher compilation failed with exit code $LASTEXITCODE."
}
Get-Item -LiteralPath $exe
