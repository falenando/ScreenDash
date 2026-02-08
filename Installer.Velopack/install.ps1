param(
    [string]$InstallDir,
    [switch]$Elevated
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

function New-InstallLogPaths {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $fileName = "ScreenDash_Install_${stamp}.log"

    $paths = @()
    if (-not [string]::IsNullOrWhiteSpace($env:TEMP)) {
        $paths += (Join-Path $env:TEMP $fileName)
    }

    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $localLogDir = Join-Path $env:LOCALAPPDATA "ScreenDash\logs"
        try {
            New-Item -ItemType Directory -Force $localLogDir | Out-Null
            $paths += (Join-Path $localLogDir $fileName)
        } catch {
            # ignore
        }
    }

    $programDataLogDir = Join-Path $env:ProgramData "ScreenDash\logs"
    try {
        New-Item -ItemType Directory -Force $programDataLogDir | Out-Null
        $paths += (Join-Path $programDataLogDir $fileName)
    } catch {
        # ignore
    }

    return $paths
}

$logPaths = New-InstallLogPaths

function Write-InstallLog {
    param([string]$Message)
    $entry = "$(Get-Date -Format o) - $Message"
    foreach ($path in $logPaths) {
        try {
            Add-Content -Path $path -Value $entry
        } catch {
            # ignore logging failures to avoid breaking install hook
        }
    }
}

$serviceName = "ScreenDash.Privileged"
$exePath = Join-Path $InstallDir "PrivilegedService\PrivilegedService.exe"

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    if (-not $Elevated) {
        Write-InstallLog "Elevation required. Relaunching install hook as administrator."
        $escapedInstallDir = $InstallDir.Replace('"', '""')
        $arguments = "-ExecutionPolicy Bypass -File `"$PSCommandPath`" -InstallDir `"$escapedInstallDir`" -Elevated"
        $elevatedProcess = Start-Process -FilePath "powershell" -Verb RunAs -ArgumentList $arguments -Wait -PassThru
        Write-InstallLog "Elevated install hook finished with exit code $($elevatedProcess.ExitCode)."
        return
    }

    Write-InstallLog "ERROR: Administrator privileges are required to install the Windows service."
    throw "Administrator privileges are required to install ScreenDash.Privileged. Execute the installer as administrator."
}

Write-InstallLog "Install started. InstallDir: $InstallDir"
Write-InstallLog "Checking executable path: $exePath"

try {
    if (-not (Test-Path $exePath)) {
        Write-InstallLog "ERROR: Executable not found at $exePath"
        throw "Executable not found: $exePath"
    }

    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        Write-InstallLog "Service $serviceName exists. Stopping and deleting."
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        $deleteOutput = sc.exe delete $serviceName 2>&1
        Write-InstallLog "sc.exe delete output: $deleteOutput"
    }

    $createOutput = sc.exe create $serviceName binPath= "`"$exePath`"" start= auto obj= LocalSystem 2>&1
    Write-InstallLog "sc.exe create output: $createOutput"

    $descriptionOutput = sc.exe description $serviceName "ScreenDash Privileged Service" 2>&1
    Write-InstallLog "sc.exe description output: $descriptionOutput"

    $startOutput = sc.exe start $serviceName 2>&1
    Write-InstallLog "sc.exe start output: $startOutput"
    Write-InstallLog "Install hook completed successfully."
}
catch {
    Write-InstallLog "ERROR during install hook: $_"
    Write-InstallLog "Full exception: $($_ | Out-String)"
    throw
}
finally {
    if ($logPaths.Count -gt 0) {
        Write-InstallLog "Install finished. Log file(s): $($logPaths -join '; ')"
    } else {
        Write-InstallLog "Install finished. (No log file path available)"
    }
}
