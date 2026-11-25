function Receive-ReleaseArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][uri]$BaseUrl,
        [Parameter(Mandatory)][string]$AssetName,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$Sha256,
        [object[]]$Parts
    )

    $ErrorActionPreference = 'Stop'
    $namePattern = '\A[A-Za-z0-9][A-Za-z0-9._-]*\z'
    $hashPattern = '\A[0-9A-Fa-f]{64}\z'
    if (-not $BaseUrl.IsAbsoluteUri -or $BaseUrl.Scheme -ne 'https' -or
        $BaseUrl.Query -or $BaseUrl.Fragment -or $BaseUrl.UserInfo) {
        throw 'Use an HTTPS release download base URL without a query, fragment or credentials.'
    }
    if ($AssetName -notmatch $namePattern -or $Sha256 -notmatch $hashPattern) {
        throw 'The release archive descriptor has an invalid name or SHA-256.'
    }

    $partNames = @{}
    foreach ($part in $Parts) {
        $partBytes = 0L
        if ($part.name -notmatch $namePattern -or $part.sha256 -notmatch $hashPattern -or
            -not [long]::TryParse([string]$part.bytes, [ref]$partBytes) -or $partBytes -le 0) {
            throw 'Each release part must specify a valid name, positive byte count, and SHA-256.'
        }
        if ($partNames.ContainsKey($part.name)) {
            throw "Duplicate release part '$($part.name)'."
        }
        $partNames.Add($part.name, $true)
    }

    $base = $BaseUrl.AbsoluteUri.TrimEnd('/')
    $target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)
    $pending = $target + '.' + [Guid]::NewGuid().ToString('N') + '.partial'
    $pendingPart = $pending + '.part'
    $oldProgress = $ProgressPreference
    $oldProtocol = [Net.ServicePointManager]::SecurityProtocol
    try {
        $ProgressPreference = 'SilentlyContinue'
        if ($PSVersionTable.PSVersion.Major -eq 5) {
            [Net.ServicePointManager]::SecurityProtocol = $oldProtocol -bor [Net.SecurityProtocolType]::Tls12
        }

        if ($partNames.Count -gt 0) {
            $archive = [IO.File]::Open($pending, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try {
                foreach ($part in $Parts) {
                    $name = $part.name
                    Write-Host "Downloading $name..."
                    Invoke-WebRequest -UseBasicParsing -Uri "$base/$name" -OutFile $pendingPart
                    $stream = [IO.File]::OpenRead($pendingPart)
                    try {
                        if ($stream.Length -ne [long]$part.bytes) {
                            throw "Release part '$name' does not match its expected byte count."
                        }
                        $partHash = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash
                        if ($partHash -ne $part.sha256) {
                            throw "Release part '$name' does not match its expected SHA-256."
                        }
                        $stream.Position = 0
                        $stream.CopyTo($archive)
                    }
                    finally {
                        $stream.Dispose()
                    }
                    [IO.File]::Delete($pendingPart)
                }
            }
            finally {
                $archive.Dispose()
            }
        }
        else {
            Write-Host "Downloading $AssetName..."
            Invoke-WebRequest -UseBasicParsing -Uri "$base/$AssetName" -OutFile $pending
        }

        $wholeHash = (Get-FileHash -LiteralPath $pending -Algorithm SHA256).Hash
        if ($wholeHash -ne $Sha256) {
            throw "Release archive '$AssetName' does not match its expected SHA-256."
        }
        [IO.File]::Move($pending, $target)
    }
    finally {
        $ProgressPreference = $oldProgress
        [Net.ServicePointManager]::SecurityProtocol = $oldProtocol
        [IO.File]::Delete($pendingPart)
        [IO.File]::Delete($pending)
    }
}
