# Publicar o CosmicCraft em repositorio personalizado Dalamud

1. Crie um repositorio GitHub para o codigo. Nao envie `.git`, `bin`, `obj`, arquivos pessoais, tokens ou logs.
2. Compile no Windows com `dotnet build SamplePlugin/SamplePlugin.csproj -c Release`.
3. Verifique a pasta gerada `SamplePlugin/bin/x64/Release/SamplePlugin/` (a estrutura pode variar conforme o SDK). Ela deve conter a DLL, o manifesto JSON gerado pelo SDK e dependencias necessarias. Se o SDK gerar um ZIP de distribuicao, prefira esse pacote. Nao compacte o codigo-fonte como se fosse o plugin.
4. Teste o pacote compilado numa instalacao de desenvolvimento do Dalamud antes de publicar.
5. Crie uma release GitHub com o ZIP compilado, por exemplo `CosmicCraft-v0.9.39.3.zip`.
6. Edite `repo.json`: substitua `SEU_USUARIO` e o nome do repositorio pelos reais. Confira o campo `DalamudApiLevel` no manifesto gerado para a build. Confirme que o link `DownloadLinkInstall` baixa exatamente o ZIP publicado.
7. Hospede `repo.json` em URL HTTPS publica, por exemplo `https://raw.githubusercontent.com/SEU_USUARIO/CosmicCraft-Dalamud/main/repo.json`.
8. Adicione essa URL em `/xlsettings` > Experimental > Custom Plugin Repositories, atualize os repositorios e procure CosmicCraft em `/xlplugins`.

IMPORTANTE: `repo.json` e um MODELO, nao esta pronto para publicacao. A versao `0.9.39.3` no csproj foi atualizada apenas para identificar o pacote; nao foi recompilada aqui. O nivel da API deve ser validado na build local. O arquivo `SamplePlugin.json` nao existe na raiz do projeto enviado; o SDK costuma gera-lo no build. Nao invente um manifesto de plugin com dados incorretos.
