using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PokeIdleMulti;

/// <summary>
/// Encapsula uma instância isolada do Pokeidle.io (container UI, mini-barra e WebView2 com perfil próprio).
/// </summary>
public sealed class InstanceSlot : IDisposable
{
    // PNG 1x1 transparente (68 bytes) usado no modo Sem Sprites para zerar memória de texturas
    private static readonly byte[] Transparent1x1Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x60, 0x00, 0x02, 0x00,
        0x00, 0x05, 0x00, 0x01, 0x7A, 0x5E, 0xAB, 0x3F,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44,
        0xAE, 0x42, 0x60, 0x82
    ];

    private const string DocumentBootScript = """
        (() => {
          if (window.__pokeIdleMultiBootstrapped) return;
          window.__pokeIdleMultiBootstrapped = true;

          // 1. Mantém a página sempre visível para impedir throttling de timers/WebSocket pelo jogo
          try {
            Object.defineProperty(document, 'hidden', { configurable: true, get: () => false });
            Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'visible' });
          } catch {}

          const origDocAdd = document.addEventListener.bind(document);
          origDocAdd('visibilitychange', (ev) => {
            ev.stopImmediatePropagation();
          }, true);

          document.addEventListener = function(type, listener, options) {
            if (type === 'visibilitychange' || type === 'webkitvisibilitychange') {
              return;
            }
            return origDocAdd(type, listener, options);
          };

          // 2. Otimizações iniciais do próprio jogo para reduzir DOM/reflows
          try {
            if (localStorage.getItem('cfg-otimizado') === null) localStorage.setItem('cfg-otimizado', '1');
            if (localStorage.getItem('cfg-night') === null) localStorage.setItem('cfg-night', '1');
            if (localStorage.getItem('cfg-chat-sem-anuncios') === null) localStorage.setItem('cfg-chat-sem-anuncios', '1');
            if (localStorage.getItem('cfg-ocultar-conquistas') === null) localStorage.setItem('cfg-ocultar-conquistas', '1');
          } catch {}

          // 3. API de controle para Low-GPU, Sem Sprites, Só Hunt (/hunt), Economia e Ultra
          const LOW_GPU_STYLE_ID = '__pokeidle_low_gpu';
          const LOW_GPU_CSS = `
            *, *::before, *::after {
              animation: none !important;
              animation-duration: 0s !important;
              transition: none !important;
              transition-duration: 0s !important;
              box-shadow: none !important;
              text-shadow: none !important;
              filter: none !important;
              backdrop-filter: none !important;
            }
          `;

          const HUNT_ONLY_STYLE_ID = '__pokeidle_hunt_only';
          const HUNT_ONLY_CSS = `
            html.modo-imersivo .col.esq,
            html.modo-imersivo .col.dir,
            html.modo-imersivo .menu-topo,
            html.modo-imersivo .rodape-fan-jogo {
              display: none !important;
            }
          `;

          const NO_SPRITES_STYLE_ID = '__pokeidle_no_sprites';
          const NO_SPRITES_CSS = `
            img, .ptl-palco, .la-faiscas, .da-faiscas, .la-raios, .da-raios {
              opacity: 0 !important;
            }
          `;

          const desiredState = {
            lowGpu: false,
            eco: false,
            ultra: false,
            huntOnly: false,
            noSprites: false
          };

          const setStyleElement = (id, css, enable) => {
            const root = document.head || document.documentElement;
            if (!root) return;
            let style = document.getElementById(id);
            if (enable) {
              if (!style) {
                style = document.createElement('style');
                style.id = id;
                style.textContent = css;
                root.appendChild(style);
              }
            } else if (style) {
              style.remove();
            }
          };

          const isVisibleEl = (el) => !!el && !el.classList.contains('hidden') && el.offsetParent !== null;

          const clickConfigToggle = (toggleId, desired) => {
            const appEl = document.getElementById('app');
            if (!appEl || appEl.classList.contains('hidden')) return false;

            const modal = document.getElementById('modal');
            const alreadyOpen = modal && !modal.classList.contains('hidden');
            let toggle = document.getElementById(toggleId);

            if (!toggle) {
              const btnCfg = document.getElementById('btn-config');
              if (!btnCfg) return false;
              btnCfg.click();
              toggle = document.getElementById(toggleId);
            }

            if (toggle) {
              const isOn = toggle.classList.contains('on') || toggle.getAttribute('aria-checked') === 'true';
              if (isOn !== desired) {
                toggle.click();
              }
            }

            if (!alreadyOpen) {
              const m = document.getElementById('modal');
              if (m && !m.classList.contains('hidden')) {
                document.getElementById('modal-fechar')?.click();
              }
            }
            return true;
          };

          const applyAllDesired = () => {
            api.setLowGpuCss(desiredState.lowGpu);
            api.setNoSpritesCss(desiredState.noSprites);
            api.setHuntOnly(desiredState.huntOnly);
            api.setEco(desiredState.eco);
            api.setUltra(desiredState.ultra);
          };

          const watchLoginTransition = () => {
            const appEl = document.getElementById('app');
            if (!appEl) return;
            if (!appEl.classList.contains('hidden')) return;

            const observer = new MutationObserver(() => {
              if (!appEl.classList.contains('hidden')) {
                observer.disconnect();
                setTimeout(applyAllDesired, 150);
              }
            });
            observer.observe(appEl, { attributes: true, attributeFilter: ['class'] });
          };

          if (document.readyState === 'loading') {
            origDocAdd('DOMContentLoaded', watchLoginTransition, { once: true });
          } else {
            watchLoginTransition();
          }

          const api = {
            setLowGpuCss(enable) {
              desiredState.lowGpu = !!enable;
              setStyleElement(LOW_GPU_STYLE_ID, LOW_GPU_CSS, desiredState.lowGpu);
            },

            setNoSpritesCss(enable) {
              desiredState.noSprites = !!enable;
              setStyleElement(NO_SPRITES_STYLE_ID, NO_SPRITES_CSS, desiredState.noSprites);
            },

            setHuntOnly(enable) {
              desiredState.huntOnly = !!enable;
              try {
                localStorage.setItem('cfg-imersivo', desiredState.huntOnly ? '1' : '0');
              } catch {}
              document.documentElement.classList.toggle('modo-imersivo', desiredState.huntOnly);
              setStyleElement(HUNT_ONLY_STYLE_ID, HUNT_ONLY_CSS, desiredState.huntOnly);
            },

            setEco(enable) {
              desiredState.eco = !!enable;
              const appEl = document.getElementById('app');
              const loggedIn = !!appEl && !appEl.classList.contains('hidden');
              if (!loggedIn) return;

              const ecoOn = document.documentElement.classList.contains('modo-economia') ||
                            localStorage.getItem('cfg-economia') === '1';
              if (ecoOn === desiredState.eco) return;

              if (desiredState.eco) {
                const huntBtn = document.getElementById('hunt-eco');
                const arenaBtn = document.getElementById('arena-eco');
                if (isVisibleEl(huntBtn)) { huntBtn.click(); return; }
                if (isVisibleEl(arenaBtn)) { arenaBtn.click(); return; }
                clickConfigToggle('cfg-economia', true);
              } else {
                const sairBtn = document.getElementById('eco-sair');
                if (isVisibleEl(sairBtn)) { sairBtn.click(); return; }
                clickConfigToggle('cfg-economia', false);
              }
            },

            setUltra(enable) {
              desiredState.ultra = !!enable;
              const appEl = document.getElementById('app');
              const loggedIn = !!appEl && !appEl.classList.contains('hidden');
              if (!loggedIn) return;

              const ultraOn = document.documentElement.classList.contains('modo-ultra') ||
                              localStorage.getItem('cfg-ultra') === '1';
              if (ultraOn === desiredState.ultra) return;

              if (!desiredState.ultra) {
                const sairBtn = document.getElementById('ultra-sair');
                if (isVisibleEl(sairBtn)) { sairBtn.click(); return; }
              }
              clickConfigToggle('cfg-ultra', desiredState.ultra);
            }
          };

          window.__pokeIdleMulti = api;

          // 4. Encaminha atalhos do launcher mesmo quando o foco estiver no canvas/jogo
          window.addEventListener('keydown', (e) => {
            if (!window.chrome?.webview) return;
            if (e.key === 'F4' && !e.altKey && !e.ctrlKey && !e.shiftKey) {
              e.preventDefault();
              window.chrome.webview.postMessage('shortcut:toggle-grid');
            } else if (e.altKey && !e.ctrlKey && (e.key === 'g' || e.key === 'G')) {
              e.preventDefault();
              window.chrome.webview.postMessage('shortcut:toggle-grid');
            } else if (e.altKey && !e.ctrlKey && ['1', '2', '3', '4'].includes(e.key)) {
              e.preventDefault();
              window.chrome.webview.postMessage('shortcut:tab:' + e.key);
            } else if (e.altKey && !e.ctrlKey && (e.key === 'e' || e.key === 'E')) {
              e.preventDefault();
              window.chrome.webview.postMessage('shortcut:toggle-eco');
            } else if (e.altKey && !e.ctrlKey && (e.key === 'm' || e.key === 'M')) {
              e.preventDefault();
              window.chrome.webview.postMessage('shortcut:toggle-mute');
            } else if (e.altKey && !e.ctrlKey && (e.key === 'h' || e.key === 'H')) {
              e.preventDefault();
              window.chrome.webview.postMessage('shortcut:toggle-hunt');
            } else if (e.altKey && !e.ctrlKey && (e.key === 's' || e.key === 'S')) {
              e.preventDefault();
              window.chrome.webview.postMessage('shortcut:toggle-sprites');
            } else if (e.altKey && !e.ctrlKey && (e.key === 'a' || e.key === 'A')) {
              e.preventDefault();
              window.chrome.webview.postMessage('shortcut:enter-afk');
            }
          }, true);
        })();
        """;

    public int Index { get; }
    public string ProfileName => $"PokeIdle_Instance_{Index + 1}";

    public Panel ContainerPanel { get; }
    public Panel HeaderPanel { get; }
    public Label TitleLabel { get; }
    public Button HuntButton { get; }
    public Button EcoButton { get; }
    public Button UltraButton { get; }
    public Button MuteButton { get; }
    public Button ReloadButton { get; }
    public Button FocusButton { get; }
    public WebView2 WebView { get; }

    public bool IsInitialized { get; private set; }
    public bool ManualEco { get; set; }
    public bool ManualUltra { get; set; }
    public bool ManualHuntOnly { get; set; }
    public bool ManualNoSprites { get; set; }

    public bool EffectiveEco { get; private set; }
    public bool EffectiveUltra { get; private set; }
    public bool EffectiveHuntOnly { get; private set; }
    public bool EffectiveNoSprites { get; private set; }
    public bool IsMuted { get; private set; } = true;

    public event Action<int>? HuntToggled;
    public event Action<int>? EcoToggled;
    public event Action<int>? UltraToggled;
    public event Action<int>? MuteToggled;
    public event Action<int>? ReloadRequested;
    public event Action<int>? FocusRequested;
    public event Action<int>? RenameRequested;
    public event Action<string>? ShortcutReceived;

    private string _displayName;
    private string _statusText = "Aguardando...";

    public InstanceSlot(int index, string displayName)
    {
        Index = index;
        _displayName = displayName;

        ContainerPanel = new Panel
        {
            BackColor = Color.FromArgb(22, 13, 19),
            Padding = new Padding(1),
            Margin = Padding.Empty
        };

        HeaderPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 28,
            BackColor = Color.FromArgb(36, 20, 30),
            Padding = new Padding(6, 3, 4, 3)
        };

        TitleLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(240, 232, 237),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Cursor = Cursors.Hand
        };
        TitleLabel.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
                RenameRequested?.Invoke(Index);
        };

        var actionsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        HuntButton = CreateMiniButton("👁️", "Modo Imersivo: oculta chat e menus laterais desta instância");
        HuntButton.Width = 28;
        EcoButton = CreateMiniButton("⚡ Eco", "Alternar Modo Economia (Low CPU/GPU) desta instância");
        UltraButton = CreateMiniButton("🔋 Ultra", "Alternar Modo Ultra Economia (10s/frame) desta instância");
        MuteButton = CreateMiniButton("🔇", "Silenciar / Ativar áudio desta instância");
        MuteButton.Width = 30;
        ReloadButton = CreateMiniButton("↻", "Recarregar esta instância");
        ReloadButton.Width = 26;
        FocusButton = CreateMiniButton("⤢", "Focar esta instância em tela cheia (Modo Abas)");
        FocusButton.Width = 26;

        HuntButton.Click += (_, _) => HuntToggled?.Invoke(Index);
        EcoButton.Click += (_, _) => EcoToggled?.Invoke(Index);
        UltraButton.Click += (_, _) => UltraToggled?.Invoke(Index);
        MuteButton.Click += (_, _) => MuteToggled?.Invoke(Index);
        ReloadButton.Click += (_, _) => ReloadRequested?.Invoke(Index);
        FocusButton.Click += (_, _) => FocusRequested?.Invoke(Index);

        actionsFlow.Controls.Add(HuntButton);
        actionsFlow.Controls.Add(EcoButton);
        actionsFlow.Controls.Add(UltraButton);
        actionsFlow.Controls.Add(MuteButton);
        actionsFlow.Controls.Add(ReloadButton);
        actionsFlow.Controls.Add(FocusButton);

        HeaderPanel.Controls.Add(TitleLabel);
        HeaderPanel.Controls.Add(actionsFlow);

        WebView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(22, 13, 19)
        };

        ContainerPanel.Controls.Add(WebView);
        ContainerPanel.Controls.Add(HeaderPanel);

        UpdateTitleLabel();
        UpdateButtonStyles();
    }

    public async Task InitializeAsync(
        CoreWebView2Environment environment,
        string targetUrl,
        bool initialMuted,
        string? huntExtensionDir = null)
    {
        if (IsInitialized) return;

        IsMuted = initialMuted;
        SetStatus("Carregando...");

        var options = environment.CreateCoreWebView2ControllerOptions();
        options.ProfileName = ProfileName;
        options.IsInPrivateModeEnabled = false;
        options.ScriptLocale = "pt-BR";

        await WebView.EnsureCoreWebView2Async(environment, options);

        var core = WebView.CoreWebView2;
        core.IsMuted = IsMuted;
        core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = true;

        if (!string.IsNullOrEmpty(huntExtensionDir) && Directory.Exists(huntExtensionDir))
        {
            try
            {
                var loadedExts = await core.Profile.GetBrowserExtensionsAsync();
                bool alreadyLoaded = loadedExts.Any(ext =>
                    ext.Name.Contains("hunt", StringComparison.OrdinalIgnoreCase));
                if (!alreadyLoaded)
                    await core.Profile.AddBrowserExtensionAsync(huntExtensionDir);
            }
            catch
            {
                // Ignora se a extensão já estiver carregada ou se o runtime não suportar
            }
        }

        core.AddWebResourceRequestedFilter("*cloudflareinsights.com*", CoreWebView2WebResourceContext.Script);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Media);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Image);

        core.WebResourceRequested += (_, args) => OnWebResourceRequested(core, args);

        core.WebMessageReceived += (_, args) =>
        {
            try
            {
                var msg = args.TryGetWebMessageAsString();
                if (!string.IsNullOrEmpty(msg))
                    ShortcutReceived?.Invoke(msg);
            }
            catch
            {
                // Ignora mensagens que não sejam string
            }
        };

        core.NavigationStarting += (_, args) =>
        {
            if (AppConfig.TryRewriteHuntUrl(args.Uri, out var rewrittenUrl))
            {
                args.Cancel = true;
                ManualHuntOnly = true;
                core.Navigate(rewrittenUrl);
                return;
            }
            SetStatus("Conectando...");
        };

        core.NavigationCompleted += async (_, args) =>
        {
            SetStatus(args.IsSuccess ? "Online" : $"Erro ({args.WebErrorStatus})");
            await SyncVisualModesAsync();
        };

        await core.AddScriptToExecuteOnDocumentCreatedAsync(DocumentBootScript);
        core.Navigate(targetUrl);

        IsInitialized = true;
        UpdateButtonStyles();
    }

    private void OnWebResourceRequested(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (args.ResourceContext == CoreWebView2WebResourceContext.Script)
        {
            args.Response = core.Environment.CreateWebResourceResponse(Stream.Null, 204, "No Content", "");
            return;
        }

        if (args.ResourceContext == CoreWebView2WebResourceContext.Media && IsMuted)
        {
            args.Response = core.Environment.CreateWebResourceResponse(Stream.Null, 204, "No Content", "");
            return;
        }

        if (args.ResourceContext == CoreWebView2WebResourceContext.Image && EffectiveNoSprites)
        {
            if (args.Request.Uri.Contains("pokeidle.io", StringComparison.OrdinalIgnoreCase))
            {
                var stream = new MemoryStream(Transparent1x1Png, writable: false);
                args.Response = core.Environment.CreateWebResourceResponse(
                    stream,
                    200,
                    "OK",
                    "Content-Type: image/png");
            }
        }
    }

    public void SetDisplayName(string name)
    {
        _displayName = name;
        UpdateTitleLabel();
    }

    public void SetActiveHighlight(bool isActive, bool isGridMode)
    {
        ContainerPanel.BackColor = isActive
            ? Color.FromArgb(214, 59, 89)
            : Color.FromArgb(52, 30, 44);
        FocusButton.Visible = isGridMode;
    }

    public void SetMuted(bool muted)
    {
        IsMuted = muted;
        if (WebView.CoreWebView2 is not null)
            WebView.CoreWebView2.IsMuted = muted;
        UpdateButtonStyles();
    }

    public void Navigate(string url)
    {
        if (WebView.CoreWebView2 is not null)
            WebView.CoreWebView2.Navigate(url);
    }

    public void Reload()
    {
        if (WebView.CoreWebView2 is not null)
            WebView.Reload();
    }

    public async Task ApplyOptimizationStateAsync(
        bool effectiveEco,
        bool effectiveUltra,
        bool effectiveHuntOnly,
        bool effectiveNoSprites)
    {
        EffectiveEco = effectiveEco;
        EffectiveUltra = effectiveUltra;
        EffectiveHuntOnly = effectiveHuntOnly;
        EffectiveNoSprites = effectiveNoSprites;
        UpdateButtonStyles();

        if (!IsInitialized || WebView.CoreWebView2 is null)
            return;

        await SyncVisualModesAsync(effectiveEco, effectiveUltra, effectiveHuntOnly, effectiveNoSprites);
    }

    private Task SyncVisualModesAsync()
        => SyncVisualModesAsync(EffectiveEco, EffectiveUltra, EffectiveHuntOnly, EffectiveNoSprites);

    private async Task SyncVisualModesAsync(bool eco, bool ultra, bool huntOnly, bool noSprites)
    {
        if (WebView.CoreWebView2 is null) return;

        try
        {
            bool lowGpu = eco || ultra || noSprites;
            string lowGpuArg = lowGpu ? "true" : "false";
            string noSpritesArg = noSprites ? "true" : "false";
            string huntOnlyArg = huntOnly ? "true" : "false";
            string ecoArg = eco ? "true" : "false";
            string ultraArg = ultra ? "true" : "false";

            await WebView.ExecuteScriptAsync(
                $"window.__pokeIdleMulti?.setLowGpuCss({lowGpuArg});" +
                $"window.__pokeIdleMulti?.setNoSpritesCss({noSpritesArg});" +
                $"window.__pokeIdleMulti?.setHuntOnly({huntOnlyArg});" +
                $"window.__pokeIdleMulti?.setEco({ecoArg});" +
                $"window.__pokeIdleMulti?.setUltra({ultraArg});");
        }
        catch
        {
            // Página pode estar em transição de navegação
        }
    }

    public void ApplyLowMemoryTarget()
    {
        if (WebView.CoreWebView2 is not null)
            WebView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
    }

    private void SetStatus(string status)
    {
        _statusText = status;
        UpdateTitleLabel();
    }

    private void UpdateTitleLabel()
    {
        TitleLabel.Text = $"[{Index + 1}] {_displayName} · {_statusText}";
    }

    private void UpdateButtonStyles()
    {
        HuntButton.BackColor = EffectiveHuntOnly ? Color.FromArgb(138, 46, 128) : Color.FromArgb(54, 34, 47);
        HuntButton.ForeColor = EffectiveHuntOnly ? Color.White : Color.FromArgb(215, 200, 209);

        EcoButton.BackColor = EffectiveEco ? Color.FromArgb(26, 118, 72) : Color.FromArgb(54, 34, 47);
        EcoButton.ForeColor = EffectiveEco ? Color.White : Color.FromArgb(215, 200, 209);

        UltraButton.BackColor = EffectiveUltra ? Color.FromArgb(176, 106, 16) : Color.FromArgb(54, 34, 47);
        UltraButton.ForeColor = EffectiveUltra ? Color.White : Color.FromArgb(215, 200, 209);

        MuteButton.Text = IsMuted ? "🔇" : "🔊";
        MuteButton.BackColor = IsMuted ? Color.FromArgb(54, 34, 47) : Color.FromArgb(47, 112, 168);
        MuteButton.ForeColor = Color.White;
    }

    private static Button CreateMiniButton(string text, string tooltip)
    {
        var btn = new Button
        {
            Text = text,
            Height = 22,
            Width = 56,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(54, 34, 47),
            ForeColor = Color.FromArgb(215, 200, 209),
            Font = new Font("Segoe UI", 7.8f, FontStyle.Bold),
            Margin = new Padding(2, 0, 0, 0),
            Padding = Padding.Empty,
            Cursor = Cursors.Hand,
            TabStop = false
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.AccessibleDescription = tooltip;
        return btn;
    }

    public void Dispose()
    {
        WebView.Dispose();
        ContainerPanel.Dispose();
    }
}
