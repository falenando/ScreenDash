param(
    [string]$PublishDir = "..\artifacts\HostApp-publish",
    [string]$Version = "1.0.0",
    [string]$Channel = "stable",
    [string]$PackId = "ScreenDash.HostApp",
    [string]$ExeName = "HostApp.exe"
)

if (-not (Test-Path $PublishDir)) {
    Write-Error "Publish directory not found: $PublishDir"
    exit 1
}

$dist = Join-Path $PSScriptRoot "dist"
if (-not (Test-Path $dist)) {
    New-Item -ItemType Directory -Path $dist | Out-Null
}

$exePath = Join-Path $PublishDir $ExeName
if (-not (Test-Path $exePath)) {
    Write-Error "Executable not found: $exePath"
    exit 1
}

# Garante que o vpk está disponível
$vpk = "vpk"
if (-not (Get-Command $vpk -ErrorAction SilentlyContinue)) {
    Write-Error "Velopack CLI (vpk) não encontrado. Instale com: dotnet tool install -g vpk"
    exit 1
}

# Empacota usando Velopack oficial
& $vpk pack `
    --packId $PackId `
    --packVersion $Version `
    --channel $Channel `
    --packTitle "ScreenDash Host" `
    --publisher "ScreenDash" `
    --appDir $PublishDir `
    --entryExecutable $ExeName `
    --outputDir $dist

if ($LASTEXITCODE -ne 0) {
    Write-Error "Falha ao empacotar com Velopack."
    exit $LASTEXITCODE
}

Write-Host "Pacotes gerados em: $dist" -ForegroundColor Green
