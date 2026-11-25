param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [switch]$AsLibrary
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()
$assetsRoot = Join-Path $ProjectRoot 'Assets'
$standardGuidPattern = '^[0-9a-f]{32}$'
$guidByValue = @{}
$metaFileCount = 0
$standardGuidCount = 0
$legacyOpaqueGuidCount = 0
$serializedGuidReferenceCount = 0
$engineEvidenceByPath = @{}
$engineEvidencePath = Join-Path $ProjectRoot 'TestResults/meta-guid-evidence.json'
if (Test-Path -LiteralPath $engineEvidencePath -PathType Leaf) {
    try {
        $evidence = Get-Content -LiteralPath $engineEvidencePath -Raw | ConvertFrom-Json
        $versionText = Get-Content -LiteralPath (Join-Path $ProjectRoot 'ProjectSettings/ProjectVersion.txt') -Raw
        $version = [regex]::Match($versionText, '(?m)^m_EditorVersion:\s*(\S+)').Groups[1].Value
        if ($evidence.schemaVersion -eq 1 -and $evidence.engineVersion -ceq $version) {
            foreach ($item in $evidence.metas) {
                if (-not [string]::IsNullOrWhiteSpace($item.path)) { $engineEvidenceByPath[$item.path] = $item }
            }
        }
    }
    catch { $engineEvidenceByPath.Clear() }
}
$externalAssetPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$serializedTextExtensions = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($extension in @(
    '.asset',
    '.prefab',
    '.unity',
    '.mat',
    '.controller',
    '.overrideController',
    '.anim')) {
    $serializedTextExtensions.Add($extension) | Out-Null
}

function Add-Failure([string]$Message) {
    $failures.Add($Message)
}

function Get-RepositoryRelativePath([string]$FullPath) {
    $root = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\', '/')
    $path = [IO.Path]::GetFullPath($FullPath)
    return $path.Substring($root.Length + 1).Replace('\', '/')
}

function Test-IsIgnoredAssetEntry([string]$RelativePath) {
    foreach ($segment in $RelativePath.Split('/')) {
        if ($segment.StartsWith('.') -or $segment.EndsWith('~')) {
            return $true
        }
    }
    return $false
}

function Get-MetaGuid([string]$MetaPath) {
    $text = Get-Content -Raw -LiteralPath $MetaPath
    $matches = [regex]::Matches($text, '(?m)^\s*guid:\s*(\S+)\s*$')
    if ($matches.Count -ne 1) {
        Add-Failure "GUID 字段数量必须为 1：$(Get-RepositoryRelativePath $MetaPath)（实际 $($matches.Count)）"
        return $null
    }
    return $matches[0].Groups[1].Value
}

function Test-IsStructurallyValidLegacyOpaqueGuid([string]$Guid) {
    try {
        $decoded = [Convert]::FromBase64String($Guid)
        return $decoded.Length -eq 41
    }
    catch {
        return $false
    }
}

function Test-MatchesHistoricalMetaGuid([string]$RelativePath, [string]$Guid) {
    # 被删除后从 Git 原样恢复的资源仍是同一个已导入资源，不属于“新造不透明 GUID”。
    # 只接受路径和 guid: 值都能在任一历史版本中精确匹配的情况。
    $revisions = @(& git -C $ProjectRoot log --all --format='%H' -- $RelativePath)
    if ($LASTEXITCODE -ne 0) { return $false }

    foreach ($revision in $revisions) {
        if ([string]::IsNullOrWhiteSpace($revision)) { continue }
        $objectSpec = "${revision}:$RelativePath"
        $historicalText = @(& git -C $ProjectRoot show $objectSpec 2>$null) -join "`n"
        if ($LASTEXITCODE -ne 0) { continue }

        $matches = [regex]::Matches($historicalText, '(?m)^\s*guid:\s*(\S+)\s*$')
        if ($matches.Count -eq 1 -and $matches[0].Groups[1].Value -ceq $Guid) {
            return $true
        }
    }
    return $false
}

function Add-NewMetaPaths([System.Collections.Generic.HashSet[string]]$Destination, [string[]]$Paths) {
    foreach ($path in $Paths) {
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $normalized = $path.Replace('\', '/')
        if ($normalized.EndsWith('.meta', [StringComparison]::OrdinalIgnoreCase)) {
            $Destination.Add($normalized) | Out-Null
        }
    }
}

function Test-MatchesMovedMeta([string]$RelativePath) {
    # A move keeps the existing engine-owned identity. Require both old paths
    # to be absent and the entire meta payload to match HEAD (ignoring EOL).
    # Merely reusing a historical GUID is insufficient evidence of a move.
    $deletedPaths = @(& git -C $ProjectRoot -c core.quotepath=false diff `
        --no-renames --name-only --diff-filter=D HEAD -- Assets)
    if ($LASTEXITCODE -ne 0) { return $false }
    $currentText = (Get-Content -LiteralPath (Join-Path $ProjectRoot $RelativePath) -Raw).
        Replace("`r`n", "`n").TrimEnd([char]10)
    foreach ($oldPath in $deletedPaths) {
        if (-not $oldPath.EndsWith('.meta', [StringComparison]::OrdinalIgnoreCase)) { continue }
        $oldMeta = Join-Path $ProjectRoot $oldPath
        $oldAsset = $oldMeta.Substring(0, $oldMeta.Length - 5)
        if ((Test-Path -LiteralPath $oldMeta) -or (Test-Path -LiteralPath $oldAsset)) { continue }
        $historicalText = @(& git -C $ProjectRoot show "HEAD:$oldPath" 2>$null) -join "`n"
        if ($LASTEXITCODE -eq 0 -and $historicalText -ceq $currentText) { return $true }
    }
    return $false
}

function Test-MatchesEngineEvidence([string]$RelativePath) {
    $item = $engineEvidenceByPath[$RelativePath]
    if ($null -eq $item -or [string]::IsNullOrWhiteSpace($item.assetDatabaseGuid) -or
        $item.assetDatabaseGuid -eq '00000000000000000000000000000000' -or
        $item.roundTripPath -cne $RelativePath.Substring(0, $RelativePath.Length - 5)) { return $false }
    $hash = (Get-FileHash -LiteralPath (Join-Path $ProjectRoot $RelativePath) -Algorithm SHA256).Hash
    return $hash -eq $item.metaSha256
}

if (-not (Test-Path -LiteralPath $assetsRoot -PathType Container)) {
    Add-Failure '缺少 Assets 目录。'
}
else {
    $externalAssetManifestPath = Join-Path $ProjectRoot 'external-assets.manifest.json'
    if (Test-Path -LiteralPath $externalAssetManifestPath -PathType Leaf) {
        try {
            $externalAssetManifest = Get-Content -Raw -LiteralPath $externalAssetManifestPath | ConvertFrom-Json
            foreach ($externalAsset in @($externalAssetManifest.assets)) {
                if (-not [string]::IsNullOrWhiteSpace($externalAsset.path)) {
                    $normalizedExternalAssetPath = $externalAsset.path.Replace('\', '/')
                    $externalAssetPaths.Add($normalizedExternalAssetPath) | Out-Null
                    $externalMetaPath = Join-Path $ProjectRoot ($normalizedExternalAssetPath + '.meta')
                    if (-not (Test-Path -LiteralPath $externalMetaPath -PathType Leaf)) {
                        Add-Failure "外部资源缺少必须跟踪的 .meta：$normalizedExternalAssetPath.meta"
                    }
                }
            }
        }
        catch {
            Add-Failure "外部资源清单无法解析：$($_.Exception.Message)"
        }
    }

    $newMetaPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    $untrackedPaths = @(& git -C $ProjectRoot ls-files --others --exclude-standard -- Assets)
    if ($LASTEXITCODE -eq 0) {
        Add-NewMetaPaths $newMetaPaths $untrackedPaths
    }
    else {
        Add-Failure '无法读取 Git 未跟踪文件，不能判定新增 .meta。'
    }

    $unstagedAddedPaths = @(& git -C $ProjectRoot diff --name-only --diff-filter=A -- Assets)
    if ($LASTEXITCODE -eq 0) {
        Add-NewMetaPaths $newMetaPaths $unstagedAddedPaths
    }
    else {
        Add-Failure '无法读取 Git 未暂存新增文件，不能判定新增 .meta。'
    }

    $stagedAddedPaths = @(& git -C $ProjectRoot diff --cached --name-only --diff-filter=A -- Assets)
    if ($LASTEXITCODE -eq 0) {
        Add-NewMetaPaths $newMetaPaths $stagedAddedPaths
    }
    else {
        Add-Failure '无法读取 Git 已暂存新增文件，不能判定新增 .meta。'
    }

    & git -C $ProjectRoot rev-parse --verify 'HEAD^' *> $null
    if ($LASTEXITCODE -eq 0) {
        $headAddedPaths = @(& git -C $ProjectRoot diff-tree --no-commit-id --name-only --diff-filter=A -r 'HEAD^' HEAD -- Assets)
        if ($LASTEXITCODE -eq 0) {
            Add-NewMetaPaths $newMetaPaths $headAddedPaths
        }
        else {
            Add-Failure '无法读取当前提交新增文件，不能校验刚提交的 .meta。'
        }
    }

    $entries = @(Get-ChildItem -LiteralPath $assetsRoot -Recurse -Force)
    foreach ($entry in $entries) {
        $relativePath = Get-RepositoryRelativePath $entry.FullName
        if (Test-IsIgnoredAssetEntry $relativePath) { continue }

        if (-not $entry.Name.EndsWith('.meta', [StringComparison]::OrdinalIgnoreCase)) {
            $expectedMetaPath = "$($entry.FullName).meta"
            if (-not (Test-Path -LiteralPath $expectedMetaPath -PathType Leaf)) {
                Add-Failure "资源或目录缺少 .meta：$relativePath"
            }

            # 团结遗留 opaque GUID 可以继续保留在原始 .meta 中，但不能手写进
            # YAML 对象引用；AssetDatabase 无法从这种文本引用提取 GUID。
            if (-not $entry.PSIsContainer -and
                $serializedTextExtensions.Contains($entry.Extension)) {
                $lineNumber = 0
                foreach ($line in Get-Content -LiteralPath $entry.FullName) {
                    $lineNumber++
                    foreach ($match in [regex]::Matches(
                        $line,
                        'guid:\s*([^,\s}\]]+)')) {
                        $serializedGuidReferenceCount++
                        $serializedGuid = $match.Groups[1].Value
                        if ($serializedGuid -cnotmatch $standardGuidPattern) {
                            Add-Failure (
                                "文本资源包含不可解析的 GUID 引用：" +
                                "$relativePath`:$lineNumber（$serializedGuid）；" +
                                "请由 AssetDatabase 序列化标准引用，或改用类型化路径字段。")
                        }
                    }
                }
            }
            continue
        }

        $metaFileCount++
        $assetPath = $entry.FullName.Substring(0, $entry.FullName.Length - 5)
        $assetRelativePath = $relativePath.Substring(0, $relativePath.Length - 5)
        if (-not (Test-Path -LiteralPath $assetPath) -and -not $externalAssetPaths.Contains($assetRelativePath)) {
            Add-Failure "孤立 .meta（对应资源不存在）：$relativePath"
        }

        $guid = Get-MetaGuid $entry.FullName
        if ([string]::IsNullOrWhiteSpace($guid)) { continue }

        $isStandard = $guid -cmatch $standardGuidPattern
        $isLegacyOpaque = -not $isStandard -and (Test-IsStructurallyValidLegacyOpaqueGuid $guid)
        if ($isStandard) {
            $standardGuidCount++
            if ($guid -eq '00000000000000000000000000000000') {
                Add-Failure "GUID 不得为全零：$relativePath"
            }
        }
        elseif ($isLegacyOpaque) {
            $legacyOpaqueGuidCount++
            if ($newMetaPaths.Contains($relativePath)) {
                if (-not (Test-MatchesEngineEvidence $relativePath) -and
                    -not (Test-MatchesHistoricalMetaGuid $relativePath $guid) -and
                    -not (Test-MatchesMovedMeta $relativePath)) {
                    Add-Failure "新增 .meta 不透明 GUID 缺少引擎权威回执；离线新建须用 32 位 hex，已有引擎身份须保留并运行 MetaGuidValidator：$relativePath"
                }
            }
        }
        else {
            Add-Failure "GUID 格式无效：$relativePath（既不是 32 位小写 hex，也不是当前仓库遗留不透明格式）"
        }

        if ($guidByValue.ContainsKey($guid)) {
            Add-Failure "GUID 重复：$relativePath 与 $($guidByValue[$guid])"
        }
        else {
            $guidByValue[$guid] = $relativePath
        }
    }
}

$result = [pscustomobject]@{
    Passed = $failures.Count -eq 0
    MetaFileCount = $metaFileCount
    StandardGuidCount = $standardGuidCount
    LegacyOpaqueGuidCount = $legacyOpaqueGuidCount
    SerializedGuidReferenceCount = $serializedGuidReferenceCount
    Failures = @($failures)
}

if ($AsLibrary) {
    return $result
}

if (-not $result.Passed) {
    Write-Host "Meta GUID verification failed. Failures: $($failures.Count)."
    for ($index = 0; $index -lt $failures.Count; $index++) {
        $failureNumber = $index + 1
        Write-Error "[$failureNumber/$($failures.Count)] $($failures[$index])" -ErrorAction Continue
    }
    exit 1
}

Write-Host "Meta GUID verification passed. Meta files: $metaFileCount; standard hex: $standardGuidCount; legacy opaque: $legacyOpaqueGuidCount; serialized references: $serializedGuidReferenceCount."
