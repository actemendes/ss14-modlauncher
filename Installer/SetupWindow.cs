using SS14ModLauncher.Core;

namespace SS14ModLauncher;

/// <summary>A single decision on first launch; installation still uses the normal transaction.</summary>
internal sealed class SetupWindow : Form
{
    private readonly AppSettings _settings;
    private readonly Func<string, string, Task> _installAndLaunch;
    private readonly Action<string?, string> _dismiss;
    private readonly string[] _candidates;
    private readonly ToolTip _tips = new() { AutoPopDelay = 15000, InitialDelay = 350 };
    private string _language;
    private string _root;
    private ComboBox _folder = null!;
    private Label _readiness = null!;
    private Label _integration = null!;
    private Label _error = null!;
    private Button _primary = null!;
    private Button _diagnostics = null!;
    private FlowLayoutPanel _content = null!;
    private bool _working;
    private bool _completed;
    private bool _dismissed;
    public bool OpenDiagnostics { get; private set; }
    public string SelectedRoot => _root;
    public string SelectedLanguage => _language;
    private string T(string ru, string en) => _language == "ru" ? ru : en;

    public SetupWindow(AppSettings settings, string[] candidates, Func<string, string, Task> installAndLaunch,
        Action<string?, string> dismiss)
    {
        _settings = settings; _candidates = candidates; _installAndLaunch = installAndLaunch; _dismiss = dismiss;
        _language = settings.Language; _root = settings.LauncherPath;
        Name = "first-run-setup"; Text = "SS14 ModLauncher";
        ClientSize = new Size(790, 710); MinimumSize = new Size(806, 540);
        StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Background; ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        Render();
        Shown += (_, _) =>
        {
            var screen = Screen.FromControl(this).WorkingArea;
            if (Height > screen.Height) Height = screen.Height;
            if (Bottom > screen.Bottom) Top = screen.Bottom - Height;
            if (string.IsNullOrWhiteSpace(_folder.Text)) _folder.Focus(); else _primary.Focus();
        };
        FormClosing += (_, e) =>
        {
            if (_working) { e.Cancel = true; return; }
            if (_completed || _dismissed || OpenDiagnostics) return;
            try { _dismiss(_folder.Text.Trim(), _language); _dismissed = true; }
            catch (Exception error)
            {
                MessageBox.Show(this, T("Не удалось запомнить выбор. При следующем запуске настройка может появиться снова.\n\n", "Your preference could not be saved. Setup may appear again next time.\n\n")
                    + ErrorText.Message(error, _language), "SS14 ModLauncher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        FormClosed += (_, _) => _tips.Dispose();
    }

    private Label Paragraph(string text, float size = 10, Color? color = null, bool bold = false)
    {
        var label = Theme.Label(text, size, color, bold);
        label.MaximumSize = new Size(Math.Max(580, ClientSize.Width - 82), 0);
        return label;
    }

    private void Render()
    {
        SuspendLayout();
        foreach (Control control in Controls.Cast<Control>().ToArray()) control.Dispose();
        _content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(32, 26, 24, 20) };
        Controls.Add(_content);
        var top = new FlowLayoutPanel { Width = 704, Height = 43, WrapContents = false };
        var brand = Theme.Label("SS14 MODLAUNCHER", 10, Theme.Mint, true); brand.Width = 510; brand.AutoSize = false;
        top.Controls.Add(brand);
        var language = new ComboBox { Name = "setup-language", AccessibleName = "Language / Язык",
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, BackColor = Theme.Raised, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat };
        language.Items.AddRange(["Русский", "English"]); language.SelectedIndex = _language == "ru" ? 0 : 1;
        language.SelectedIndexChanged += (_, _) => { _root = _folder.Text; _language = language.SelectedIndex == 0 ? "ru" : "en"; Render(); };
        top.Controls.Add(language); _content.Controls.Add(top);
        _content.Controls.Add(Paragraph(T("Начнём с одной кнопки", "Ready in one click"), 26, bold: true));
        _content.Controls.Add(Paragraph(T("Настроим моды и откроем SS14. Дальше выбирайте сервер и играйте как обычно.",
            "Set up your mods and open SS14. Then pick a server and play as usual."), 11, Theme.Muted));
        _content.Controls.Add(Paragraph(T("1  ·  ВАША ИГРА", "1  ·  YOUR GAME"), 9, Theme.Blue, true));
        var pathRow = new FlowLayoutPanel { Width = 710, Height = 49, WrapContents = false };
        _folder = new ComboBox { Name = "setup-folder", AccessibleName = T("Папка SS14", "SS14 folder"),
            DropDownStyle = ComboBoxStyle.DropDown, Width = 535, BackColor = Theme.Raised, ForeColor = Theme.Text,
            FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 5, 10, 0), Text = _root };
        _folder.Items.AddRange(_candidates.Cast<object>().ToArray());
        _folder.TextChanged += (_, _) =>
        {
            // Full installation verification happens on selection/check, not on every keystroke.
            _primary.Enabled = !_settings.IsReadOnly;
            _primary.Text = T("Настроить и играть  →", "Set up & play  →");
            _readiness.Text = T("Папка будет проверена перед установкой.", "The folder will be checked before setup.");
            _error.Text = ""; _integration.Text = T("Интеграцию со Steam проверим для выбранной папки.", "Steam integration will be checked for this folder.");
        };
        _folder.SelectionChangeCommitted += (_, _) => RefreshReadiness();
        _folder.Leave += (_, _) => { if (!_working) RefreshReadiness(); };
        _tips.SetToolTip(_folder, T("Можно выбрать папку игры или bin_x64. При нескольких установках выберите нужную из списка.",
            "Select the game folder or bin_x64. If several installations are found, choose one from the list."));
        pathRow.Controls.Add(_folder);
        pathRow.Controls.Add(Theme.Button(T("Выбрать…", "Browse…"), (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = T("Выберите папку Space Station 14 или bin_x64", "Choose the Space Station 14 or bin_x64 folder"),
                UseDescriptionForTitle = true, InitialDirectory = Directory.Exists(_folder.Text) ? _folder.Text : "" };
            if (picker.ShowDialog(this) == DialogResult.OK) { _folder.Text = picker.SelectedPath; RefreshReadiness(); }
        }));
        _content.Controls.Add(pathRow);
        _readiness = Paragraph("", 10, Theme.Muted); _readiness.Name = "setup-readiness"; _content.Controls.Add(_readiness);
        _content.Controls.Add(Paragraph(T("2  ·  ЧТО БУДЕТ НАСТРОЕНО", "2  ·  WHAT YOU GET"), 9, Theme.Blue, true));
        var selected = Catalog.Bundled.Where(mod => _settings.SelectedModIds.Contains(mod.Id)).Select(mod => mod.Name(_language)).ToArray();
        _content.Controls.Add(Paragraph(selected.Length == 0 ? T("Моды выключены в вашем профиле. Их можно включить после настройки.", "Mods are disabled in your profile. You can enable them after setup.")
            : T("Моды: ", "Mods: ") + string.Join(", ", selected), 12, bold: true));
        _content.Controls.Add(Paragraph(T("Выбор модов можно изменить в любой момент. Оригинальные файлы сохраним для восстановления.",
            "Change your mod selection anytime. Original files are backed up for restoration."), 10, Theme.Muted));
        _integration = Paragraph("", 10, Theme.Muted); _integration.Name = "setup-steam"; _content.Controls.Add(_integration);
        _error = Paragraph("", 10, Theme.Red); _error.Name = "setup-error"; _content.Controls.Add(_error);
        var actions = new FlowLayoutPanel { Width = 710, Height = 54, WrapContents = false };
        _primary = Theme.Button(T("Настроить и играть  →", "Set up & play  →"), async (_, _) => await SetUpAsync(), true);
        _primary.Name = "setup-primary"; actions.Controls.Add(_primary);
        var later = Theme.Button(T("Позже", "Later"), (_, _) => Close()); later.Name = "setup-later"; actions.Controls.Add(later);
        _content.Controls.Add(actions); AcceptButton = _primary; CancelButton = later;
        _diagnostics = Theme.Button(T("Открыть диагностику", "Open diagnostics"), (_, _) => { _root = _folder.Text.Trim(); OpenDiagnostics = true; Close(); });
        _diagnostics.Name = "setup-diagnostics"; _content.Controls.Add(_diagnostics);
        _content.Controls.Add(Paragraph(T("Вернуться к этому окну: «Установка» → «Быстрая настройка». Интернет для установки модов из архива не нужен.",
            "Reopen this window from Installation → Quick setup. Bundled mods can be installed offline."), 9, Theme.Muted));
        RefreshReadiness(); ResumeLayout(true);
    }

    private SetupReadiness RefreshReadiness()
    {
        var ready = Setup.Prepare(_folder.Text.Trim());
        _primary.Enabled = !_settings.IsReadOnly;
        _diagnostics.Visible = _settings.IsReadOnly || (!ready.CanInstall && ready.Issue is not ("root-missing" or "root-invalid") && ready.InstallationState is "changed" or "recovery");
        _readiness.ForeColor = ready.CanInstall && !_settings.IsReadOnly ? Theme.Mint : Theme.Amber;
        _readiness.Text = _settings.IsReadOnly ? T("Нужно восстановить настройки. Откройте диагностику.", "Settings need recovery. Open diagnostics.")
            : ready.CanInstall ? T("✓ SS14 найден. Можно начинать.", "✓ SS14 found. Ready to go.") : IssueText(ready);
        _integration.Text = ready.CanInstall ? ready.SteamEnabledOnInstall
            ? T("✓ Steam → «Играть» будет открывать ModLauncher автоматически.", "✓ Steam → Play will open ModLauncher automatically.")
            : Installation.IsSteamInstallation(ready.Root)
                ? T("Автозапуск из Steam оставим выключенным — это ваш сохранённый выбор.", "Steam integration will stay off, following your saved preference.")
                : T("Запускайте игру через этот ModLauncher. Steam-установка в этой папке не найдена.", "Use this ModLauncher to play. This folder is not a Steam installation.")
            : T("Сначала найдём готовую к настройке установку SS14.", "First, select an SS14 installation ready for setup.");
        _primary.Text = ready.Issue is "game-running" or "process-check-failed" ? T("Проверить снова", "Check again") : T("Настроить и играть  →", "Set up & play  →");
        return ready;
    }

    private string IssueText(SetupReadiness ready) => ready.Issue switch
    {
        "root-missing" or "root-invalid" => MissingFolderText,
        "game-running" or "process-check-failed" => T("Закройте игру и обычный SS14 Launcher, затем нажмите «Проверить снова». Steam можно оставить открытым.", "Close the game and the original SS14 Launcher, then click Check again. Steam can stay open."),
        _ when ready.InstallationState is "changed" or "recovery" => T("Файлы этой установки требуют проверки. Откройте диагностику — она поможет восстановить SS14.", "This installation needs checking. Open diagnostics to recover SS14."),
        _ => MissingFolderText
    };
    private string MissingFolderText => T("SS14 не найден. Установите игру через Steam, затем выберите её папку здесь. Можно выбрать саму папку игры или bin_x64.", "SS14 was not found. Install the game through Steam, then select its folder here. The game folder or bin_x64 both work.");

    private async Task SetUpAsync()
    {
        if (_working) return;
        if (_completed) { Close(); return; }
        var ready = RefreshReadiness();
        if (!ready.CanInstall || _settings.IsReadOnly) { _error.Text = _readiness.Text; _folder.Focus(); return; }
        _working = true; _content.Enabled = false; UseWaitCursor = true;
        _error.ForeColor = Theme.Blue; _error.Text = T("Настраиваем моды и запуск… Это может занять несколько секунд.", "Setting up mods and launch integration… This may take a few seconds.");
        try
        {
            await _installAndLaunch(ready.Root, _language);
            _completed = true;
            foreach (Control control in _content.Controls.Cast<Control>().ToArray()) control.Dispose();
            _content.Controls.Add(Paragraph("✓", 48, Theme.Mint, true));
            _content.Controls.Add(Paragraph(T("Всё готово. Приятной смены!", "You're ready. Enjoy your shift!"), 25, bold: true));
            _content.Controls.Add(Paragraph(T("SS14 Launcher открыт. Войдите в аккаунт, выберите сервер и подключитесь — выбранные моды загрузятся вместе с клиентом.",
                "SS14 Launcher is open. Sign in, choose a server and connect — your selected mods will load with the client."), 12));
            _content.Controls.Add(Paragraph(Installation.IsSteamEnabled(ready.Root)
                ? T("В следующий раз просто нажмите «Играть» в Steam. Сначала откроется ModLauncher с вашим набором модов.", "Next time, just press Play in Steam. ModLauncher will open with your selected mods.")
                : T("В следующий раз откройте ModLauncher и нажмите «Запустить SS14».", "Next time, open ModLauncher and click Launch SS14."), 12, Theme.Mint));
            _content.Controls.Add(Paragraph(T("Мои моды — включить и выключить моды.\nОбновления — новые версии модов и лаунчера.\nУстановка — вернуть чистый SS14.",
                "My mods — enable or disable mods.\nUpdates — new mod and launcher versions.\nInstallation — restore clean SS14."), 11, Theme.Muted));
            var done = Theme.Button(T("Понятно", "Got it"), (_, _) => Close(), true); done.Name = "setup-done";
            _content.Controls.Add(done); AcceptButton = done; CancelButton = done;
        }
        catch (Exception error) { RefreshReadiness(); ShowError(error); }
        finally { _working = false; _content.Enabled = true; UseWaitCursor = false; }
    }

    private void ShowError(Exception error)
    {
        _error.ForeColor = Theme.Red;
        _error.Text = ErrorText.Message(error, _language) + "\n" + T("Исправьте причину и повторите настройку. Если ошибка повторяется, откройте диагностику.", "Resolve the problem and try setup again. If it persists, open diagnostics.");
        _diagnostics.Visible = true;
        _content.ScrollControlIntoView(_error);
    }
}
