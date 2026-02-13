# Copilot Instructions

## General Guidelines
- First general instruction
- Second general instruction
- Evitar frases meta nas respostas; manter tom direto e impessoal.

## Code Style
- Use specific formatting rules
- Follow naming conventions

## Project-Specific Rules
- Prefer implementar Windows.Graphics.Capture / DXGI Desktop Duplication + H.264 HW encoder + WebRTC para projetos LAN Windows 10/11.
- O HostApp escuta em TCP na porta 5050 (const Port = 5050); clientes na LAN devem conectar no IP do host usando TCP:5050 e enviar "REQUEST_STREAM" para iniciar o streaming de tela (protocolo: cabeçalho de 8 dígitos ASCII com tamanho do JPEG seguido pelos bytes do JPEG). Se a linha do handshake não for "HOST", encaminhar o comando do Viewer como InitialPayload. Se o Relay consumir a linha "REQUEST_STREAM" e não a repassar ao Host, deve encaminhar essa linha como payload inicial.
- Se a porta TCP 5050 estiver disponível no Host, gerar o access code com o último octeto do IP do Host; caso contrário, gerar com o último octeto do IP do servidor Relay definido em hostconfig.json. O ServerRelay permanecerá como está (uma sessão por vez, sem multiplexação).
- Workspace targets .NET 10 e ServerRelay devem ser publicados para linux-x64 ou usar o runtime .NET 10 instalado. Utilize BackgroundService quando implementar serviços em background e Worker Service (.NET 10).
- Implementar um serviço Windows seguro para UAC chamado 'RemoteSupport.Service' utilizando Named Pipes IPC e .NET 10 BackgroundService. O projeto de instalação deve ser 'Installer.Velopack'. O HostApp deve permanecer como uma interface WinForms, reutilizando o HostApp/ViewerApp/ServerRelay existentes; garantir que o serviço seja executado como LocalSystem e continue durante o UAC. Além disso, um CaptureAgent por sessão deve ser iniciado em sessões interativas usando WTSQueryUserToken/CreateProcessAsUser para capturar a área de trabalho UAC/segura. **Certifique-se de habilitar privilégios no token para CreateProcessAsUser, pois o usuário relatou erro de elevação ao iniciar o CaptureAgent no PrivilegedWorker.**
- Use o repositório oficial do Velopack para o instalador; prefira um processo separado para o CaptureAgent; o serviço deve ser executado como LocalSystem.
- A correção para não reexecutar TryEnableService ao conectar o Viewer resolveu o problema de closed pipe; mantenha a conexão do pipe estável e evite Dispose/reconnect em momentos críticos.
- Regra prática: não chame `TryEnableService()` (nem faça `Dispose()` do `_serviceClient`) durante uma sessão ativa/streaming ou no instante em que o Viewer conecta; isso pode fechar o named pipe e derrubar o input no UAC.
