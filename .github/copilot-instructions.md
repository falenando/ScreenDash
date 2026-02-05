# Copilot Instructions

## General Guidelines
- First general instruction
- Second general instruction

## Code Style
- Use specific formatting rules
- Follow naming conventions

## Project-Specific Rules
- Prefer implementar Windows.Graphics.Capture / DXGI Desktop Duplication + H.264 HW encoder + WebRTC para projetos LAN Windows 10/11.
- O HostApp escuta em TCP na porta 5050 (const Port = 5050); clientes na LAN devem conectar no IP do host usando TCP:5050 e enviar "REQUEST_STREAM" para iniciar o streaming de tela (protocolo: cabeçalho de 8 dígitos ASCII com tamanho do JPEG seguido pelos bytes do JPEG). Se a linha do handshake não for "HOST", encaminhar o comando do Viewer como InitialPayload. Se o Relay consumir a linha "REQUEST_STREAM" e não a repassar ao Host, deve encaminhar essa linha como payload inicial.
- Se a porta TCP 5050 estiver disponível no Host, gerar o access code com o último octeto do IP do Host; caso contrário, gerar com o último octeto do IP do servidor Relay definido em hostconfig.json. O ServerRelay permanecerá como está (uma sessão por vez, sem multiplexação).
- Workspace targets .NET 10 and ServerRelay should be published for linux-x64 or use installed .NET 10 runtime.
- Implementar um serviço Windows seguro para UAC chamado 'RemoteSupport.Service' utilizando Named Pipes IPC e .NET 10 BackgroundService. O projeto de instalação deve ser 'Installer.Velopack'. O HostApp deve permanecer como uma interface WinForms, reutilizando o HostApp/ViewerApp/ServerRelay existentes; garantir que o serviço seja executado como LocalSystem e continue durante o UAC. Além disso, um CaptureAgent por sessão deve ser iniciado em sessões interativas usando WTSQueryUserToken/CreateProcessAsUser para capturar a área de trabalho UAC/segura.
- Use o repositório oficial do Velopack para o instalador; prefira um processo separado para o CaptureAgent; o serviço deve ser executado como LocalSystem.