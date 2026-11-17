using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace PokeIdleMulti;

/// <summary>
/// Janela principal do gerenciador ultraleve de 4 sessões isoladas do Pokeidle.io.
/// </summary>
public sealed class MainForm : Form
{
    private const string ChromiumAntiThrottleArgs =
        "--disable-background-timer-throttling " +
        "--disable-renderer-backgrounding " +
        "--disable-backgrounding-occluded-windows " +
        "--disable-ipc-flooding-protection " +
        "--disable-features=CalculateNativeWinOcclusion,IntensiveWakeUpThrottling,ThrottleDisplayNoneAndVisibilityHiddenCrossOriginIframes,AudioServiceOutOfProcess,Translate,OptimizationHints,MediaRouter " +
        "--renderer-process-limit=4 " +
        "--aggressive-cache-discard " +
        "--media-cache-size=1 " +
        "--disk-cache-size=10485760 " +
        "--disable-background-networking " +
        "--disable-component-update " +
        "--disable-domain-reliability " +
        "--disable-sync " +
        "--disable-breakpad " +
        "--no-pings";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly AppConfig _config;
    private readonly Panel _topBar;
    private readonly Panel _workspacePanel;
    private readonly Button[] _tabButtons = new Button[4];
    private readonly InstanceSlot[] _slots = new InstanceSlot[4];
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 8000, InitialDelay = 400, ReshowDelay = 200 };
    private readonly System.Windows.Forms.Timer _maintenanceTimer;
    private readonly NotifyIcon _trayIcon;
    private readonly Icon _trayNativeIcon;
    private readonly IntPtr _trayIconHandle;

    private readonly Button _layoutToggleButton;
    private readonly Button _globalHuntButton;
    private readonly Button _globalEcoButton;
    private readonly Button _globalUltraButton;
    private readonly Button _globalSpritesButton;
    private readonly Button _afkTrayButton;
    private readonly Button _globalMuteButton;
    private readonly Button _trimRamButton;
    private readonly Button _reloadButton;

    private CoreWebView2Environment? _sharedEnvironment;
    private bool _isInitializing;
    private bool _isAfkTrayMode;
    private FormWindowState _preAfkWindowState = FormWindowState.Normal;

    public MainForm()
    {
        _config = AppConfig.Load();

        Text = "PokeIdle Multi (4x Ultraleve)";
        Size = new Size(1380, 820);
        MinimumSize = new Size(1060, 620);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(15, 8, 13);
        ForeColor = Color.FromArgb(240, 232, 237);
        DoubleBuffered = true;

        (_trayNativeIcon, _trayIconHandle) = CreatePokeballTrayIcon();
        Icon = _trayNativeIcon;

        _trayIcon = new NotifyIcon
        {
            Icon = _trayNativeIcon,
            Text = "PokeIdle Multi — 4x AFK",
            Visible = false,
            ContextMenuStrip = BuildTrayContextMenu()
        };
        _trayIcon.MouseClick += async (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                await ExitAfkTrayModeAsync();
        };

        _topBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 42,
            BackColor = Color.FromArgb(28, 15, 23),
            Padding = new Padding(8, 6, 8, 6)
        };

        var tabsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            var tabBtn = CreateTopButton($"[{idx + 1}] {_config.InstanceNames[idx]}", 88);
            tabBtn.AutoEllipsis = true;
            tabBtn.Margin = new Padding(0, 0, 3, 0);
            tabBtn.Click += async (_, _) => await SwitchToTabAsync(idx);
            tabBtn.MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtons.Right)
                    PromptRenameInstance(idx);
            };
            _toolTip.SetToolTip(tabBtn, $"Ativar {_config.InstanceNames[idx]} (Alt+{idx + 1}). Clique direito para renomear.");
            _tabButtons[idx] = tabBtn;
            tabsFlow.Controls.Add(tabBtn);
        }

        var controlsFlow = new FlowLayoutPanel
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

        _layoutToggleButton = CreateTopButton("⊞ Grade", 78);
        _layoutToggleButton.Click += async (_, _) => await ToggleLayoutModeAsync();
        _toolTip.SetToolTip(_layoutToggleButton, "Alternar entre Modo Abas e Grade 2x2 (F4 ou Alt+G)");

        _globalHuntButton = CreateTopButton("👁️ Foco", 74);
        _globalHuntButton.Click += async (_, _) => await ToggleGlobalHuntOnlyAsync();
        _globalHuntButton.MouseUp += async (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
                await PromptEditUrlExtensionAsync();
        };
        _toolTip.SetToolTip(_globalHuntButton, $"Modo Imersivo: oculta chat/menus laterais (Alt+H). Clique direito: configurar rota ({_config.UrlExtension}).");

        _globalEcoButton = CreateTopButton("⚡ Eco", 78);
        _globalEcoButton.Click += async (_, _) => await ToggleGlobalEcoAsync();
        _globalEcoButton.MouseUp += async (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                _config.AutoEcoOnInactiveTabs = !_config.AutoEcoOnInactiveTabs;
                _config.Save();
                await SyncAllOptimizationsAsync();
                UpdateTopBarState();
                ShowTransientTitleStatus(_config.AutoEcoOnInactiveTabs
                    ? "Auto-Eco em abas inativas: ATIVADO"
                    : "Auto-Eco em abas inativas: DESATIVADO");
            }
        };
        _toolTip.SetToolTip(_globalEcoButton, "Ligar/Desligar Modo Economia + Low-GPU em todas (Alt+E). Clique direito: alternar Auto-Eco nas abas inativas.");

        _globalUltraButton = CreateTopButton("🔋 Ultra", 70);
        _globalUltraButton.Click += async (_, _) => await ToggleGlobalUltraAsync();
        _toolTip.SetToolTip(_globalUltraButton, "Ligar/Desligar Modo Ultra Economia (repintura a cada 10s) em todas as instâncias");

        _globalSpritesButton = CreateTopButton("🖼️ Sprites", 82);
        _globalSpritesButton.Click += async (_, _) => await ToggleGlobalNoSpritesAsync();
        _toolTip.SetToolTip(_globalSpritesButton, "Modo Sem Sprites: bloqueia download/renderização de imagens do jogo para economia extrema de RAM (Alt+S)");

        _afkTrayButton = CreateTopButton("👻 AFK", 66);
        _afkTrayButton.Click += async (_, _) => await EnterAfkTrayModeAsync();
        _toolTip.SetToolTip(_afkTrayButton, "Modo AFK Fantasma: oculta para a bandeja, zera renderização e reduz CPU para Idle (Alt+A)");

        _globalMuteButton = CreateTopButton("🔇 Mudo", 74);
        _globalMuteButton.Click += (_, _) => ToggleGlobalMute();
        _toolTip.SetToolTip(_globalMuteButton, "Silenciar ou ativar áudio em todas as 4 instâncias (Alt+M)");

        _trimRamButton = CreateTopButton("🧹 RAM", 68);
        _trimRamButton.Click += (_, _) => TrimMemoryNow();
        _toolTip.SetToolTip(_trimRamButton, "Reduz o uso de memória RAM (Working Set) do app e dos processos WebView2");

        _reloadButton = CreateTopButton("↻", 30);
        _reloadButton.Click += (_, _) => ReloadActiveOrAll((ModifierKeys & Keys.Shift) == Keys.Shift);
        _toolTip.SetToolTip(_reloadButton, "Recarregar aba ativa (Shift+Clique recarrega todas as 4)");

        controlsFlow.Controls.Add(_layoutToggleButton);
        controlsFlow.Controls.Add(_globalHuntButton);
        controlsFlow.Controls.Add(_globalEcoButton);
        controlsFlow.Controls.Add(_globalUltraButton);
        controlsFlow.Controls.Add(_globalSpritesButton);
        controlsFlow.Controls.Add(_afkTrayButton);
        controlsFlow.Controls.Add(_globalMuteButton);
        controlsFlow.Controls.Add(_trimRamButton);
        controlsFlow.Controls.Add(_reloadButton);

        _topBar.Controls.Add(tabsFlow);
        _topBar.Controls.Add(controlsFlow);

        _workspacePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 8, 13),
            Padding = Padding.Empty
        };
        _workspacePanel.Resize += (_, _) => UpdateWorkspaceLayout();

        for (int i = 0; i < 4; i++)
        {
            var slot = new InstanceSlot(i, _config.InstanceNames[i]);
            slot.HuntToggled += async idx => await ToggleSlotHuntOnlyAsync(idx);
            slot.EcoToggled += async idx => await ToggleSlotEcoAsync(idx);
            slot.UltraToggled += async idx => await ToggleSlotUltraAsync(idx);
            slot.MuteToggled += idx => ToggleSlotMute(idx);
            slot.ReloadRequested += idx => _slots[idx].Reload();
            slot.FocusRequested += async idx => await FocusSlotInTabModeAsync(idx);
            slot.RenameRequested += PromptRenameInstance;
            slot.ShortcutReceived += HandleWebShortcut;

            _toolTip.SetToolTip(slot.HuntButton, slot.HuntButton.AccessibleDescription);
            _toolTip.SetToolTip(slot.EcoButton, slot.EcoButton.AccessibleDescription);
            _toolTip.SetToolTip(slot.UltraButton, slot.UltraButton.AccessibleDescription);
            _toolTip.SetToolTip(slot.MuteButton, slot.MuteButton.AccessibleDescription);
            _toolTip.SetToolTip(slot.ReloadButton, slot.ReloadButton.AccessibleDescription);
            _toolTip.SetToolTip(slot.FocusButton, slot.FocusButton.AccessibleDescription);

            _slots[i] = slot;
            _workspacePanel.Controls.Add(slot.ContainerPanel);
        }

        Controls.Add(_workspacePanel);
        Controls.Add(_topBar);

        _maintenanceTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _maintenanceTimer.Tick += (_, _) => RunBackgroundMaintenance();

        UpdateTopBarState();
        UpdateWorkspaceLayout();

        Shown += async (_, _) => await InitializeWebViewsAsync();
        FormClosing += (_, _) =>
        {
            _maintenanceTimer.Stop();
            _maintenanceTimer.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _config.Save();
            foreach (var slot in _slots)
                slot.Dispose();
            _trayNativeIcon.Dispose();
            if (_trayIconHandle != IntPtr.Zero)
                DestroyIcon(_trayIconHandle);
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F4 || keyData == (Keys.Alt | Keys.G))
        {
            _ = ToggleLayoutModeAsync();
            return true;
        }
        if (keyData == (Keys.Alt | Keys.D1) || keyData == (Keys.Alt | Keys.NumPad1))
        {
            _ = SwitchToTabAsync(0);
            return true;
        }
        if (keyData == (Keys.Alt | Keys.D2) || keyData == (Keys.Alt | Keys.NumPad2))
        {
            _ = SwitchToTabAsync(1);
            return true;
        }
        if (keyData == (Keys.Alt | Keys.D3) || keyData == (Keys.Alt | Keys.NumPad3))
        {
            _ = SwitchToTabAsync(2);
            return true;
        }
        if (keyData == (Keys.Alt | Keys.D4) || keyData == (Keys.Alt | Keys.NumPad4))
        {
            _ = SwitchToTabAsync(3);
            return true;
        }
        if (keyData == (Keys.Alt | Keys.H))
        {
            _ = ToggleGlobalHuntOnlyAsync();
            return true;
        }
        if (keyData == (Keys.Alt | Keys.E))
        {
            _ = ToggleGlobalEcoAsync();
            return true;
        }
        if (keyData == (Keys.Alt | Keys.S))
        {
            _ = ToggleGlobalNoSpritesAsync();
            return true;
        }
        if (keyData == (Keys.Alt | Keys.A))
        {
            _ = EnterAfkTrayModeAsync();
            return true;
        }
        if (keyData == (Keys.Alt | Keys.M))
        {
            ToggleGlobalMute();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void HandleWebShortcut(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => HandleWebShortcut(message));
            return;
        }

        switch (message)
        {
            case "shortcut:toggle-grid":
                _ = ToggleLayoutModeAsync();
                break;
            case "shortcut:toggle-hunt":
                _ = ToggleGlobalHuntOnlyAsync();
                break;
            case "shortcut:toggle-eco":
                _ = ToggleGlobalEcoAsync();
                break;
            case "shortcut:toggle-sprites":
                _ = ToggleGlobalNoSpritesAsync();
                break;
            case "shortcut:enter-afk":
                _ = EnterAfkTrayModeAsync();
                break;
            case "shortcut:toggle-mute":
                ToggleGlobalMute();
                break;
            case "shortcut:tab:1":
                _ = SwitchToTabAsync(0);
                break;
            case "shortcut:tab:2":
                _ = SwitchToTabAsync(1);
                break;
            case "shortcut:tab:3":
                _ = SwitchToTabAsync(2);
                break;
            case "shortcut:tab:4":
                _ = SwitchToTabAsync(3);
                break;
        }
    }

    private async Task InitializeWebViewsAsync()
    {
        if (_isInitializing) return;
        _isInitializing = true;

        try
        {
            var userDataFolder = Path.Combine(AppContext.BaseDirectory, "WebView2Data");
            Directory.CreateDirectory(userDataFolder);

            var envOptions = new CoreWebView2EnvironmentOptions(ChromiumAntiThrottleArgs)
            {
                AreBrowserExtensionsEnabled = true
            };
            _sharedEnvironment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, envOptions);

            string? huntExtDir = _config.LoadHuntExtension ? AppConfig.GetHuntExtensionDirectory() : null;
            string resolvedUrl = _config.GetResolvedTargetUrl(out bool huntRouteRequested);
            if (huntRouteRequested && !_config.GlobalHuntOnlyMode)
            {
                _config.GlobalHuntOnlyMode = true;
                _config.Save();
                UpdateTopBarState();
            }

            // Inicializa primeiro a aba ativa para resposta visual imediata, depois as demais
            var order = Enumerable.Range(0, 4)
                .OrderBy(i => i == _config.ActiveTabIndex ? 0 : 1)
                .ThenBy(i => i);

            foreach (int i in order)
            {
                if (IsDisposed) return;
                await _slots[i].InitializeAsync(_sharedEnvironment, resolvedUrl, _config.GlobalMuted, huntExtDir);
                await SyncSlotOptimizationAsync(i);
            }

            ProcessMemoryHelper.SetProcessTreePriority(ProcessPriorityClass.BelowNormal);
            _maintenanceTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Não foi possível iniciar o WebView2 Runtime.\nVerifique se o Microsoft Edge WebView2 Runtime está instalado.\n\nDetalhes: {ex.Message}",
                "Erro de Inicialização",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void UpdateWorkspaceLayout()
    {
        if (_isAfkTrayMode)
        {
            for (int i = 0; i < 4; i++)
                _slots[i].ContainerPanel.Visible = false;
            return;
        }

        int w = _workspacePanel.ClientSize.Width;
        int h = _workspacePanel.ClientSize.Height;
        if (w <= 0 || h <= 0) return;

        _workspacePanel.SuspendLayout();
        try
        {
            if (_config.IsGridMode)
            {
                const int gap = 4;
                int cellW = Math.Max(100, (w - gap) / 2);
                int cellH = Math.Max(100, (h - gap) / 2);
                int rightW = Math.Max(100, w - cellW - gap);
                int bottomH = Math.Max(100, h - cellH - gap);

                var rects = new[]
                {
                    new Rectangle(0, 0, cellW, cellH),
                    new Rectangle(cellW + gap, 0, rightW, cellH),
                    new Rectangle(0, cellH + gap, cellW, bottomH),
                    new Rectangle(cellW + gap, cellH + gap, rightW, bottomH)
                };

                for (int i = 0; i < 4; i++)
                {
                    _slots[i].ContainerPanel.Bounds = rects[i];
                    _slots[i].ContainerPanel.Visible = true;
                    _slots[i].SetActiveHighlight(i == _config.ActiveTabIndex, isGridMode: true);
                }
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    bool isActive = i == _config.ActiveTabIndex;
                    if (isActive)
                        _slots[i].ContainerPanel.Bounds = new Rectangle(0, 0, w, h);

                    _slots[i].ContainerPanel.Visible = isActive;
                    _slots[i].SetActiveHighlight(isActive, isGridMode: false);
                }
            }
        }
        finally
        {
            _workspacePanel.ResumeLayout(performLayout: true);
        }
    }

    private async Task SwitchToTabAsync(int index)
    {
        if (index < 0 || index >= 4) return;
        _config.ActiveTabIndex = index;
        _config.Save();

        UpdateTopBarState();
        UpdateWorkspaceLayout();
        await SyncAllOptimizationsAsync();
    }

    private async Task FocusSlotInTabModeAsync(int index)
    {
        _config.IsGridMode = false;
        _config.ActiveTabIndex = index;
        _config.Save();

        UpdateTopBarState();
        UpdateWorkspaceLayout();
        await SyncAllOptimizationsAsync();
    }

    private async Task ToggleLayoutModeAsync()
    {
        _config.IsGridMode = !_config.IsGridMode;
        _config.Save();

        UpdateTopBarState();
        UpdateWorkspaceLayout();
        await SyncAllOptimizationsAsync();
    }

    private async Task ToggleGlobalHuntOnlyAsync()
    {
        _config.GlobalHuntOnlyMode = !_config.GlobalHuntOnlyMode;
        _config.Save();

        UpdateTopBarState();
        await SyncAllOptimizationsAsync();
    }

    private async Task ToggleSlotHuntOnlyAsync(int index)
    {
        if (_config.GlobalHuntOnlyMode)
        {
            _config.GlobalHuntOnlyMode = false;
            for (int i = 0; i < 4; i++)
                _slots[i].ManualHuntOnly = i != index;
            _config.Save();
        }
        else
        {
            _slots[index].ManualHuntOnly = !_slots[index].ManualHuntOnly;
        }

        UpdateTopBarState();
        await SyncAllOptimizationsAsync();
    }

    private async Task ToggleGlobalEcoAsync()
    {
        _config.GlobalEcoMode = !_config.GlobalEcoMode;
        _config.Save();

        UpdateTopBarState();
        await SyncAllOptimizationsAsync();
    }

    private async Task ToggleGlobalUltraAsync()
    {
        _config.GlobalUltraMode = !_config.GlobalUltraMode;
        _config.Save();

        UpdateTopBarState();
        await SyncAllOptimizationsAsync();
    }

    private async Task ToggleGlobalNoSpritesAsync()
    {
        _config.GlobalNoSpritesMode = !_config.GlobalNoSpritesMode;
        _config.Save();

        UpdateTopBarState();
        await SyncAllOptimizationsAsync();
    }

    private async Task ToggleSlotEcoAsync(int index)
    {
        if (_config.GlobalEcoMode)
        {
            _config.GlobalEcoMode = false;
            for (int i = 0; i < 4; i++)
                _slots[i].ManualEco = i != index;
            _config.Save();
        }
        else
        {
            _slots[index].ManualEco = !_slots[index].ManualEco;
        }

        UpdateTopBarState();
        await SyncAllOptimizationsAsync();
    }

    private async Task ToggleSlotUltraAsync(int index)
    {
        if (_config.GlobalUltraMode)
        {
            _config.GlobalUltraMode = false;
            for (int i = 0; i < 4; i++)
                _slots[i].ManualUltra = i != index;
            _config.Save();
        }
        else
        {
            _slots[index].ManualUltra = !_slots[index].ManualUltra;
        }

        UpdateTopBarState();
        await SyncAllOptimizationsAsync();
    }

    private async Task EnterAfkTrayModeAsync()
    {
        if (_isAfkTrayMode) return;

        _preAfkWindowState = WindowState;
        _isAfkTrayMode = true;

        UpdateWorkspaceLayout();
        await SyncAllOptimizationsAsync();

        ProcessMemoryHelper.SetProcessTreePriority(ProcessPriorityClass.Idle);
        ProcessMemoryHelper.TrimMemoryTree();

        Hide();
        _trayIcon.Visible = true;
        _trayIcon.ShowBalloonTip(
            2500,
            "PokeIdle Multi — Modo AFK Fantasma",
            "As 4 contas continuam caçando em segundo plano com renderização desligada. Clique no ícone para restaurar.",
            ToolTipIcon.Info);
    }

    private async Task ExitAfkTrayModeAsync()
    {
        if (!_isAfkTrayMode) return;

        _isAfkTrayMode = false;
        _trayIcon.Visible = false;

        Show();
        WindowState = _preAfkWindowState == FormWindowState.Minimized
            ? FormWindowState.Normal
            : _preAfkWindowState;
        Activate();

        ProcessMemoryHelper.SetProcessTreePriority(ProcessPriorityClass.BelowNormal);
        UpdateTopBarState();
        UpdateWorkspaceLayout();
        await SyncAllOptimizationsAsync();
    }

    private void ToggleGlobalMute()
    {
        _config.GlobalMuted = !_config.GlobalMuted;
        _config.Save();

        for (int i = 0; i < 4; i++)
            _slots[i].SetMuted(_config.GlobalMuted);

        UpdateTopBarState();
    }

    private void ToggleSlotMute(int index)
    {
        bool nextMuted = !_slots[index].IsMuted;
        _slots[index].SetMuted(nextMuted);

        if (_slots.All(s => s.IsMuted))
            _config.GlobalMuted = true;
        else if (_slots.All(s => !s.IsMuted))
            _config.GlobalMuted = false;

        _config.Save();
        UpdateTopBarState();
    }

    private void ReloadActiveOrAll(bool all)
    {
        if (all)
        {
            for (int i = 0; i < 4; i++)
                _slots[i].Reload();
            ShowTransientTitleStatus("Recarregando todas as 4 instâncias...");
        }
        else
        {
            _slots[_config.ActiveTabIndex].Reload();
            ShowTransientTitleStatus($"Recarregando {_config.InstanceNames[_config.ActiveTabIndex]}...");
        }
    }

    private async Task SyncAllOptimizationsAsync()
    {
        var tasks = new Task[4];
        for (int i = 0; i < 4; i++)
            tasks[i] = SyncSlotOptimizationAsync(i);
        await Task.WhenAll(tasks);
    }

    private Task SyncSlotOptimizationAsync(int index)
    {
        bool inactiveInTabMode = !_config.IsGridMode && _config.AutoEcoOnInactiveTabs && index != _config.ActiveTabIndex;
        bool effectiveEco = _isAfkTrayMode || _config.GlobalEcoMode || _slots[index].ManualEco || inactiveInTabMode;
        bool effectiveUltra = _isAfkTrayMode || _config.GlobalUltraMode || _slots[index].ManualUltra;
        bool effectiveHuntOnly = _isAfkTrayMode || _config.GlobalHuntOnlyMode || _slots[index].ManualHuntOnly;
        bool effectiveNoSprites = _config.GlobalNoSpritesMode || _slots[index].ManualNoSprites;

        return _slots[index].ApplyOptimizationStateAsync(
            effectiveEco,
            effectiveUltra,
            effectiveHuntOnly,
            effectiveNoSprites);
    }

    private void RunBackgroundMaintenance()
    {
        if (_isInitializing || IsDisposed) return;

        for (int i = 0; i < 4; i++)
            _slots[i].ApplyLowMemoryTarget();

        ProcessMemoryHelper.SetProcessTreePriority(
            _isAfkTrayMode ? ProcessPriorityClass.Idle : ProcessPriorityClass.BelowNormal);
        ProcessMemoryHelper.TrimMemoryTree();
    }

    private void TrimMemoryNow()
    {
        for (int i = 0; i < 4; i++)
            _slots[i].ApplyLowMemoryTarget();

        ProcessMemoryHelper.SetProcessTreePriority(
            _isAfkTrayMode ? ProcessPriorityClass.Idle : ProcessPriorityClass.BelowNormal);
        int trimmed = ProcessMemoryHelper.TrimMemoryTree();
        using var proc = Process.GetCurrentProcess();
        long mb = proc.WorkingSet64 / (1024 * 1024);
        ShowTransientTitleStatus($"RAM limpa em {trimmed} processos (Host: {mb} MB)");
    }

    private async Task PromptEditUrlExtensionAsync()
    {
        using var dialog = new Form
        {
            Text = "Configurar Extensão de URL (/hunt)",
            Size = new Size(420, 175),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(28, 15, 23),
            ForeColor = Color.White
        };

        var hintLabel = new Label
        {
            Text = "Exemplos: /hunt  |  /app  |  /hunt?hunt=nome-do-mapa",
            Location = new Point(16, 14),
            Width = 370,
            Height = 20,
            ForeColor = Color.FromArgb(215, 200, 209),
            Font = new Font("Segoe UI", 8.5f)
        };

        var input = new TextBox
        {
            Text = _config.UrlExtension,
            Location = new Point(16, 40),
            Width = 370,
            MaxLength = 120,
            BackColor = Color.FromArgb(46, 26, 38),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        var applyBtn = new Button
        {
            Text = "Salvar e Aplicar",
            DialogResult = DialogResult.OK,
            Location = new Point(194, 84),
            Width = 110,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(214, 59, 89),
            ForeColor = Color.White
        };
        applyBtn.FlatAppearance.BorderSize = 0;

        var cancelBtn = new Button
        {
            Text = "Cancelar",
            DialogResult = DialogResult.Cancel,
            Location = new Point(310, 84),
            Width = 76,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(62, 39, 53),
            ForeColor = Color.White
        };
        cancelBtn.FlatAppearance.BorderSize = 0;

        dialog.Controls.Add(hintLabel);
        dialog.Controls.Add(input);
        dialog.Controls.Add(applyBtn);
        dialog.Controls.Add(cancelBtn);
        dialog.AcceptButton = applyBtn;
        dialog.CancelButton = cancelBtn;

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _config.UrlExtension = string.IsNullOrWhiteSpace(input.Text) ? "/hunt" : input.Text.Trim();
        string resolvedUrl = _config.GetResolvedTargetUrl(out bool huntRouteRequested);
        if (huntRouteRequested)
            _config.GlobalHuntOnlyMode = true;
        _config.Save();

        UpdateTopBarState();
        _toolTip.SetToolTip(_globalHuntButton, $"Modo Só Hunt (/hunt): oculta chat/menus laterais (Alt+H). Clique direito: configurar extensão de URL ({_config.UrlExtension}).");

        for (int i = 0; i < 4; i++)
            _slots[i].Navigate(resolvedUrl);

        await SyncAllOptimizationsAsync();
        ShowTransientTitleStatus($"Extensão aplicada: {_config.UrlExtension}");
    }

    private void PromptRenameInstance(int index)
    {
        using var dialog = new Form
        {
            Text = $"Renomear Instância {index + 1}",
            Size = new Size(340, 145),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(28, 15, 23),
            ForeColor = Color.White
        };

        var input = new TextBox
        {
            Text = _config.InstanceNames[index],
            Location = new Point(16, 18),
            Width = 290,
            MaxLength = 24,
            BackColor = Color.FromArgb(46, 26, 38),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        var okBtn = new Button
        {
            Text = "Salvar",
            DialogResult = DialogResult.OK,
            Location = new Point(144, 58),
            Width = 76,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(214, 59, 89),
            ForeColor = Color.White
        };
        okBtn.FlatAppearance.BorderSize = 0;

        var cancelBtn = new Button
        {
            Text = "Cancelar",
            DialogResult = DialogResult.Cancel,
            Location = new Point(230, 58),
            Width = 76,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(62, 39, 53),
            ForeColor = Color.White
        };
        cancelBtn.FlatAppearance.BorderSize = 0;

        dialog.Controls.Add(input);
        dialog.Controls.Add(okBtn);
        dialog.Controls.Add(cancelBtn);
        dialog.AcceptButton = okBtn;
        dialog.CancelButton = cancelBtn;

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var newName = string.IsNullOrWhiteSpace(input.Text) ? $"Conta {index + 1}" : input.Text.Trim();
        _config.InstanceNames[index] = newName;
        _config.Save();

        _slots[index].SetDisplayName(newName);
        _toolTip.SetToolTip(_tabButtons[index], $"Ativar {newName} (Alt+{index + 1}). Clique direito para renomear.");
        UpdateTopBarState();
    }

    private ContextMenuStrip BuildTrayContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Restaurar Janela", null, async (_, _) => await ExitAfkTrayModeAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Alternar Modo Eco (Todas)", null, async (_, _) => await ToggleGlobalEcoAsync());
        menu.Items.Add("Alternar Modo Ultra (Todas)", null, async (_, _) => await ToggleGlobalUltraAsync());
        menu.Items.Add("Limpar RAM Agora", null, (_, _) => TrimMemoryNow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => Close());
        return menu;
    }

    private static (Icon icon, IntPtr handle) CreatePokeballTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var topBrush = new SolidBrush(Color.FromArgb(220, 48, 72));
            using var bottomBrush = new SolidBrush(Color.FromArgb(245, 245, 245));
            using var outlinePen = new Pen(Color.FromArgb(28, 15, 23), 2.5f);

            g.FillPie(topBrush, 2, 2, 28, 28, 180, 180);
            g.FillPie(bottomBrush, 2, 2, 28, 28, 0, 180);
            g.DrawEllipse(outlinePen, 2, 2, 28, 28);
            g.DrawLine(outlinePen, 2, 16, 30, 16);

            g.FillEllipse(bottomBrush, 11, 11, 10, 10);
            g.DrawEllipse(outlinePen, 11, 11, 10, 10);
        }

        IntPtr handle = bmp.GetHicon();
        return (Icon.FromHandle(handle), handle);
    }

    private void UpdateTopBarState()
    {
        for (int i = 0; i < 4; i++)
        {
            bool active = i == _config.ActiveTabIndex;
            _tabButtons[i].Text = $"[{i + 1}] {_config.InstanceNames[i]}";
            _tabButtons[i].BackColor = active ? Color.FromArgb(214, 59, 89) : Color.FromArgb(52, 30, 44);
            _tabButtons[i].ForeColor = active ? Color.White : Color.FromArgb(220, 204, 214);
        }

        _layoutToggleButton.Text = _config.IsGridMode ? "▭ Abas" : "⊞ Grade";
        _layoutToggleButton.BackColor = _config.IsGridMode ? Color.FromArgb(64, 92, 168) : Color.FromArgb(52, 30, 44);

        _globalHuntButton.BackColor = _config.GlobalHuntOnlyMode
            ? Color.FromArgb(138, 46, 128)
            : Color.FromArgb(52, 30, 44);

        _globalEcoButton.Text = _config.AutoEcoOnInactiveTabs && !_config.GlobalEcoMode
            ? "⚡ Eco*"
            : "⚡ Eco";
        _globalEcoButton.BackColor = _config.GlobalEcoMode
            ? Color.FromArgb(26, 118, 72)
            : (_config.AutoEcoOnInactiveTabs ? Color.FromArgb(32, 74, 54) : Color.FromArgb(52, 30, 44));

        _globalUltraButton.BackColor = _config.GlobalUltraMode
            ? Color.FromArgb(176, 106, 16)
            : Color.FromArgb(52, 30, 44);

        _globalSpritesButton.BackColor = _config.GlobalNoSpritesMode
            ? Color.FromArgb(148, 62, 42)
            : Color.FromArgb(52, 30, 44);

        bool allMuted = _slots.All(s => s.IsMuted);
        _globalMuteButton.Text = allMuted ? "🔇 Mudo" : "🔊 Som";
        _globalMuteButton.BackColor = allMuted ? Color.FromArgb(52, 30, 44) : Color.FromArgb(47, 112, 168);
    }

    private void ShowTransientTitleStatus(string status)
    {
        Text = $"PokeIdle Multi (4x Ultraleve) — {status}";
        var timer = new System.Windows.Forms.Timer { Interval = 3500 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            if (!IsDisposed)
                Text = "PokeIdle Multi (4x Ultraleve)";
        };
        timer.Start();
    }

    private static Button CreateTopButton(string text, int width)
    {
        var btn = new Button
        {
            Text = text,
            Width = width,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(52, 30, 44),
            ForeColor = Color.FromArgb(230, 218, 226),
            Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
            Margin = new Padding(3, 0, 0, 0),
            Padding = Padding.Empty,
            Cursor = Cursors.Hand,
            TabStop = false
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }
}
