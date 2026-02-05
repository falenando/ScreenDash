[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Runtime = "win-x64",

    [Parameter(Mandatory = $false)]
    [string]$Configuration = "Release",

    [Parameter(Mandatory = $false)]
    [string]$Version = "1.0.0",

    [Parameter(Mandatory = $false)]
    [string]$Channel = "stable",

    [Parameter(Mandatory = $false)]
    [switch]$SelfContained = $true
)

$ErrorActionPreference = "Stop"

function Publish-Project([string]$ProjectPath, [string]$OutDir) {
    $sc = if ($SelfContained) { "true" } else { "false" }

    dotnet publish $ProjectPath -c $Configuration -r $Runtime --self-contained $sc -o $OutDir | Write-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $ProjectPath"
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repoRoot ".artifacts"
$stagingRoot = Join-Path $artifactsRoot "staging"
$appDir = Join-Path $artifactsRoot "velopack\app"
$outDir = Join-Path $artifactsRoot "velopack\out"
$hooksDir = Join-Path $appDir "VelopackHooks"

New-Item -ItemType Directory -Force $stagingRoot | Out-Null
New-Item -ItemType Directory -Force $appDir | Out-Null
New-Item -ItemType Directory -Force $outDir | Out-Null
New-Item -ItemType Directory -Force $hooksDir | Out-Null

$hostOut = Join-Path $stagingRoot "HostApp"
$svcOut = Join-Path $stagingRoot "PrivilegedService"
$helperOut = Join-Path $stagingRoot "PrivilegedHelper"

Publish-Project (Join-Path $repoRoot "HostApp\HostApp.csproj") $hostOut
Publish-Project (Join-Path $repoRoot "PrivilegedService\PrivilegedService.csproj") $svcOut
Publish-Project (Join-Path $repoRoot "PrivilegedHelper\PrivilegedHelper.csproj") $helperOut

New-Item -ItemType Directory -Force (Join-Path $appDir "HostApp") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $appDir "PrivilegedService") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $appDir "PrivilegedHelper") | Out-Null

Copy-Item (Join-Path $hostOut "*") (Join-Path $appDir "HostApp") -Recurse -Force
Copy-Item (Join-Path $svcOut "*") (Join-Path $appDir "PrivilegedService") -Recurse -Force
Copy-Item (Join-Path $helperOut "*") (Join-Path $appDir "PrivilegedHelper") -Recurse -Force

# Velopack expects the primary executable to exist at the root of packDir when creating stubs.
# If self-contained, copy the full publish output so the runtime files (hostpolicy/hostfxr/etc) are available.
if ($SelfContained) {
    Copy-Item (Join-Path $hostOut "*") $appDir -Recurse -Force
} else {
    # Framework-dependent root entrypoint with required dependencies.
    Copy-Item (Join-Path $hostOut "HostApp.exe") (Join-Path $appDir "HostApp.exe") -Force
    Copy-Item (Join-Path $hostOut "HostApp.dll") (Join-Path $appDir "HostApp.dll") -Force
    Copy-Item (Join-Path $hostOut "HostApp.runtimeconfig.json") (Join-Path $appDir "HostApp.runtimeconfig.json") -Force
    Copy-Item (Join-Path $hostOut "HostApp.deps.json") (Join-Path $appDir "HostApp.deps.json") -Force

    # HostApp.exe is placed at the pack root for Velopack stubs, so its direct dependencies must also exist there.
    # Ensure RemoteCore.dll is available at the root to avoid FileNotFoundException at startup.
    Copy-Item (Join-Path $hostOut "RemoteCore.dll") (Join-Path $appDir "RemoteCore.dll") -Force

    # HostApp references Velopack at runtime (VelopackApp.Build().Run()), so ensure Velopack.dll is also present at the root.
    Copy-Item (Join-Path $hostOut "Velopack.dll") (Join-Path $appDir "Velopack.dll") -Force

    # Velopack depends on NuGet.Versioning at runtime.
    Copy-Item (Join-Path $hostOut "NuGet.Versioning.dll") (Join-Path $appDir "NuGet.Versioning.dll") -Force
}

# Ensure service publish output includes required runtimeconfig/deps next to its executable.
Copy-Item (Join-Path $svcOut "PrivilegedService.runtimeconfig.json") (Join-Path $appDir "PrivilegedService\PrivilegedService.runtimeconfig.json") -Force
Copy-Item (Join-Path $svcOut "PrivilegedService.deps.json") (Join-Path $appDir "PrivilegedService\PrivilegedService.deps.json") -Force

Copy-Item (Join-Path $PSScriptRoot "install.ps1") (Join-Path $hooksDir "install.ps1") -Force
Copy-Item (Join-Path $PSScriptRoot "uninstall.ps1") (Join-Path $hooksDir "uninstall.ps1") -Force

Write-Host "Packing with Velopack (vpk)..."

vpk pack `
  --packId ScreenDash.HostApp `
  --packVersion $Version `
  --packTitle "ScreenDash HostApp" `
  --packDir $appDir `
  --mainExe "HostApp.exe" `
  --outputDir $outDir `
  --channel $Channel

if ($LASTEXITCODE -ne 0) {
    throw "vpk pack failed"
}

Write-Host "Done. Output: $outDir"