# 🎮 PokeIdle Multi — 4x Ultraleve

<p align="center">
  <strong>Gerenciador desktop ultraleve para rodar 4 sessões totalmente isoladas de <a href="https://pokeidle.io/">pokeidle.io</a> em uma única janela — com consumo mínimo de CPU, RAM e GPU.</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0%20Windows-512BD4?style=flat-square&logo=dotnet" alt=".NET 10" />
  <img src="https://img.shields.io/badge/WebView2-Edge%20Chromium-0078D7?style=flat-square&logo=microsoftedge" alt="WebView2" />
  <img src="https://img.shields.io/badge/Inst%C3%A2ncias-4%20Perfis%20Isolados-E63956?style=flat-square" alt="4 Perfis" />
  <img src="https://img.shields.io/badge/Extens%C3%A3o-%2Fhunt%20Integrada-2EA043?style=flat-square" alt="/hunt integrada" />
</p>

---

## ✨ Por que usar o PokeIdle Multi?

Abrir 4 navegadores ou 4 janelas anônimas comuns consome muita memória RAM, disputa placa de vídeo com outros jogos e ainda sofre *throttling* quando você minimiza ou troca de janela.

O **PokeIdle Multi** resolve tudo isso em um executável nativo leve:

- 🔒 **4 Perfis 100% Isolados**: Cada aba utiliza um `ProfileName` próprio (`PokeIdle_Instance_1` a `4`), mantendo contas, logins, cookies, saves e `localStorage` totalmente separados entre si e separados do seu navegador principal.
- 🧩 **Motor Compartilhado**: As 4 instâncias compartilham o mesmo processo base do WebView2 com `--renderer-process-limit=4` e cache enxuto, consumindo muito menos RAM que 4 navegadores separados sem travar a interface ou a extensão `/hunt`.
- ⚡ **Anti-Throttling Real**: Flags nativas do Chromium + override de `document.hidden` e `visibilityState` mantêm os timers e o WebSocket rodando a 100% mesmo com a janela minimizada ou em segundo plano.
- 🧭 **Extensão `/hunt for PokeIdle` Integrada**: Carrega automaticamente a extensão **`/hunt`** (*ONDE CAÇAR? made by Liang*) nas 4 contas para calcular XP/h, ouro/h, risco e tempo de abate direto na tela.
- 🪟 **Abas ↔ Grade 2x2 Instantâneo**: Alterne entre ver 1 conta em tela cheia ou as 4 juntas em grade 2x2 sem recarregar nenhuma página.
- 👻 **Modo AFK Fantasma (Bandeja)**: Oculta a janela perto do relógio do Windows (*System Tray*), desliga 100% da renderização das 4 telas, ativa Economia + Ultra, reduz a prioridade de CPU para `Idle` e limpa a RAM.
- ⚙️ **Prioridade `BelowNormal` + Auto-Limpeza**: O app e seus subprocessos `msedgewebview2.exe` rodam em prioridade abaixo do normal para nunca causar *stuttering* no seu jogo principal, com limpeza automática de Working Set a cada 60 segundos.

---

## 🚀 Como Usar (Download Pronto)

1. Baixe o `.zip` mais recente na página de **[Releases](../../releases)**.
2. Extraia em uma pasta de sua preferência (ex.: `C:\Jogos\PokeIdleMulti`).
3. Execute **`PokeIdleMulti.exe`**.

> **Requisito**: Windows 10/11 com **Microsoft Edge WebView2 Runtime** (já vem instalado por padrão no Windows 11 e na maioria dos Windows 10).

---

## 🛠️ Compilando do Código-Fonte

Requer **.NET 10 SDK** (Windows):

```powershell
git clone https://github.com/sekyrA1/PokeIdleInstances.git
cd PokeIdleInstances
dotnet build -c Release
```

Ou simplesmente dê um duplo clique em **`Iniciar_PokeIdle_4x.bat`**, que compila automaticamente na primeira execução e abre o programa.

---

## 🎛️ Controles e Modos de Economia

| Botão | Função |
| :--- | :--- |
| **`[1]` .. `[4]`** | Alterna entre as 4 contas isoladas (**Clique direito** para renomear a conta) |
| **`⊞ Grade` / `▭ Abas`** | Alterna entre visualização em abas ou Grade 2x2 simultânea |
| **`👁️ Foco`** | Oculta chat, colunas laterais e menus para focar apenas na caçada (**Clique direito** para configurar URL) |
| **`⚡ Eco`** | Aciona o **Modo Economia** nativo do jogo (desliga `<canvas>`) + remove animações/sombras CSS (**Clique direito** liga/desliga Auto-Eco nas abas inativas) |
| **`🔋 Ultra`** | Aciona o **Modo Ultra Economia** nativo do jogo (atualiza os números apenas a cada 10s) |
| **`🖼️ Sprites`** | Substitui imagens do jogo por 1x1 transparente em memória para economia extrema de RAM |
| **`👻 AFK`** | Envia o app para a bandeja do sistema com renderização desligada e prioridade `Idle` |
| **`🔇 Mudo` / `🔊 Som`** | Silencia (padrão) ou ativa o áudio globalmente ou por instância |
| **`🧹 RAM`** | Executa redução imediata de *Working Set* (RAM física) em toda a árvore de processos |
| **`↻`** | Recarrega a aba ativa (`Shift + Clique` recarrega todas as 4) |

---

## ⌨️ Atalhos de Teclado

- `Alt + 1` .. `Alt + 4` — Seleciona a Conta 1 a 4
- `F4` ou `Alt + G` — Alterna entre **Modo Abas** e **Grade 2x2**
- `Alt + H` — Liga/desliga **Modo Imersivo (`👁️ Foco`)**
- `Alt + E` — Liga/desliga **Modo Eco** em todas
- `Alt + S` — Liga/desliga **Modo Sem Sprites** em todas
- `Alt + A` — Entra no **Modo AFK Fantasma** (bandeja)
- `Alt + M` — Silencia/ativa o áudio de todas as instâncias

---

## 🔐 Privacidade e Dados Locais

- Seus logins, cookies e saves ficam armazenados **apenas localmente** na pasta `WebView2Data/` ao lado do executável.
- Suas preferências e nomes de contas ficam em `config.json` ao lado do executável.
- Ambos estão no `.gitignore` para nunca serem enviados em commits.

---

## 🙏 Créditos

- Jogo base: [pokeidle.io](https://pokeidle.io/)
- Extensão `/hunt for PokeIdle`: criada por **Liang**
