param(
    [string]$InstallDir,
    [switch]$Elevated
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

$serviceName = "ScreenDash.Privileged"
$helperTaskName = "ScreenDash.PrivilegedHelper"

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
    try {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    } catch {
        # ignore
    }

    # Best-effort wait for the service to actually stop before deleting.
    try {
        $svc = Get-Service -Name $serviceName -ErrorAction Stop
        if ($svc.Status -ne 'Stopped') {
            $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(10))
        }
    } catch {
        # ignore
    }

    try {
        sc.exe delete $serviceName 2>&1 | Out-Null
    } catch {
        # ignore
    }
}

try {
    schtasks.exe /Delete /F /TN "\$helperTaskName" 2>&1 | Out-Null
} catch {
    # ignore
}
