# CosmicCraft

Plugin comunitario para Dalamud, voltado ao crafting em Cosmic Exploration. Gera macros e permite executar uma fabricacao por vez, com monitoramento e parada de seguranca.

## Instalacao

Apos publicar uma release e atualizar `repo.json` com URLs publicas HTTPS, adicione a URL publica de `repo.json` em `/xlsettings` > Experimental > Custom Plugin Repositories. Depois instale pelo `/xlplugins`.

## Desenvolvimento

Requer o ambiente Dalamud e .NET correspondente ao projeto. Compile com `dotnet build -c Release`. O nome interno da pasta e do assembly ainda e `SamplePlugin`; isso e intencional nesta etapa para evitar alterar a logica testada. Nao renomeie DLL ou arquivos de manifesto isoladamente.

## Aviso

Plugin nao oficial. A automacao pode contrariar os Termos de Servico de FFXIV. Use por sua conta e risco. Nao ha afiliacao com Square Enix ou Dalamud.

## Publicacao

Veja `PUBLICAR.md`. O `repo.json` fornecido e um MODELO e nao deve ser publicado antes de substituir os URLs e validar o pacote compilado.
