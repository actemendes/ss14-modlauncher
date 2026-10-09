#requires -Version 7.0
[CmdletBinding()]
param(
    [Alias('Version')][ValidatePattern('^$|^\d+\.\d+\.\d+$')][string]$ReleaseVersion = '',
    [AllowEmptyString()][string]$Repository,
    [ValidateSet('Auto', 'Full', 'ModsOnly')][string]$Mode = 'Auto',
    [switch]$ModsOnly,
    [string]$OutputRoot = '',
    [switch]$Offline,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
. (Join-Path $projectRoot 'scripts/Release.Common.ps1')
if ($ModsOnly) { $Mode = 'ModsOnly' }
$plan = Get-ReleasePlan -ProjectRoot $projectRoot -ReleaseVersion $ReleaseVersion -Repository $Repository -OverrideRepository:($PSBoundParameters.ContainsKey('Repository'))
if ($Mode -eq 'Auto') { $Mode = $plan.Mode }
if (-not $OutputRoot) { $OutputRoot = if ($Mode -eq 'ModsOnly') { 'dist/mod-release' } else { 'dist' } }
$payloadPath = Join-Path $projectRoot 'payload'
$outputRelative = if ([IO.Path]::IsPathRooted($OutputRoot)) { [IO.Path]::GetRelativePath($projectRoot, $OutputRoot) } else { $OutputRoot }
$distPath = Get-ReleaseContainedPath $projectRoot $outputRelative
$outputPath = Join-Path $distPath 'SS14ModLauncher'
$modAssetsPath = Join-Path $distPath 'mod-assets'
$stagingPath = Get-ReleaseContainedPath $projectRoot ".tools/build-staging/$([guid]::NewGuid().ToString('N'))"

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

function Reset-GeneratedDirectory {
    param([Parameter(Mandatory)][string]$Path)
    $resolvedTarget = [IO.Path]::GetFullPath($Path)
    $allowedTargets = @(
        [IO.Path]::GetFullPath((Join-Path $projectRoot 'payload')),
        [IO.Path]::GetFullPath($outputPath),
        [IO.Path]::GetFullPath($modAssetsPath)
    )
    if ($resolvedTarget -notin $allowedTargets -or
        -not $resolvedTarget.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a directory outside the explicit generated targets: $resolvedTarget"
    }
    for ($ancestor = [IO.DirectoryInfo]::new($resolvedTarget).Parent; $null -ne $ancestor; $ancestor = $ancestor.Parent) {
        if ([string]::Equals($ancestor.FullName, $projectRoot, [StringComparison]::OrdinalIgnoreCase)) { break }
        if (Test-Path -LiteralPath $ancestor.FullName) {
            if ((Get-Item -LiteralPath $ancestor.FullName -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Refusing to clean through a linked parent directory: $($ancestor.FullName)"
            }
        }
    }
    if (Test-Path -LiteralPath $resolvedTarget) {
        $item = Get-Item -LiteralPath $resolvedTarget -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing to clean a generated directory that is a link: $resolvedTarget"
        }
        $linkedChild = Get-ChildItem -LiteralPath $resolvedTarget -Force -Recurse |
            Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } |
            Select-Object -First 1
        if ($null -ne $linkedChild) { throw "Refusing to clean a generated directory containing links: $resolvedTarget" }
        Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resolvedTarget -Force | Out-Null
}

if (-not $IsWindows) { throw 'The v0.1 launcher build requires Windows.' }
$sdkVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $sdkVersion.StartsWith('10.')) { throw '.NET SDK 10 is required.' }
if ($Mode -eq 'Full') {
    if ($plan.ReleaseVersion -ne $plan.LauncherReleaseVersion) { throw 'A full build must host the launcher ZIP on this release tag: set launcherReleaseVersion to releaseVersion.' }
    $catalogVersion = [regex]::Match((Get-Content -LiteralPath (Join-Path $projectRoot 'Launcher.Core/Catalog.cs') -Raw), 'LauncherVersion\s*=\s*"([^"]+)"').Groups[1].Value
    $appVersion = [regex]::Match((Get-Content -LiteralPath (Join-Path $projectRoot 'Installer/Program.cs') -Raw), 'const string Version\s*=\s*"([^"]+)"').Groups[1].Value
    [xml]$installerProject = Get-Content -LiteralPath (Join-Path $projectRoot 'Installer/Installer.csproj') -Raw
    [xml]$bootstrapProject = Get-Content -LiteralPath (Join-Path $projectRoot 'Bootstrap/Bootstrap.csproj') -Raw
    if ($catalogVersion -ne $plan.LauncherVersion -or $appVersion -ne $plan.LauncherVersion -or @($installerProject.SelectNodes('//PropertyGroup/Version'))[0].InnerText -ne $plan.LauncherVersion) { throw 'Launcher sources and Installer.csproj must match launcherVersion. Mod versions are independent.' }
    if (@($bootstrapProject.SelectNodes('//PropertyGroup/Version'))[0].InnerText -ne $plan.BootstrapVersion) { throw 'Bootstrap.csproj must match its independent bootstrapVersion.' }
}
if (-not $SkipTests) {
    & (Join-Path $projectRoot 'scripts/Test-ReleaseBuild.ps1')
    Invoke-DotNet @('run', '--project', (Join-Path $projectRoot 'tests/CrewConsole.Tests.csproj'), '-c', 'Release')
    Invoke-DotNet @('run', '--project', (Join-Path $projectRoot 'Launcher.Core.Tests/Launcher.Core.Tests.csproj'), '-c', 'Release')
}

# Resolve before cleaning payload: old, hash-verified payload bytes are a valid artifact cache.
$resolvedMods = @(Resolve-ReleaseMods -Plan $plan -StagingPath (Join-Path $stagingPath 'mods') -Offline:$Offline)
$manifest = New-ReleaseManifest -Plan $plan -Mods $resolvedMods
$runtimeInfo = [ordered]@{}
if ($Mode -eq 'Full') {
$bootstrapOutput = Join-Path $stagingPath 'bootstrap'
Invoke-DotNet @('build', (Join-Path $projectRoot 'Bootstrap/Bootstrap.csproj'), '-c', 'Release', '-o', $bootstrapOutput)
$bootstrapDll = Join-Path $bootstrapOutput 'SS14LocalMods.Bootstrap.dll'
if ([Reflection.AssemblyName]::GetAssemblyName($bootstrapDll).Version.ToString(3) -ne $plan.BootstrapVersion) { throw 'Bootstrap assembly version drifted.' }
$nugetPackages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
$harmonySource = Join-Path $nugetPackages 'lib.harmony/2.4.2/lib/net10.0/0Harmony.dll'
if (-not (Test-Path -LiteralPath $harmonySource -PathType Leaf)) {
    $dependencies = Join-Path $stagingPath 'RuntimeDependencies.csproj'
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="Lib.Harmony" Version="2.4.2" /></ItemGroup></Project>' | Set-Content -LiteralPath $dependencies -Encoding utf8
    Invoke-DotNet @('restore', $dependencies)
}
Reset-GeneratedDirectory $payloadPath
Copy-Item -LiteralPath $bootstrapDll -Destination $payloadPath
Copy-Item -LiteralPath $harmonySource -Destination (Join-Path $payloadPath '0Harmony.dll') -Force
foreach ($mod in $resolvedMods) { Copy-Item -LiteralPath $mod.Path -Destination $payloadPath }

Reset-GeneratedDirectory $outputPath
# Do not pass global Version; it would change every referenced assembly.
Invoke-DotNet @(
    'publish', (Join-Path $projectRoot 'Installer/Installer.csproj'), '-c', 'Release', '-r', 'win-x64',
    '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $outputPath
)
if (-not (Test-Path -LiteralPath (Join-Path $outputPath 'SS14ModLauncher.exe'))) { throw 'Published launcher executable is missing.' }

foreach ($name in @('README.md', 'README.ru.md', 'CHANGELOG.md', 'CONTRIBUTING.md', 'SECURITY.md', 'THIRD-PARTY-NOTICES.md', 'LICENSE')) {
    $source = Join-Path $projectRoot $name
    if (Test-Path -LiteralPath $source -PathType Leaf) { Copy-Item -LiteralPath $source -Destination $outputPath }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination (Join-Path $outputPath 'docs') -Recurse

$licensesPath = Join-Path $outputPath 'licenses'
New-Item -ItemType Directory -Path $licensesPath -Force | Out-Null
$assets = Get-Content -LiteralPath (Join-Path $projectRoot 'Installer/obj/project.assets.json') -Raw | ConvertFrom-Json
foreach ($dependency in @($assets.project.frameworks.PSObject.Properties | ForEach-Object { $entry = $_.Value.PSObject.Properties['downloadDependencies']; if ($null -ne $entry) { $entry.Value } })) {
    if ($dependency.name -notin @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64')) { continue }
    $runtimeVersion = $dependency.version.Trim('[', ']').Split(',')[0].Trim()
    $packageDirectory = Join-Path $nugetPackages ($dependency.name.ToLowerInvariant() + '/' + $runtimeVersion)
    $runtimeInfo[$dependency.name] = $runtimeVersion
    $noticeFiles = @(Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\.TXT)?$' })
    if ($noticeFiles.Count -eq 0) { throw "Runtime notices not found for $($dependency.name) $runtimeVersion" }
    foreach ($notice in $noticeFiles) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licensesPath ($dependency.name + '-' + $notice.Name))
    }
}
if ($runtimeInfo.Count -ne 2) { throw 'Could not identify both published runtime license packages.' }
}

Reset-GeneratedDirectory $modAssetsPath
foreach ($mod in $resolvedMods) { Copy-Item -LiteralPath $mod.Path -Destination $modAssetsPath }
$manifestName = if ($plan.Repository) { 'mods-manifest.json' } else { 'mods-manifest.local.json' }
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $modAssetsPath $manifestName) -Encoding utf8
(New-ReleaseArtifactLedger -Plan $plan -Mods $resolvedMods) | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $distPath 'mod-artifacts.next.json') -Encoding utf8

$revision = 'UNKNOWN'
$dirty = $null
if (Get-Command git -ErrorAction SilentlyContinue) {
    $revisionOutput = & git -C $projectRoot rev-parse HEAD 2>$null
    if ($LASTEXITCODE -eq 0) {
        $revision = "$revisionOutput".Trim()
        $statusOutput = & git -C $projectRoot status --porcelain 2>$null
        if ($LASTEXITCODE -eq 0) { $dirty = -not [string]::IsNullOrWhiteSpace("$statusOutput") }
    }
}
$buildInfo = [ordered]@{
    product = 'SS14 ModLauncher by actemendes'
    version = $plan.ReleaseVersion
    releaseVersion = $plan.ReleaseVersion
    launcherVersion = $plan.LauncherVersion
    launcherReleaseVersion = $plan.LauncherReleaseVersion
    bootstrapVersion = $plan.BootstrapVersion
    mode = $Mode
    launcherBuilt = ($Mode -eq 'Full')
    launcherDownloadUrl = $manifest.launcher.downloadUrl
    mods = @($resolvedMods | ForEach-Object { [ordered]@{ id = $_.Id; version = $_.Version; sha256 = $_.Sha256; origin = $_.Origin } })
    runtimeIdentifier = 'win-x64'
    sdk = $sdkVersion
    runtimes = $runtimeInfo
    sourceRevision = $revision
    sourceDirty = $dirty
    builtAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    repository = $plan.Repository
    testsRun = -not [bool]$SkipTests
    published = $false
}
$buildInfo | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $distPath 'build-info.json') -Encoding utf8
$archivePath = $null
if ($Mode -eq 'Full') {
    Copy-Item -LiteralPath (Join-Path $distPath 'build-info.json') -Destination $outputPath
    $archivePath = Join-Path $distPath "SS14ModLauncher-$($plan.LauncherVersion)-win-x64.zip"
    Compress-Archive -LiteralPath $outputPath -DestinationPath $archivePath -CompressionLevel Optimal -Force
}
# mod-artifacts.next.json is a local maintainer receipt, not a public release asset.
$checksumFiles = @((Join-Path $distPath 'build-info.json')) + @(Get-ChildItem -LiteralPath $modAssetsPath -File | Sort-Object Name | ForEach-Object { $_.FullName })
if ($archivePath) { $checksumFiles = @($archivePath) + $checksumFiles }
$checksums = foreach ($file in $checksumFiles) {
    # GitHub release assets are downloaded into one directory, regardless of local build layout.
    $assetName = [IO.Path]::GetFileName($file)
    "$((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant())  $assetName"
}
$checksums | Set-Content -LiteralPath (Join-Path $distPath 'SHA256SUMS.txt') -Encoding utf8
if ($archivePath) { Write-Host "Built launcher: $archivePath" } else { Write-Host "Reusing launcher $($plan.LauncherVersion); no launcher was built or repackaged." }
Write-Host "Manifest: $modAssetsPath/$manifestName"
Write-Host 'No files were published.'
