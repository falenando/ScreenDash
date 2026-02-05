param(
    [string]$InstallDir = "C:\Program Files\ScreenDash\RemoteSupport.Service"
)

$serviceName = "ScreenDash.RemoteSupport"
$exePath = Join-Path $InstallDir "RemoteSupport.Service.exe"

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
}

sc.exe create $serviceName binPath= "`"$exePath`"" start= auto obj= LocalSystem | Out-Null
sc.exe description $serviceName "ScreenDash RemoteSupport Service" | Out-Null
sc.exe start $serviceName | Out-Null
