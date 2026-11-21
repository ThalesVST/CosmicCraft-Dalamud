# 🌌 CosmicCraft

**A crafting assistant for Final Fantasy XIV's Cosmic Exploration, built as a community Dalamud plugin.**

**Current release:** [v0.9.42.0](https://github.com/ThalesVST/CosmicCraft-Dalamud/releases/tag/v0.9.42.0) · [Português abaixo](#português)

> Unofficial third-party plugin. Not affiliated with or endorsed by Square Enix or the Dalamud project.

## Features

- In-game crafting assistant for **Cosmic Exploration**
- Crafting macro generation
- Support for all crafting jobs
- Quality-focused crafting strategy
- Optional **single-run** macro execution — not continuous batch crafting
- Crafting monitoring and safety stop

## Installation

1. Install and launch FFXIV with Dalamud.
2. In the game chat, enter `/xlsettings`.
3. Open **Experimental → Custom Plugin Repositories**.
4. Add and enable this repository URL:

   ```text
   https://raw.githubusercontent.com/ThalesVST/CosmicCraft-Dalamud/main/repo.json
   ```

5. Open `/xlplugins`, find **CosmicCraft**, and install it.
6. Enter `/cosmiccraft` in chat to open the assistant.

**Download:** [Latest release](https://github.com/ThalesVST/CosmicCraft-Dalamud/releases/latest)

## Screenshots

Screenshots of the in-game interface will be added here. 

## Feedback and issues

Found a bug or have a suggestion? [Open an issue](https://github.com/ThalesVST/CosmicCraft-Dalamud/issues). Please include your plugin version, steps to reproduce, and any relevant error messages. Avoid sharing account or character information.

## Development

This project uses Dalamud and the .NET SDK required by the project. Build with:

```sh
dotnet build -c Release
```

The internal assembly/plugin name remains `SamplePlugin` for compatibility with the tested build. Do not rename the DLL or manifest files independently. See [PUBLICAR.md](PUBLICAR.md) for release instructions.

## Disclaimer

This is an unofficial community plugin. Third-party tools and automation may violate FFXIV's Terms of Service and can carry account risks. Use at your own risk. No affiliation with Square Enix or Dalamud is implied.

---

## Português

**CosmicCraft** é um assistente de crafting para a **Exploração Cósmica (Cosmic Exploration)** de Final Fantasy XIV, desenvolvido como plugin comunitário para Dalamud.

**Versão atual:** [v0.9.42.0](https://github.com/ThalesVST/CosmicCraft-Dalamud/releases/tag/v0.9.42.0)

### Funcionalidades

- Interface dentro do jogo, voltada à Exploração Cósmica
- Geração de macros de crafting
- Suporte a todas as profissões de criação
- Estratégia focada em qualidade
- Execução opcional da macro **uma vez por acionamento**, sem produção consecutiva
- Monitoramento da fabricação e parada de segurança

### Instalação

1. Abra o FFXIV com o Dalamud.
2. Digite `/xlsettings` no chat.
3. Acesse **Experimental → Custom Plugin Repositories**.
4. Adicione e habilite a URL:

   ```text
   https://raw.githubusercontent.com/ThalesVST/CosmicCraft-Dalamud/main/repo.json
   ```

5. Abra `/xlplugins`, procure **CosmicCraft** e instale.
6. Digite `/cosmiccraft` para abrir o assistente.

**Download:** [Versão mais recente](https://github.com/ThalesVST/CosmicCraft-Dalamud/releases/latest)

### Imagens

Capturas da interface no jogo serão adicionadas aqui.

### Problemas e sugestões

Encontrou algum problema ou tem uma sugestão? [Abra uma issue](https://github.com/ThalesVST/CosmicCraft-Dalamud/issues). Informe a versão do plugin, como reproduzir o problema e eventuais mensagens de erro. Evite compartilhar dados da sua conta ou personagem.

### Desenvolvimento

Requer o ambiente Dalamud e o SDK .NET correspondente ao projeto. Para compilar:

```sh
dotnet build -c Release
```

O nome interno `SamplePlugin` foi mantido para preservar a compatibilidade da versão testada. Não renomeie a DLL ou os manifestos isoladamente. Consulte [PUBLICAR.md](PUBLICAR.md).

### Aviso

Plugin comunitário não oficial, sem afiliação à Square Enix ou ao projeto Dalamud. O uso de ferramentas de terceiros e automação pode contrariar os Termos de Serviço de FFXIV e envolver riscos para a conta. Use por sua conta e risco.
