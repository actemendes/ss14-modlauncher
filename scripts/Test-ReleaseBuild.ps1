#requires -Version 7.0
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Release.Common.ps1')
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
function Assert-ReleaseTest([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-ReleaseThrows([scriptblock]$Action, [string]$Message) {
    $threw = $false
    try { & $Action | Out-Null } catch { $threw = $true }
    Assert-ReleaseTest $threw $Message
}
$plan = Get-ReleasePlan -ProjectRoot $repositoryRoot
Assert-ReleaseThrows { Assert-ReleaseVersion '01.2.3' } 'Release versions must follow runtime semantic-version rules.'
Assert-ReleaseThrows { Assert-ReleaseRepository 'owner/repo..name' } 'Repository names must follow runtime URL trust rules.'
$currentFeed = [version]$plan.ReleaseVersion
$futureFeed = "$($currentFeed.Major).$($currentFeed.Minor).$($currentFeed.Build + 1)"
$feed = Get-ReleasePlan -ProjectRoot $repositoryRoot -ReleaseVersion $futureFeed
$manifestMods = @($feed.Mods | ForEach-Object { @{ Id = $_.Id; File = $_.File; Version = $_.Version; MinLauncherVersion = $_.MinLauncherVersion; Sha256 = if ($_.Artifact) { $_.Artifact.sha256 } else { 'A' * 64 } } })
$manifest = New-ReleaseManifest -Plan $feed -Mods $manifestMods
Assert-ReleaseTest ($manifest.version -eq $futureFeed -and $manifest.launcher.version -eq $plan.LauncherVersion) 'Feed and launcher versions must vary independently.'
Assert-ReleaseTest ($manifest.launcher.downloadUrl.EndsWith("/v$($plan.LauncherReleaseVersion)/SS14ModLauncher-$($plan.LauncherVersion)-win-x64.zip")) 'A mod-only feed must reference the prior launcher archive.'
Assert-ReleaseTest ($manifest.minLauncherVersion -eq $plan.MinLauncherVersion) 'A feed override must not increase compatibility floors.'
foreach ($mod in $plan.Mods) { Assert-ReleaseTest (@($manifest.mods | Where-Object { $_.id -eq $mod.Id -and $_.version -eq $mod.Version }).Count -eq 1) 'A launcher/feed version must not change an individual mod version.' }
$fork = Get-ReleasePlan -ProjectRoot $repositoryRoot -Repository 'example/fork' -OverrideRepository
Assert-ReleaseTest ($fork.Repository -eq 'example/fork') 'The output repository override was ignored.'
foreach ($mod in $plan.Mods | Where-Object { $_.Artifact }) {
    $forkMod = @($fork.Mods | Where-Object { $_.Id -eq $mod.Id })[0]
    Assert-ReleaseTest ($forkMod.Artifact.repository -eq $mod.Artifact.repository) 'Output repository overrides must not alter pinned source repositories.'
}

$fixture = Join-Path ([IO.Path]::GetTempPath()) ('SS14ModLauncher-ReleaseBuildTests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
try {
    foreach ($directory in @('release', 'catalog', 'Probe', 'Bootstrap')) { New-Item -ItemType Directory -Path (Join-Path $fixture $directory) -Force | Out-Null }
    @{ schemaVersion = 1; mode = 'Full'; releaseVersion = '1.5.0'; launcherVersion = '0.2.0'; launcherReleaseVersion = '1.5.0'; bootstrapVersion = '0.1.2'; repository = 'example/project' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixture 'release/release.json')
    @(@{ id = 'probe'; version = '2.4.0'; minLauncherVersion = '0.1.2'; file = 'Probe.Mod.dll'; source = 'Probe' }) | ConvertTo-Json -AsArray | Set-Content -LiteralPath (Join-Path $fixture 'catalog/mods.json')
    @{ schemaVersion = 1; artifacts = @() } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixture 'release/mod-artifacts.json')
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Version>0.1.2</Version></PropertyGroup></Project>' | Set-Content -LiteralPath (Join-Path $fixture 'Bootstrap/Bootstrap.csproj')
    'namespace Bootstrap; public class Marker { }' | Set-Content -LiteralPath (Join-Path $fixture 'Bootstrap/Marker.cs')
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>Probe.Mod</AssemblyName><Version>0.1.2</Version><Version Condition="'$(ModBuildId)' == 'probe' and '$(ModBuildVersion)' != ''">$(ModBuildVersion)</Version></PropertyGroup>
  <ItemGroup><ProjectReference Include="../Bootstrap/Bootstrap.csproj" /></ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $fixture 'Probe/Probe.csproj')
    'public static class Mod { public static System.Type ApiType => typeof(Bootstrap.Marker); }' | Set-Content -LiteralPath (Join-Path $fixture 'Probe/Mod.cs')
    $sourcePlan = Get-ReleasePlan -ProjectRoot $fixture
    $built = @(Resolve-ReleaseMods -Plan $sourcePlan -StagingPath (Join-Path $fixture 'staging'))
    $independent = New-ReleaseManifest -Plan $sourcePlan -Mods $built
    Assert-ReleaseTest ($independent.version -eq '1.5.0' -and $independent.launcher.version -eq '0.2.0' -and $independent.launcher.releaseVersion -eq '1.5.0') 'Feed tags must not consume or force launcher versions.'
    Assert-ReleaseTest ($independent.launcher.downloadUrl.EndsWith('/v1.5.0/SS14ModLauncher-0.2.0-win-x64.zip')) 'Launcher artifact filename and hosting release tag must be independent.'
    Assert-ReleaseTest ($built.Count -eq 1 -and $built[0].Version -eq '2.4.0' -and $built[0].Origin -eq 'source-build') 'A new mod must build at its own version.'
    $bootstrap = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $fixture 'staging/source/probe/Bootstrap.dll'))
    Assert-ReleaseTest ($bootstrap.Version.ToString(3) -eq '0.1.2') 'Mod build properties must not change the referenced bootstrap version.'
    $nextLedger = New-ReleaseArtifactLedger -Plan $sourcePlan -Mods $built
    $nextLedger | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $fixture 'release/mod-artifacts.json')
    New-Item -ItemType Directory -Path (Join-Path $fixture 'payload') -Force | Out-Null
    Copy-Item -LiteralPath $built[0].Path -Destination (Join-Path $fixture 'payload/Probe.Mod.dll')
    # Destroy source compilation deliberately: a pinned version must still reuse its verified bytes.
    'This is not valid C#.' | Set-Content -LiteralPath (Join-Path $fixture 'Probe/Mod.cs')
    $lockedPlan = Get-ReleasePlan -ProjectRoot $fixture -ReleaseVersion '1.5.1'
    $reused = @(Resolve-ReleaseMods -Plan $lockedPlan -StagingPath (Join-Path $fixture 'reuse') -Offline)
    $laterFeed = New-ReleaseManifest -Plan $lockedPlan -Mods $reused
    Assert-ReleaseTest ($laterFeed.version -eq '1.5.1' -and $laterFeed.launcher.downloadUrl -eq $independent.launcher.downloadUrl) 'A later mod feed must retain the actual prior launcher hosting tag.'
    Assert-ReleaseTest ($reused[0].Origin -eq 'published-artifact' -and $reused[0].Sha256 -eq $built[0].Sha256) 'Published mod versions must be reused byte-for-byte without compiling their source.'
    [IO.File]::WriteAllBytes((Join-Path $fixture 'payload/Probe.Mod.dll'), [byte[]]@(1, 2, 3))
    Assert-ReleaseThrows { Resolve-ReleaseMods -Plan $lockedPlan -StagingPath (Join-Path $fixture 'corrupt') -Offline } 'A mismatched artifact must never be accepted or silently rebuilt.'
    Assert-ReleaseThrows { Get-ReleaseContainedPath $fixture '../outside' } 'Release artifacts must remain inside their specified root.'
    Write-Host 'Release version independence, source-mod versioning, bootstrap pinning, immutable reuse and corruption rejection passed.'
} finally {
    $full = [IO.Path]::GetFullPath($fixture)
    $allowed = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or -not [IO.Path]::GetFileName($full).StartsWith('SS14ModLauncher-ReleaseBuildTests-')) { throw 'Unsafe test cleanup path.' }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}
