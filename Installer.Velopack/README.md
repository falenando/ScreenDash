# Installer.Velopack

Este projeto empacota o `PrivilegedService` e o `PrivilegedHelper` usando o Velopack oficial.

- Repositório: https://github.com/velopack/velopack
- Documentação: https://docs.velopack.io/

Fluxo esperado:
1. Publicar os projetos `RemoteSupport.Service` e `RemoteSupport.Service.Agent`.
2. Copiar os artefatos para a pasta de staging do Velopack.
3. Executar o CLI do Velopack (`vpk`) conforme a documentação para gerar o instalador.

Os scripts `install.ps1` e `uninstall.ps1` devem ser incluídos no pacote para registrar/remover o serviço Windows.
