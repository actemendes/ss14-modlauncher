# Shared release planning and immutable mod-artifact handling. Dot-sourcing has no side effects.
Set-StrictMode -Version Latest

function Assert-ReleaseVersion([string]$Value) {
    if ($Value.Length -gt 128 -or $Value -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw "Expected a stable three-part version, got '$Value'." }
    try { [void][version]$Value } catch { throw "Invalid version '$Value'." }
}

function Assert-ReleaseRepository([string]$Value, [switch]$AllowEmpty) {
    if ($AllowEmpty -and [string]::IsNullOrWhiteSpace($Value)) { return }
    if ($Value -notmatch '^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100}$' -or $Value.Split('/')[1].StartsWith('.') -or $Value.Split('/')[1].EndsWith('.') -or $Value.Split('/')[1].Contains('..')) {
        throw "Invalid GitHub repository '$Value'."
    }
}

function Get-ReleaseContainedPath([string]$Root, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':') -or @($Relative -split '[/\\]' | Where-Object { $_ -in @('', '.', '..') }).Count) {
        throw "Unsafe relative path '$Relative'."
    }
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath((Join-Path $fullRoot $Relative))
    if (-not $full.StartsWith($fullRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Path escapes its root.' }
    for ($current = $full; $current; $current = [IO.Path]::GetDirectoryName($current)) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked paths are not supported: $current" }
        if ($current -eq $fullRoot) { break }
    }
    return $full
}

function Get-ReleasePlan {
    param([Parameter(Mandatory)][string]$ProjectRoot, [string]$ReleaseVersion = '', [AllowEmptyString()][string]$Repository, [switch]$OverrideRepository)
    $config = Get-Content -LiteralPath (Join-Path $ProjectRoot 'release/release.json') -Raw | ConvertFrom-Json -AsHashtable
    $catalog = @(Get-Content -LiteralPath (Join-Path $ProjectRoot 'catalog/mods.json') -Raw | ConvertFrom-Json -AsHashtable)
    $ledger = Get-Content -LiteralPath (Join-Path $ProjectRoot 'release/mod-artifacts.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($config.schemaVersion -ne 1 -or $ledger.schemaVersion -ne 1) { throw 'Unsupported release configuration or artifact ledger schema.' }
    if ($ReleaseVersion) { $config.releaseVersion = $ReleaseVersion }
    if ($OverrideRepository) { $config.repository = $Repository }
    foreach ($value in @($config.releaseVersion, $config.launcherVersion, $config.launcherReleaseVersion, $config.bootstrapVersion)) { Assert-ReleaseVersion $value }
    Assert-ReleaseRepository $config.repository -AllowEmpty
    if ($config.mode -cnotin @('Full', 'ModsOnly')) { throw 'Release mode must be Full or ModsOnly.' }
    if ($catalog.Count -eq 0) { throw 'The mod catalog must not be empty.' }
    $locks = @{}
    foreach ($artifact in @($ledger.artifacts)) {
        foreach ($value in @($artifact.version, $artifact.sourceRelease)) { Assert-ReleaseVersion $value }
        Assert-ReleaseRepository $artifact.repository
        if ($artifact.id -notmatch '^[a-z0-9][a-z0-9-]*$' -or $artifact.file -notmatch '^[A-Za-z0-9][A-Za-z0-9_.-]*\.Mod\.dll$' -or $artifact.file.Contains('..') -or $artifact.sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'An artifact ledger entry is invalid.' }
        $key = "$($artifact.id)@$($artifact.version)"
        if ($locks.ContainsKey($key)) { throw "Duplicate published mod version: $key" }
        $locks[$key] = $artifact
    }
    $ids = @{}; $files = @{}; $mods = @(); $minimum = [version]'0.0.0'
    foreach ($mod in $catalog) {
        Assert-ReleaseVersion $mod.version
        Assert-ReleaseVersion $mod.minLauncherVersion
        if ($mod.id -notmatch '^[a-z0-9][a-z0-9-]*$' -or $mod.file -notmatch '^[A-Za-z0-9][A-Za-z0-9_.-]*\.Mod\.dll$' -or $mod.file.Contains('..') -or $ids.ContainsKey($mod.id) -or $files.ContainsKey($mod.file)) { throw 'Catalog mod IDs and filenames must be safe and unique.' }
        $ids[$mod.id] = $true; $files[$mod.file] = $true
        if ([version]$mod.minLauncherVersion -gt [version]$config.launcherVersion) { throw "$($mod.id) requires a launcher newer than the release provides." }
        if ([version]$mod.minLauncherVersion -gt $minimum) { $minimum = [version]$mod.minLauncherVersion }
        $key = "$($mod.id)@$($mod.version)"
        $artifact = if ($locks.ContainsKey($key)) { $locks[$key] } else { $null }
        if ($artifact -and $artifact.file -cne $mod.file) { throw "Published filename is immutable: $key" }
        if (-not $artifact) {
            foreach ($prior in @($ledger.artifacts | Where-Object { $_.id -eq $mod.id })) {
                if ([version]$mod.version -le [version]$prior.version) { throw "A new source build of $($mod.id) must use a version newer than $($prior.version)." }
            }
        }
        $project = if ($mod.ContainsKey('project')) { $mod.project } else { "$($mod.source)/$([IO.Path]::GetFileName($mod.source)).csproj" }
        $projectPath = Get-ReleaseContainedPath $ProjectRoot $project
        $mods += @{ Id = $mod.id; Version = $mod.version; MinLauncherVersion = $mod.minLauncherVersion; File = $mod.file; ProjectPath = $projectPath; Artifact = $artifact }
    }
    return @{
        ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot); ReleaseVersion = $config.releaseVersion; LauncherVersion = $config.launcherVersion
        Mode = $config.mode; LauncherReleaseVersion = $config.launcherReleaseVersion
        BootstrapVersion = $config.bootstrapVersion; Repository = $config.repository; MinLauncherVersion = $minimum.ToString(3); Mods = $mods; Ledger = $ledger
    }
}

function Invoke-ReleaseDotNet([Parameter(Mandatory)][string[]]$Arguments) {
    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

function Get-LockedModArtifact {
    param([Parameter(Mandatory)]$Mod, [Parameter(Mandatory)][string]$ProjectRoot, [switch]$Offline)
    $artifact = $Mod.Artifact
    if (-not $artifact) { throw 'A published artifact lock is required.' }
    $cache = Get-ReleaseContainedPath $ProjectRoot ".tools/artifact-cache/$($artifact.sha256.ToLowerInvariant())/$($Mod.File)"
    foreach ($candidate in @($cache, (Get-ReleaseContainedPath $ProjectRoot "payload/$($Mod.File)"), (Get-ReleaseContainedPath $ProjectRoot "dist/mod-assets/$($Mod.File)"))) {
        if ((Test-Path -LiteralPath $candidate -PathType Leaf) -and (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -eq $artifact.sha256) { return $candidate }
    }
    if ($Offline) { throw "No verified local artifact exists for $($Mod.Id) $($Mod.Version)." }
    $url = "https://github.com/$($artifact.repository)/releases/download/v$($artifact.sourceRelease)/$($artifact.file)"
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($cache)) -Force | Out-Null
    $temporary = "$cache.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        Invoke-WebRequest -Uri $url -OutFile $temporary -MaximumRedirection 5 -TimeoutSec 120 -Headers @{ 'User-Agent' = 'SS14ModLauncher-build' }
        if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $artifact.sha256) { throw "Published artifact hash mismatch: $($Mod.Id) $($Mod.Version)." }
        Move-Item -LiteralPath $temporary -Destination $cache -Force
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
    return $cache
}

function Resolve-ReleaseMods {
    param([Parameter(Mandatory)]$Plan, [Parameter(Mandatory)][string]$StagingPath, [switch]$Offline)
    New-Item -ItemType Directory -Path $StagingPath -Force | Out-Null
    $result = @()
    foreach ($mod in $Plan.Mods) {
        $destination = Get-ReleaseContainedPath $StagingPath $mod.File
        if ($mod.Artifact) {
            $source = Get-LockedModArtifact -Mod $mod -ProjectRoot $Plan.ProjectRoot -Offline:$Offline
            Copy-Item -LiteralPath $source -Destination $destination -Force
            $origin = 'published-artifact'
        } else {
            if (-not (Test-Path -LiteralPath $mod.ProjectPath -PathType Leaf)) { throw "Mod source project is missing: $($mod.ProjectPath)" }
            $buildOutput = Get-ReleaseContainedPath $StagingPath "source/$($mod.Id)"
            Invoke-ReleaseDotNet @('build', $mod.ProjectPath, '-c', 'Release', "-p:ModBuildId=$($mod.Id)", "-p:ModBuildVersion=$($mod.Version)", '-o', $buildOutput)
            $source = Get-ReleaseContainedPath $buildOutput $mod.File
            Copy-Item -LiteralPath $source -Destination $destination -Force
            $origin = 'source-build'
        }
        $assembly = [Reflection.AssemblyName]::GetAssemblyName($destination)
        if ($assembly.Name -cne [IO.Path]::GetFileNameWithoutExtension($mod.File) -or $assembly.Version.ToString(3) -ne $mod.Version) {
            throw "Mod assembly identity/version does not match the catalog: $($mod.Id) $($mod.Version)."
        }
        $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        if ($mod.Artifact -and $hash -ne $mod.Artifact.sha256) { throw "Immutable artifact changed: $($mod.Id) $($mod.Version)." }
        $result += @{ Id = $mod.Id; File = $mod.File; Version = $mod.Version; MinLauncherVersion = $mod.MinLauncherVersion; Sha256 = $hash; Path = $destination; Origin = $origin }
    }
    return $result
}

function New-ReleaseManifest {
    param([Parameter(Mandatory)]$Plan, [Parameter(Mandatory)][array]$Mods)
    $manifestMods = @($Mods | ForEach-Object {
        [ordered]@{ id = $_.Id; file = $_.File; version = $_.Version; minLauncherVersion = $_.MinLauncherVersion; sha256 = $_.Sha256
            downloadUrl = if ($Plan.Repository) { "https://github.com/$($Plan.Repository)/releases/download/v$($Plan.ReleaseVersion)/$($_.File)" } else { '' } }
    })
    return [ordered]@{
        version = $Plan.ReleaseVersion; minLauncherVersion = $Plan.MinLauncherVersion
        launcher = [ordered]@{ version = $Plan.LauncherVersion; releaseVersion = $Plan.LauncherReleaseVersion; downloadUrl = if ($Plan.Repository) { "https://github.com/$($Plan.Repository)/releases/download/v$($Plan.LauncherReleaseVersion)/SS14ModLauncher-$($Plan.LauncherVersion)-win-x64.zip" } else { '' } }
        mods = $manifestMods
    }
}

function New-ReleaseArtifactLedger {
    param([Parameter(Mandatory)]$Plan, [Parameter(Mandatory)][array]$Mods)
    $entries = @($Plan.Ledger.artifacts)
    foreach ($mod in $Mods) {
        if ($mod.Origin -ne 'source-build') { continue }
        if (-not $Plan.Repository) { continue }
        $entries += [ordered]@{ id = $mod.Id; version = $mod.Version; file = $mod.File; repository = $Plan.Repository; sourceRelease = $Plan.ReleaseVersion; sha256 = $mod.Sha256 }
    }
    return [ordered]@{ schemaVersion = 1; artifacts = $entries }
}
