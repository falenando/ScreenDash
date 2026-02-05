# HostApp Installer (Velopack)

Este diretório contém scripts para empacotar o `HostApp` com o Velopack oficial.

Requisitos:
- .NET 10 SDK instalado
- Velopack CLI (`vpk`) instalado:
  ```powershell
  dotnet tool install -g vpk
  ```
- Código já publicado do `HostApp` (win-x64)

Passos rápidos (PowerShell):
1. Publicar o HostApp (self-contained opcional):
   ```powershell
   dotnet publish ..\HostApp\HostApp.csproj -c Release -r win-x64 --self-contained false -o ..\artifacts\HostApp-publish
   ```
   > Troque `--self-contained true` se quiser embutir o runtime.

2. Empacotar com Velopack:
   ```powershell
   cd HostApp.Installer
   ./pack.ps1 -PublishDir "..\artifacts\HostApp-publish" -Version "1.0.0" -Channel "stable"
   ```

Saída esperada:
- Pacotes/instalador Velopack em `HostApp.Installer\dist` (ex: `.msi`/`.exe` gerados pelo `vpk pack`).

Parâmetros do `pack.ps1`:
- `-PublishDir`: caminho da pasta publicada do HostApp
- `-Version`: versão do app (ex: 1.0.0)
- `-Channel`: canal de distribuição (ex: stable/dev)
- `-PackId`: opcional, default `ScreenDash.HostApp`
- `-ExeName`: opcional, default `HostApp.exe`

Referências:
- Velopack oficial: https://github.com/velopack/velopack
- Docs: https://docs.velopack.io/
