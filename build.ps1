#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.1',
    [ValidatePattern('^$|^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_.-]+$')]
    [string]$Repository = 'actemendes/ss14-modlauncher',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$payloadPath = Join-Path $projectRoot 'payload'
$distPath = Join-Path $projectRoot 'dist'
$outputPath = Join-Path $distPath 'SS14ModLauncher'
$modAssetsPath = Join-Path $distPath 'mod-assets'

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
        [IO.Path]::GetFullPath((Join-Path $projectRoot 'dist/SS14ModLauncher')),
        [IO.Path]::GetFullPath((Join-Path $projectRoot 'dist/mod-assets'))
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
if ($Repository -and ($Repository.Split('/')[1] -in @('.', '..'))) { throw 'Invalid GitHub repository name.' }


$catalogFile = Join-Path $projectRoot 'catalog/mods.json'
$catalog = @(Get-Content -LiteralPath $catalogFile -Raw | ConvertFrom-Json)
$catalogVersion = [regex]::Match((Get-Content -LiteralPath (Join-Path $projectRoot 'Launcher.Core/Catalog.cs') -Raw), 'LauncherVersion\s*=\s*"([^"]+)"').Groups[1].Value
$appVersion = [regex]::Match((Get-Content -LiteralPath (Join-Path $projectRoot 'Installer/Program.cs') -Raw), 'const string Version\s*=\s*"([^"]+)"').Groups[1].Value
if ($catalogVersion -ne $Version -or $appVersion -ne $Version -or @($catalog | Where-Object { $_.version -ne $Version }).Count -gt 0) {
    throw "Build version must match Program.Version, Catalog.LauncherVersion, and every bundled catalog version. Update these sources before building version $Version."
}
if (-not $SkipTests) {
    Invoke-DotNet @('run', '--project', (Join-Path $projectRoot 'tests/CrewConsole.Tests.csproj'), '-c', 'Release')
    Invoke-DotNet @('run', '--project', (Join-Path $projectRoot 'Launcher.Core.Tests/Launcher.Core.Tests.csproj'), '-c', 'Release')
}

Reset-GeneratedDirectory $payloadPath
Invoke-DotNet @('build', (Join-Path $projectRoot 'HelloWorld/HelloWorld.csproj'), '-c', 'Release', "-p:Version=$Version", '-o', $payloadPath)
Invoke-DotNet @('build', (Join-Path $projectRoot 'CrewConsole/CrewConsole.csproj'), '-c', 'Release', "-p:Version=$Version", '-o', $payloadPath)

$modFiles = @(
    @{ Id = 'crew-console'; File = 'CrewConsole.Mod.dll' },
    @{ Id = 'hello-world'; File = 'HelloWorld.Mod.dll' }
)
$allowedPayload = @('SS14LocalMods.Bootstrap.dll', '0Harmony.dll', 'CrewConsole.Mod.dll', 'HelloWorld.Mod.dll')
$nugetPackages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
$harmonySource = Join-Path $nugetPackages 'lib.harmony/2.4.2/lib/net10.0/0Harmony.dll'
Copy-Item -LiteralPath $harmonySource -Destination (Join-Path $payloadPath '0Harmony.dll') -Force
foreach ($file in $allowedPayload) {
    if (-not (Test-Path -LiteralPath (Join-Path $payloadPath $file) -PathType Leaf)) { throw "Missing payload: $file" }
}
Get-ChildItem -LiteralPath $payloadPath -File |
    Where-Object { $_.Name -notin $allowedPayload } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }

Reset-GeneratedDirectory $outputPath
Reset-GeneratedDirectory $modAssetsPath
Invoke-DotNet @(
    'publish', (Join-Path $projectRoot 'Installer/Installer.csproj'), '-c', 'Release', '-r', 'win-x64',
    '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None', '-p:DebugSymbols=false', "-p:Version=$Version", '-o', $outputPath
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
$runtimeInfo = [ordered]@{}
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

$manifestMods = @()
foreach ($mod in $modFiles) {
    $source = Join-Path $payloadPath $mod.File
    Copy-Item -LiteralPath $source -Destination $modAssetsPath
    $manifestMods += [ordered]@{
        id = $mod.Id
        file = $mod.File
        version = $Version
        sha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        downloadUrl = if ($Repository) { "https://github.com/$Repository/releases/download/v$Version/$($mod.File)" } else { '' }
    }
}
$manifest = [ordered]@{ version = $Version; minLauncherVersion = $Version; mods = $manifestMods }
$manifestName = if ($Repository) { 'mods-manifest.json' } else { 'mods-manifest.local.json' }
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $modAssetsPath $manifestName) -Encoding utf8

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
    version = $Version
    runtimeIdentifier = 'win-x64'
    sdk = $sdkVersion
    runtimes = $runtimeInfo
    sourceRevision = $revision
    sourceDirty = $dirty
    builtAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    repository = $Repository
    testsRun = -not [bool]$SkipTests
    published = $false
}
$buildInfo | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $distPath 'build-info.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $distPath 'build-info.json') -Destination $outputPath

$archivePath = Join-Path $distPath "SS14ModLauncher-$Version-win-x64.zip"
Compress-Archive -LiteralPath $outputPath -DestinationPath $archivePath -CompressionLevel Optimal -Force
$checksumFiles = @($archivePath, (Join-Path $distPath 'build-info.json')) + @(Get-ChildItem -LiteralPath $modAssetsPath -File | Sort-Object Name | ForEach-Object { $_.FullName })
$checksums = foreach ($file in $checksumFiles) {
    # GitHub release assets are downloaded into one directory, regardless of local build layout.
    $assetName = [IO.Path]::GetFileName($file)
    "$((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant())  $assetName"
}
$checksums | Set-Content -LiteralPath (Join-Path $distPath 'SHA256SUMS.txt') -Encoding utf8
Write-Host "Built: $archivePath"
Write-Host "Manifest: $modAssetsPath/$manifestName"
Write-Host 'No files were published.'
