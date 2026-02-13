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

    ,
    [Parameter(Mandatory = $false)]
    [switch]$DevSign = $false,

    [Parameter(Mandatory = $false)]
    [string]$DevSignSubject = "CN=ScreenDash Dev",

    [Parameter(Mandatory = $false)]
    [string]$DevSignPfxPassword = ""
)

$ErrorActionPreference = "Stop"

function Get-SignToolPath {
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $roots = @(
        "$env:ProgramFiles (x86)\Windows Kits\10\bin",
        "$env:ProgramFiles\Windows Kits\10\bin"
    )

    foreach ($root in $roots) {
        if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path $root)) { continue }
        $candidate = Get-ChildItem -Path $root -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
            Sort-Object -Property FullName -Descending |
            Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }

    return $null
}

function Get-DevSigningCert([string]$Subject) {
    $existing = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $Subject } |
        Sort-Object -Property NotAfter -Descending |
        Select-Object -First 1
    if ($existing) { return $existing }

    Write-Host "Creating self-signed code signing certificate: $Subject"
    return New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject -CertStoreLocation Cert:\CurrentUser\My
}

function Export-DevSigningPfx($Cert, [string]$PfxPath, [string]$Password) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $PfxPath) | Out-Null
    $secure = ConvertTo-SecureString -String $Password -AsPlainText -Force
    Export-PfxCertificate -Cert $Cert -FilePath $PfxPath -Password $secure | Out-Null
}

function Trust-DevSigningCert($Cert) {
    $thumb = $Cert.Thumbprint
    $trusted = Get-ChildItem -Path Cert:\CurrentUser\TrustedPublisher -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $thumb } |
        Select-Object -First 1
    if ($trusted) { return }

    Write-Host "Trusting dev signing certificate in CurrentUser/TrustedPublisher (local machine only)."
    $null = $Cert | Export-Certificate -FilePath (Join-Path $env:TEMP "screendash-dev.cer")
    Import-Certificate -FilePath (Join-Path $env:TEMP "screendash-dev.cer") -CertStoreLocation Cert:\CurrentUser\TrustedPublisher | Out-Null
}

function Sign-Files([string]$SignTool, [string]$PfxPath, [string]$Password, [string[]]$Files) {
    $toSign = $Files | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) }
    if ($toSign.Count -eq 0) { return }

    foreach ($file in $toSign) {
        Write-Host "Signing: $file"
        & $SignTool sign /fd SHA256 /f "$PfxPath" /p "$Password" /tr http://timestamp.digicert.com /td SHA256 "$file" | Write-Host
        if ($LASTEXITCODE -ne 0) {
            throw "signtool failed for $file"
        }
    }
}

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

# Include service/helper binaries under VelopackHooks so install.ps1 can be run from there without manual file copying.
New-Item -ItemType Directory -Force (Join-Path $hooksDir "PrivilegedService") | Out-Null
New-Item -ItemType Directory -Force (Join-Path $hooksDir "PrivilegedHelper") | Out-Null
Copy-Item (Join-Path $svcOut "*") (Join-Path $hooksDir "PrivilegedService") -Recurse -Force
Copy-Item (Join-Path $helperOut "*") (Join-Path $hooksDir "PrivilegedHelper") -Recurse -Force

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
  --channel $Channel `
  --msiDeploymentTool

if ($LASTEXITCODE -ne 0) {
    throw "vpk pack failed"
}

if ($DevSign) {
    $signTool = Get-SignToolPath
    if (-not $signTool) {
        throw "DevSign enabled but signtool.exe was not found. Install Windows SDK (Signing Tools) or run without -DevSign."
    }

    $cert = Get-DevSigningCert $DevSignSubject
    Trust-DevSigningCert $cert

    $pfxPath = Join-Path $artifactsRoot "codesign\screendash-dev.pfx"
    Export-DevSigningPfx $cert $pfxPath $DevSignPfxPassword

    $hostExe = Join-Path $appDir "HostApp.exe"
    $serviceExe = Join-Path $appDir "PrivilegedService\PrivilegedService.exe"
    $helperExe = Join-Path $appDir "PrivilegedHelper\PrivilegedHelper.exe"

    Sign-Files $signTool $pfxPath $DevSignPfxPassword @($hostExe, $serviceExe, $helperExe)
}

Write-Host "Done. Output: $outDir"