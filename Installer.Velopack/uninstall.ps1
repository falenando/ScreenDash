param(
    [string]$InstallDir,
    [switch]$Elevated
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

$serviceName = "ScreenDash.Privileged"

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    if (-not $Elevated) {
        $escapedInstallDir = $InstallDir.Replace('"', '""')
        $arguments = "-ExecutionPolicy Bypass -File `"$PSCommandPath`" -InstallDir `"$escapedInstallDir`" -Elevated"
        Start-Process -FilePath "powershell" -Verb RunAs -ArgumentList $arguments -Wait | Out-Null
        return
    }

    throw "Administrator privileges are required to uninstall ScreenDash.Privileged. Execute the uninstaller as administrator."
}

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
}
