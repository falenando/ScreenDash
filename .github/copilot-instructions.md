# Copilot Instructions

## General Guidelines
- First general instruction
- Second general instruction

## Code Style
- Use specific formatting rules
- Follow naming conventions

## Project-Specific Rules
- Prefere implementar Windows.Graphics.Capture / DXGI Desktop Duplication + H.264 HW encoder + WebRTC para projetos LAN Windows 10/11.
- O HostApp escuta em TCP na porta 5050 (const Port = 5050); clientes na LAN devem conectar no IP do host usando TCP:5050 e enviar "REQUEST_STREAM" para iniciar o streaming de tela (protocolo: cabeçalho de 8 dígitos ASCII com tamanho do JPEG seguido pelos bytes do JPEG).
- Workspace targets .NET 10 and ServerRelay should be published for linux-x64 or use installed .NET 10 runtime.