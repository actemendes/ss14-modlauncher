$ErrorActionPreference = 'Stop'
$experimentRoot = $PSScriptRoot
$payloadPath = Join-Path $experimentRoot 'payload'
$outputPath = Join-Path $experimentRoot 'dist/SS14LocalMods'
dotnet build (Join-Path $experimentRoot 'HelloWorld/HelloWorld.csproj') -c Release -o $payloadPath
if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }
dotnet build (Join-Path $experimentRoot 'CrewConsole/CrewConsole.csproj') -c Release -o $payloadPath
if ($LASTEXITCODE -ne 0) { throw 'Crew console build failed.' }
$nugetPackages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
Copy-Item -LiteralPath (Join-Path $nugetPackages 'lib.harmony/2.4.2/lib/net10.0/0Harmony.dll') -Destination $payloadPath
dotnet publish (Join-Path $experimentRoot 'Installer/Installer.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $outputPath
if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed.' }
Copy-Item -LiteralPath (Join-Path $experimentRoot 'README.md') -Destination $outputPath
Write-Output (Join-Path $outputPath 'SS14ModInstaller.exe')
