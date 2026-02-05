param(
    [string]$InstallDir = "C:\Program Files\ScreenDash\RemoteSupport.Service"
)

$serviceName = "ScreenDash.RemoteSupport"

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
}
