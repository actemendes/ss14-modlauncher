using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.Json;
using SS14ModLauncher.Core;

namespace SS14ModLauncher;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(7, 11, 16), Surface = Color.FromArgb(14, 21, 30), Raised = Color.FromArgb(20, 29, 40), Border = Color.FromArgb(39, 52, 66), Text = Color.FromArgb(238, 243, 247), Muted = Color.FromArgb(141, 155, 169), Mint = Color.FromArgb(88, 214, 164), Blue = Color.FromArgb(100, 181, 214), Red = Color.FromArgb(239, 105, 116), Amber = Color.FromArgb(231, 189, 89);
    public static Label Label(string text, float size = 10, Color? color = null, bool bold = false) => new() { Text = text, UseMnemonic = false, AutoSize = true, ForeColor = color ?? Text, BackColor = Color.Transparent, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0, 0, 0, 8) };
    public static Button Button(string text, EventHandler action, bool primary = false)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(100, 40), Padding = new Padding(12, 5, 12, 5), FlatStyle = FlatStyle.Flat, BackColor = primary ? Mint : Raised, ForeColor = primary ? Background : Text, Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 10), UseVisualStyleBackColor = false };
        button.UseMnemonic = false; button.FlatAppearance.BorderColor = primary ? Mint : Border; button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(114, 231, 184) : Color.FromArgb(32, 46, 61);
        button.Click += action; return button;
    }
    public static TextBox TextBox(string text) => new() { Text = text, BackColor = Raised, ForeColor = Text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11), Width = 600, Margin = new Padding(0, 3, 0, 12) };
}

internal sealed class Card : Panel
{
    public Card() { DoubleBuffered = true; BackColor = Theme.Surface; Padding = new Padding(22); Margin = new Padding(0, 0, 0, 14); }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using var pen = new Pen(Theme.Border); e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); }
}

internal sealed class LauncherWindow : Form
{
    private readonly AppSettings _settings;
    private readonly string? _settingsPath;
    private readonly Panel _page = new() { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(28, 20, 28, 12) };
    private readonly Panel _navigation = new() { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(16, 22, 16, 10) };
    private readonly Label _status = Theme.Label("", 9, Theme.Muted);
    private readonly Dictionary<string, CheckBox> _toggles = [];
    private readonly ToolTip _tips = new();
    private readonly UpdateService _updates;
    private readonly AutomaticUpdateChecker _automaticUpdates;
    private readonly CancellationTokenSource _lifetime = new();
    private UpdateCheck? _available;
    private string? _availableContext;
    private string? _updateProblem;
    private bool _updateChecking;
    private Label? _updateStateLabel;
    private string _view = "library";
    private bool _busy;
    private string _installationState = "clean";
    private string _installationDetail = "";
    private Label? _selectionCount;
    private string T(string ru, string en) => _settings.Language == "ru" ? ru : en;
    private string[] SelectedFiles => Catalog.Bundled.Where(m => _settings.SelectedModIds.Contains(m.Id)).Select(m => m.File).ToArray();

    public LauncherWindow(AppSettings settings, string? settingsPath, string? initialView = null, bool enableAutomaticCheck = true, UpdateService? updateService = null)
    {
        _settings = settings; _settingsPath = settingsPath;
        _updates = updateService ?? new UpdateService();
        _automaticUpdates = new AutomaticUpdateChecker((repository, versions, token) => _updates.CheckAsync(repository, versions, token));
        if (initialView is "library" or "installation" or "updates" or "diagnostics" or "about") _view = initialView;
        Text = "SS14 ModLauncher by actemendes"; Font = new Font("Segoe UI", 10); BackColor = Theme.Background; ForeColor = Theme.Text;
        ClientSize = new Size(1240, 850); MinimumSize = new Size(1160, 800); StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        AutoScaleMode = AutoScaleMode.Dpi;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.Controls.Add(_navigation, 0, 0); root.SetRowSpan(_navigation, 2); root.Controls.Add(_page, 1, 0);
        var footer = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(28, 12, 12, 8) };
        _status.Dock = DockStyle.Fill; _status.AutoSize = false; footer.Controls.Add(_status); root.Controls.Add(footer, 1, 1); Controls.Add(root);
        if (string.IsNullOrWhiteSpace(_settings.LauncherPath)) _settings.LauncherPath = Installation.Discover().FirstOrDefault() ?? "";
        RefreshState(); Render();
        SetStatus(_settings.LoadError is { Length: > 0 } ? T("Настройки повреждены. Исходный файл сохранён; откройте диагностику.", "Settings are damaged. Original file preserved; open Diagnostics.") : T("Готов к запуску. Выбор модов применяется кнопкой запуска.", "Ready. Your mod selection is applied when you launch."));
        FormClosing += (_, e) => { if (_busy) { e.Cancel = true; SetStatus(T("Дождитесь завершения операции.", "Wait for the current operation to finish.")); } };
        if (enableAutomaticCheck) Shown += async (_, _) => await CheckAtStartupAsync();
        FormClosed += (_, _) => { _automaticUpdates.Dispose(); _lifetime.Cancel(); _lifetime.Dispose(); _tips.Dispose(); _updates.Dispose(); };
    }
    private void RefreshState()
    {
        if (string.IsNullOrWhiteSpace(_settings.LauncherPath)) { _installationState = "missing"; _installationDetail = ""; return; }
        try { var state = Installation.Inspect(_settings.LauncherPath); _installationState = state.State; _installationDetail = state.Detail; }
        catch (Exception e) { _installationState = "changed"; _installationDetail = e.Message; }
    }
    private void Save()
    {
        _settings.Save(_settingsPath);
        if (_availableContext != UpdateContext()) { _available = null; _availableContext = null; }
    }
    private string UpdateContext()
    {
        var versions = string.IsNullOrWhiteSpace(_settings.LauncherPath) ? null : _settings.VersionsFor(_settings.LauncherPath);
        return _settings.UpdateRepository + "\n" + _settings.LauncherPath + "\n" + string.Join(";", versions?.OrderBy(pair => pair.Key).Select(pair => pair.Key + "=" + pair.Value) ?? []);
    }
    private bool HasUpdates => _available is { } update && (update.HasLauncherUpdate || update.Mods.Count > 0 || update.BlockedMods.Count > 0);
    private string UpdateSummary()
    {
        if (_available is not { } update) return "";
        var parts = new List<string>();
        if (update.HasLauncherUpdate) parts.Add("ModLauncher " + update.LauncherVersion);
        if (update.Mods.Count > 0) parts.Add(T("Обновлений модов: ", "Mod updates: ") + update.Mods.Count);
        if (update.BlockedMods.Count > 0) parts.Add(T("Нужен новый лаунчер: ", "Newer launcher required: ") + update.BlockedMods.Count);
        return parts.Count == 0 ? T("Установлены актуальные версии.", "Everything is up to date.") : string.Join("  ·  ", parts);
    }
    private async Task CheckAtStartupAsync()
    {
        if (!_settings.CheckUpdatesOnStartup || _settings.IsReadOnly || string.IsNullOrWhiteSpace(_settings.UpdateRepository)) return;
        string context;
        try { context = UpdateContext(); } catch { return; }
        _updateChecking = true;
        var result = await _automaticUpdates.CheckOnceAsync(_settings, _lifetime.Token);
        if (IsDisposed || Disposing) return;
        if (!_busy) _updateChecking = false;
        RefreshUpdateCheckState();
        if (!_settings.CheckUpdatesOnStartup || context != UpdateContext()) return;
        if (result != null)
        {
            _available = result; _availableContext = context; _updateProblem = null;
            if (!_busy) { RefreshUpdateNotice(); if (HasUpdates) SetStatus(UpdateSummary()); }
        }
        else if (_automaticUpdates.LastError is { } error)
        {
            _updateProblem = ErrorText.Message(error, _settings.Language);
            RefreshUpdateCheckState();
            if (!_busy && _view == "updates") Render();
        }
    }
    private void RefreshUpdateCheckState()
    {
        if (_updateStateLabel is not { IsDisposed: false }) return;
        _updateStateLabel.Text = _updateChecking
            ? T("Проверяем обновления… Можно продолжать пользоваться лаунчером.", "Checking for updates… You can keep using the launcher.")
            : _updateProblem != null ? T("Проверка недоступна. Повторите позже; запуск игры доступен.", "Update check unavailable. Try again later; you can still launch the game.") : "";
        _updateStateLabel.ForeColor = _updateProblem != null && !_updateChecking ? Theme.Amber : Theme.Muted;
    }
    private Button UpdateNoticeButton()
    {
        var notice = Theme.Button(T("Доступны обновления — открыть", "Updates available — open"), (_, _) => { _view = "updates"; Render(); }, true);
        notice.Name = "update-notice"; _tips.SetToolTip(notice, UpdateSummary()); return notice;
    }
    private void RefreshUpdateNotice()
    {
        if (_view == "updates") { Render(); return; }
        // Preserve controls, focus and unfinished edits while a background request completes.
        var nav = _navigation.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
        var button = nav?.Controls.OfType<Button>().FirstOrDefault(control => (string?)control.Tag == "updates");
        if (button != null) button.Text = T("Обновления", "Updates") + (HasUpdates ? "  ●" : "");
        var page = _page.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
        if (HasUpdates && page != null && !page.Controls.ContainsKey("update-notice"))
        {
            var notice = UpdateNoticeButton(); page.Controls.Add(notice); page.Controls.SetChildIndex(notice, 2);
        }
    }
    private void SetStatus(string text, bool error = false) { _status.Text = text; _status.ForeColor = error ? Theme.Red : Theme.Muted; _tips.SetToolTip(_status, text); }
    private void Render()
    {
        SuspendLayout();
        foreach (Control child in _navigation.Controls.Cast<Control>().ToArray()) child.Dispose();
        foreach (Control child in _page.Controls.Cast<Control>().ToArray()) child.Dispose();
        _toggles.Clear();
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        nav.Controls.Add(Theme.Label("SS14", 28, Theme.Mint, true)); nav.Controls.Add(Theme.Label("MODLAUNCHER", 11, Theme.Text, true));
        var by = Theme.Label("by actemendes", 9, Theme.Muted); by.Margin = new Padding(0, 0, 0, 40); nav.Controls.Add(by);
        foreach (var (id, ru, en) in new[] { ("library", "Мои моды", "My mods"), ("installation", "Установка", "Installation"), ("updates", "Обновления", "Updates"), ("diagnostics", "Диагностика", "Diagnostics"), ("about", "О проекте", "About") })
        {
            var button = Theme.Button(T(ru, en) + (id == "updates" && HasUpdates ? "  ●" : ""), (_, _) => { _view = id; RefreshState(); Render(); }); button.AutoSize = false; button.Width = 148; button.Margin = new Padding(0, 0, 0, 10); button.TextAlign = ContentAlignment.MiddleLeft;
            button.Tag = id;
            if (_view == id) { button.BackColor = Theme.Raised; button.ForeColor = Theme.Mint; button.FlatAppearance.BorderColor = Theme.Mint; }
            else { button.BackColor = Theme.Surface; button.FlatAppearance.BorderSize = 0; }
            nav.Controls.Add(button);
        }
        nav.Controls.Add(new Panel { Height = 30, Width = 10 }); nav.Controls.Add(Theme.Label(T("ЯЗЫК / LANGUAGE", "LANGUAGE / ЯЗЫК"), 8, Theme.Muted, true));
        var language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 155, BackColor = Theme.Raised, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat };
        language.Items.AddRange(["Русский", "English"]); language.SelectedIndex = _settings.Language == "ru" ? 0 : 1;
        language.SelectedIndexChanged += (_, _) => Run(() => { _settings.Language = language.SelectedIndex == 0 ? "ru" : "en"; Save(); Render(); }, T("Язык изменён.", "Language changed.")); nav.Controls.Add(language);
        var release = Theme.Label("v" + Program.Version + "  /  WINDOWS x64", 8, Theme.Muted); release.Margin = new Padding(0, 24, 0, 0); nav.Controls.Add(release);
        _navigation.Controls.Add(nav);
        switch (_view) { case "library": Library(); break; case "installation": InstallationPage(); break; case "updates": UpdatesPage(); break; case "diagnostics": Diagnostics(); break; default: About(); break; }
        ResumeLayout(true);
    }
    private FlowLayoutPanel Page(string title, string description)
    {
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 0, 18, 10) };
        var header = Theme.Label(title, 25, bold: true); content.Controls.Add(header);
        var sub = Theme.Label(description, 10, Theme.Muted); sub.Margin = new Padding(0, 0, 0, 22); content.Controls.Add(sub);
        if (HasUpdates && _view != "updates")
        {
            content.Controls.Add(UpdateNoticeButton());
        }
        content.SizeChanged += (_, _) => { foreach (Control item in content.Controls) if (item is Card or PictureBox) item.Width = Math.Max(600, content.ClientSize.Width - 24); };
        _page.Controls.Add(content); return content;
    }
    private Card Section(FlowLayoutPanel content, int height)
    {
        var card = new Card { Width = Math.Max(600, _page.ClientSize.Width - 82), Height = height }; content.Controls.Add(card); return card;
    }
    private static FlowLayoutPanel Stack(Control parent) { var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty }; parent.Controls.Add(stack); return stack; }
    private string StateText => _installationDetail == "legacy-installation" ? T("ОБНОВИТЕ УСТАНОВКУ", "UPGRADE INSTALLATION") : _installationState switch { "installed" => T("МОДЫ УСТАНОВЛЕНЫ", "MODS INSTALLED"), "clean" => T("ЧИСТЫЙ SS14", "CLEAN SS14"), "missing" => T("ВЫБЕРИТЕ ПАПКУ ИГРЫ", "SELECT GAME FOLDER"), "recovery" => T("НУЖНО ВОССТАНОВЛЕНИЕ", "RECOVERY REQUIRED"), _ => T("ТРЕБУЕТСЯ ПРОВЕРКА", "CHECK REQUIRED") };
    private void Library()
    {
        var page = Page(T("Ваш SS14. Ваши моды.", "Your SS14. Your mods."), T("Выберите набор, запустите лаунчер, подключитесь к станции.", "Choose your loadout, launch SS14, join a station."));
        var resource = typeof(LauncherWindow).Assembly.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith("readme-banner.png"));
        if (resource != null)
        {
            using var stream = typeof(LauncherWindow).Assembly.GetManifestResourceStream(resource)!;
            var hero = Section(page, 136); hero.Padding = Padding.Empty;
            var banner = new PictureBox { Dock = DockStyle.Right, Width = 408, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Surface, Image = new Bitmap(stream), AccessibleName = "SS14 ModLauncher by actemendes — space station artwork" };
            banner.Disposed += (_, _) => banner.Image?.Dispose(); page.Controls.Add(banner);
            page.Controls.Remove(banner); hero.Controls.Add(banner);
            var intro = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 18, 12, 8) }; hero.Controls.Add(intro); intro.BringToFront();
            var summary = Stack(intro);
            summary.Controls.Add(Theme.Label(T("ВАШ НАБОР НА ЭТУ СМЕНУ", "YOUR LOADOUT FOR THIS SHIFT"), 8, Theme.Blue, true));
            _selectionCount = Theme.Label($"{_settings.SelectedModIds.Count:00} / {Catalog.Bundled.Count:00}", 26, Theme.Mint, true); summary.Controls.Add(_selectionCount);
            summary.Controls.Add(Theme.Label(T("модов в профиле  ·  всё под вашим контролем", "mods in profile  ·  you're in control"), 9, Theme.Muted));
        }
        var toolbar = Section(page, 70); toolbar.Padding = new Padding(18, 13, 18, 8);
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var profiles = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, BackColor = Theme.Raised, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 4, 10, 0), AccessibleName = T("Профиль", "Profile") };
        profiles.Items.AddRange(_settings.Profiles.Keys.Cast<object>().ToArray()); profiles.SelectedItem = _settings.ActiveProfile;
        profiles.SelectedIndexChanged += (_, _) => Run(() => { _settings.ActiveProfile = (string)profiles.SelectedItem!; Save(); Render(); }, T("Профиль выбран. Применится при следующем запуске.", "Profile selected. Applies on next launch."));
        row.Controls.Add(profiles); row.Controls.Add(Theme.Button(T("Сохранить как…", "Save as…"), (_, _) => NewProfile()));
        var deleteProfile = Theme.Button(T("Удалить", "Delete"), (_, _) =>
        {
            if (MessageBox.Show(this, T("Удалить выбранный профиль? Файлы модов останутся на диске.", "Delete the selected profile? Mod files stay on disk."), T("Удалить профиль", "Delete profile"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                Run(() => { _settings.DeleteProfile(_settings.ActiveProfile); Save(); Render(); }, T("Профиль удалён.", "Profile deleted."));
        }); deleteProfile.Enabled = _settings.Profiles.Count > 1; row.Controls.Add(deleteProfile);
        var indicator = Theme.Label(StateText, 9, _installationState == "installed" ? Theme.Mint : Theme.Amber, true); indicator.Margin = new Padding(14, 11, 0, 0); row.Controls.Add(indicator); toolbar.Controls.Add(row);
        foreach (var mod in Catalog.Bundled)
        {
            var card = Section(page, 137);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            layout.Controls.Add(Theme.Label(mod.Name(_settings.Language), 15, bold: true), 0, 0);
            var description = Theme.Label(mod.Description(_settings.Language), 10, Theme.Muted); description.AutoSize = false; description.Dock = DockStyle.Fill; description.Margin = Padding.Empty; layout.Controls.Add(description, 0, 1);
            var version = ModVersion(mod);
            layout.Controls.Add(Theme.Label("v" + version + "  ·  " + (version == mod.Version ? T("в комплекте", "bundled") : T("обновлён", "updated")) + "  ·  " + (mod.Id == "crew-console" ? T("Интерфейс", "Interface") : T("Пример мода", "Sample mod")), 8, Theme.Blue), 0, 2);
            var toggle = new CheckBox { Text = T("Включён", "Enabled"), Checked = _settings.SelectedModIds.Contains(mod.Id), Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Top, Height = 38, Cursor = Cursors.Hand, BackColor = Theme.Raised, ForeColor = Theme.Mint, AccessibleName = mod.Name(_settings.Language) };
            toggle.FlatAppearance.BorderColor = Theme.Border; toggle.FlatAppearance.CheckedBackColor = Color.FromArgb(26, 62, 52);
            toggle.Text = toggle.Checked ? T("Включён", "Enabled") : T("Выключен", "Disabled");
            toggle.CheckedChanged += (_, _) => Run(() => { var selected = _settings.SelectedModIds.ToList(); selected.Remove(mod.Id); if (toggle.Checked) selected.Add(mod.Id); _settings.Profiles[_settings.ActiveProfile] = selected; Save(); if (_selectionCount != null) _selectionCount.Text = $"{_settings.SelectedModIds.Count:00} / {Catalog.Bundled.Count:00}"; toggle.Text = toggle.Checked ? T("Включён", "Enabled") : T("Выключен", "Disabled"); }, T("Выбор сохранён. Применится при следующем запуске.", "Selection saved. Applies on next launch."));
            layout.Controls.Add(toggle, 1, 0); layout.SetRowSpan(toggle, 2); _toggles[mod.Id] = toggle; card.Controls.Add(layout);
        }
        var actions = Section(page, 108); actions.Padding = new Padding(22, 14, 22, 10); var stack = Stack(actions);
        var buttons = new FlowLayoutPanel { Width = 860, Height = 48, WrapContents = false };
        buttons.Controls.Add(Theme.Button(_installationState == "clean" ? T("Установить и запустить  →", "Install & launch  →") : T("Запустить SS14  →", "Launch SS14  →"), (_, _) => Launch(false), true));
        buttons.Controls.Add(Theme.Button(T("Без модов на один запуск", "Launch once without mods"), (_, _) => Launch(true)));
        stack.Controls.Add(buttons); stack.Controls.Add(Theme.Label(T("Откроется обычный SS14 Launcher с вашей авторизацией и серверами.", "Opens the original SS14 Launcher with your account and servers."), 9, Theme.Muted));
    }
    private void NewProfile()
    {
        using var dialog = new Form { Text = T("Новый профиль", "New profile"), ClientSize = new Size(380, 145), StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Surface, ForeColor = Theme.Text, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Padding = new Padding(20) };
        var stack = Stack(dialog); var input = Theme.TextBox(""); input.Width = 330; stack.Controls.Add(input);
        var save = Theme.Button(T("Создать профиль", "Create profile"), (_, _) => { var name = input.Text.Trim(); if (name.Length is < 1 or > 40 || _settings.Profiles.ContainsKey(name)) { input.BackColor = Color.FromArgb(70, 28, 35); return; } Run(() => { _settings.AddProfile(name); Save(); dialog.DialogResult = DialogResult.OK; }, T("Профиль создан.", "Profile created.")); }, true); stack.Controls.Add(save); dialog.AcceptButton = save;
        if (dialog.ShowDialog(this) == DialogResult.OK) Render();
    }
    private void InstallationPage()
    {
        var page = Page(T("Установка и восстановление", "Install & restore"), T("Один лаунчер. Обратимые изменения. Оригинальные файлы под защитой.", "One launcher. Reversible changes. Original files preserved."));
        var target = Section(page, 235); var stack = Stack(target);
        stack.Controls.Add(Theme.Label(T("Папка SS14 Launcher", "SS14 Launcher folder"), 15, bold: true));
        stack.Controls.Add(Theme.Label(T("Укажите bin_x64 с SS14.Launcher.exe и папкой loader.", "Select bin_x64 containing SS14.Launcher.exe and the loader folder."), 10, Theme.Muted));
        var path = Theme.TextBox(_settings.LauncherPath); path.Width = 800; path.AccessibleName = T("Папка игры", "Game folder"); stack.Controls.Add(path);
        var buttons = new FlowLayoutPanel { Width = 840, Height = 50, WrapContents = false };
        buttons.Controls.Add(Theme.Button(T("Обзор…", "Browse…"), (_, _) => { using var picker = new FolderBrowserDialog { InitialDirectory = Directory.Exists(path.Text) ? path.Text : "" }; if (picker.ShowDialog(this) == DialogResult.OK) path.Text = picker.SelectedPath; }));
        buttons.Controls.Add(Theme.Button(T("Сохранить папку", "Save folder"), (_, _) => Run(() => { var full = Path.GetFullPath(path.Text.Trim()); if (!File.Exists(Path.Combine(full, "SS14.Launcher.exe"))) throw new IOException(T("В этой папке нет SS14.Launcher.exe.", "SS14.Launcher.exe was not found in this folder.")); _settings.LauncherPath = full; Save(); RefreshState(); Render(); }, T("Папка сохранена.", "Folder saved."))));
        buttons.Controls.Add(Theme.Button(T("Установить моды", "Install mods"), (_, _) => Install(), true)); stack.Controls.Add(buttons);
        stack.Controls.Add(Theme.Label(StateText, 9, Theme.Mint, true));
        var steam = Section(page, 183); stack = Stack(steam);
        stack.Controls.Add(Theme.Label(T("Запуск из Steam", "Launch from Steam") + (Installation.IsSteamEnabled(_settings.LauncherPath) ? T("  ·  включён", "  ·  enabled") : T("  ·  выключен", "  ·  disabled")), 15, bold: true));
        stack.Controls.Add(Theme.Label(T("При установке в Steam включается автоматически: «Играть» открывает ModLauncher.\nРучное отключение запоминается. Оригинальный запускатор сохраняется для восстановления.", "Enabled automatically when installing into Steam: Play opens ModLauncher.\nAn explicit opt-out is remembered. The original launcher is backed up for restoration."), 10, Theme.Muted));
        var steamButtons = new FlowLayoutPanel { Width = 850, Height = 48, WrapContents = false };
        steamButtons.Controls.Add(Theme.Button(T("Включить интеграцию", "Enable integration"), (_, _) => Run(() => { NeedRoot(); if (!AppRuntime.IsSingleFile()) throw new InvalidOperationException(T("Для интеграции используйте опубликованную single-file сборку из dist.", "Use the published single-file build from dist for Steam integration.")); Installation.EnableSteam(_settings.LauncherPath, Environment.ProcessPath!); RefreshState(); Render(); }, T("Интеграция включена. Теперь запускайте SS14 из Steam.", "Integration enabled. Launch SS14 from Steam."))));
        steamButtons.Controls.Add(Theme.Button(T("Отключить интеграцию", "Disable integration"), (_, _) => Run(() => { NeedRoot(); Installation.DisableSteam(_settings.LauncherPath); RefreshState(); Render(); }, T("Оригинальный запускатор Steam восстановлен.", "Original Steam launcher restored.")))); stack.Controls.Add(steamButtons);
        var restore = Section(page, 183); stack = Stack(restore);
        stack.Controls.Add(Theme.Label(T("Вернуть чистый SS14", "Restore clean SS14"), 15, Theme.Amber, true));
        stack.Controls.Add(Theme.Label(T("Восстанавливает исходный загрузчик и отключает интеграцию со Steam.\nВаши профили и файлы модов сохранятся. Закройте игру и обычный лаунчер.", "Restores the original loader and removes Steam integration.\nYour profiles and mod files stay on disk. Close the game and the original launcher first."), 10, Theme.Muted));
        stack.Controls.Add(Theme.Button(T("Восстановить оригинал", "Restore originals"), (_, _) => { if (MessageBox.Show(this, T("Отключить моды и восстановить проверенные оригинальные файлы SS14?", "Disable mods and restore the verified original SS14 files?"), T("Восстановление SS14", "Restore SS14"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) Run(() => { NeedRoot(); Installation.Restore(_settings.LauncherPath); RefreshState(); Render(); }, T("Чистый SS14 восстановлен. Профили сохранены.", "Clean SS14 restored. Profiles preserved.")); }));
    }
    private void UpdatesPage()
    {
        var page = Page(T("Обновления", "Updates"), T("Моды и лаунчер обновляются независимо. Установка — по вашему выбору.", "Mods and launcher update independently. You choose when to install."));
        var source = Section(page, 304); var stack = Stack(source);
        stack.Controls.Add(Theme.Label(T("Источник релизов GitHub", "GitHub release source"), 15, bold: true));
        stack.Controls.Add(Theme.Label(T("Проверяем при открытии ModLauncher. Ничего не устанавливаем автоматически.\nИспользуйте доверенный репозиторий автора в формате owner/repository.", "Check when ModLauncher opens. Nothing is installed automatically.\nUse the author's trusted repository in owner/repository format."), 10, Theme.Muted));
        var repo = Theme.TextBox(_settings.UpdateRepository); repo.PlaceholderText = "owner/repository"; stack.Controls.Add(repo);
        repo.TextChanged += (_, _) => _automaticUpdates.Cancel();
        var automatic = new CheckBox { Text = T("Проверять обновления при запуске", "Check for updates at startup"), Checked = _settings.CheckUpdatesOnStartup, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 12) };
        automatic.CheckedChanged += (_, _) => Run(() => { _settings.CheckUpdatesOnStartup = automatic.Checked; if (!automatic.Checked) { _automaticUpdates.Cancel(); _updateChecking = false; RefreshUpdateCheckState(); } Save(); }, T("Настройка уведомлений сохранена.", "Update notification preference saved."));
        stack.Controls.Add(automatic);
        var row = new FlowLayoutPanel { Width = 830, Height = 48, WrapContents = false };
        void SaveSource()
        {
            var value = repo.Text.Trim();
            if (value.Length > 0 && !UpdateService.IsValidRepository(value)) throw new InvalidOperationException(T("Укажите источник в формате owner/repository.", "Use owner/repository for the source."));
            _automaticUpdates.Cancel(); _settings.UpdateRepository = value; _updateProblem = null; Save();
        }
        row.Controls.Add(Theme.Button(T("Проверить обновления", "Check for updates"), async (_, _) => await RunAsync(async () =>
        {
            SaveSource();
            if (string.IsNullOrEmpty(_settings.UpdateRepository)) throw new InvalidOperationException(T("Сначала укажите GitHub-репозиторий автора.", "Enter the author's GitHub repository first."));
            _available = null; _availableContext = null; _updateChecking = true;
            try
            {
                var context = UpdateContext();
                _available = await _updates.CheckAsync(_settings.UpdateRepository, string.IsNullOrWhiteSpace(_settings.LauncherPath) ? null : _settings.VersionsFor(_settings.LauncherPath), _lifetime.Token);
                _availableContext = context;
            }
            catch (Exception error) { _updateProblem = ErrorText.Message(error, _settings.Language); throw; }
            finally { _updateChecking = false; Render(); }
        }, T("Проверка завершена.", "Check complete.")), true));
        row.Controls.Add(Theme.Button(T("Сохранить источник", "Save source"), (_, _) => Run(() => { SaveSource(); Render(); }, T("Источник сохранён. Пустой источник отключает проверки.", "Source saved. An empty source disables checks."))));
        stack.Controls.Add(row);
        _updateStateLabel = Theme.Label("", 9, Theme.Muted); stack.Controls.Add(_updateStateLabel); RefreshUpdateCheckState();
        if (_available is { } update)
        {
            var result = Section(page, 200 + 24 * (update.Mods.Count + update.BlockedMods.Count) + (update.HasLauncherUpdate ? 48 : 0)); stack = Stack(result);
            var summary = Theme.Label(UpdateSummary(), 13, Theme.Mint, true); summary.MaximumSize = new Size(840, 0); stack.Controls.Add(summary);
            foreach (var mod in update.Mods)
                stack.Controls.Add(Theme.Label((Catalog.ById(mod.Id)?.Name(_settings.Language) ?? mod.Id) + "  →  " + mod.Version, 10));
            foreach (var mod in update.BlockedMods)
                stack.Controls.Add(Theme.Label((Catalog.ById(mod.Id)?.Name(_settings.Language) ?? mod.Id) + "  " + mod.Version + T(" — нужен ModLauncher ", " — requires ModLauncher ") + mod.MinLauncherVersion, 10, Theme.Amber));
            var actions = new FlowLayoutPanel { Width = 820, Height = 50, WrapContents = false };
            var install = Theme.Button(T("Загрузить и применить моды", "Download & apply mods"), async (_, _) => await RunAsync(async () => { NeedRoot(); var files = await _updates.DownloadAsync(update, _lifetime.Token); var payload = CurrentPayload(); foreach (var (name, bytes) in files) payload[name] = bytes; Installation.Install(_settings.LauncherPath, payload, SelectedFiles, _settings.Language); RecordBundledVersions(payload); _settings.RecordInstalledVersions(_settings.LauncherPath, update.Mods); Save(); _available = update with { Mods = [] }; _availableContext = UpdateContext(); RefreshState(); Render(); }, T("Пакеты проверены и установлены.", "Packages verified and installed.")), true);
            install.Enabled = update.Mods.Count > 0; actions.Controls.Add(install);
            actions.Controls.Add(Theme.Button(T("Открыть релиз", "Open release"), (_, _) => Open(update.ReleaseUrl))); stack.Controls.Add(actions);
            if (update.HasLauncherUpdate)
                stack.Controls.Add(Theme.Button(T("Скачать ModLauncher ", "Download ModLauncher ") + update.LauncherVersion, (_, _) => Open(string.IsNullOrEmpty(update.LauncherDownloadUrl) ? update.ReleaseUrl : update.LauncherDownloadUrl)));
            stack.Controls.Add(Theme.Label(T("Закройте игру перед обновлением модов. Лаунчер устанавливается отдельно из ZIP.", "Close the game before applying mods. Install the launcher separately from its ZIP."), 9, Theme.Muted));
        }
    }
    private void Diagnostics()
    {
        var page = Page(T("Диагностика", "Diagnostics"), T("Проверьте состояние установки перед запуском или отправкой отчёта об ошибке.", "Check installation health before launching or reporting an issue."));
        var card = Section(page, 360); var stack = Stack(card);
        stack.Controls.Add(Theme.Label(StateText, 16, _installationState == "installed" ? Theme.Mint : Theme.Amber, true));
        var details = new TextBox { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Muted, Font = new Font("Consolas", 10), Width = 820, Height = 185, ScrollBars = ScrollBars.Vertical, Text = DiagnosticText() }; stack.Controls.Add(details);
        var actions = new FlowLayoutPanel { Width = 850, Height = 60, WrapContents = false };
        actions.Controls.Add(Theme.Button(T("Проверить снова", "Check again"), (_, _) => { RefreshState(); Render(); }));
        actions.Controls.Add(Theme.Button(T("Сохранить отчёт…", "Save report…"), (_, _) => Run(() => { using var file = new SaveFileDialog { FileName = "SS14ModLauncher-diagnostics.txt", Filter = "Text|*.txt" }; if (file.ShowDialog(this) == DialogResult.OK) File.WriteAllText(file.FileName, DiagnosticText()); }, T("Отчёт сохранён локально.", "Report saved locally."))));
        actions.Controls.Add(Theme.Button(T("Открыть логи", "Open logs"), (_, _) => Run(() => { var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SS14LocalMods"); Directory.CreateDirectory(folder); Open(folder); }, T("Папка логов открыта.", "Log folder opened.")))); stack.Controls.Add(actions);
        if (_settings.IsReadOnly)
        {
            var recover = Section(page, 100); var recovery = Stack(recover);
            recovery.Controls.Add(Theme.Button(T("Сохранить повреждённые настройки и сбросить", "Back up damaged settings & reset"), (_, _) => Run(() => { _settings.Recover(); RefreshState(); Render(); }, T("Повреждённый файл сохранён отдельно. Настройки сброшены.", "Damaged file backed up. Settings reset."))));
        }
        var help = Section(page, 190); stack = Stack(help);
        stack.Controls.Add(Theme.Label(T("Если Steam обновил игру", "If Steam updated the game"), 15, bold: true));
        stack.Controls.Add(Theme.Label(T("При неизвестном хеше лаунчер остановит запись. Не удаляйте резервную копию.\nЗакройте SS14, проверьте файлы игры через Steam и повторите диагностику.\nЗапуск без модов помогает проверить, связан ли сбой с выбранным набором.", "An unknown hash stops writes. Keep your backup.\nClose SS14, verify game files in Steam, then check again.\nLaunching without mods helps isolate problems with your current loadout."), 10, Theme.Muted));
        stack.Controls.Add(Theme.Button(T("Принять проверенные файлы Steam…", "Adopt verified Steam files…"), (_, _) =>
        {
            if (MessageBox.Show(this, T("Продолжайте только после проверки целостности файлов SS14 в Steam.\n\nПринять текущие файлы как новые оригиналы? Предыдущие резервные копии будут сохранены в истории.", "Continue only after verifying SS14 files in Steam.\n\nAdopt the current files as new originals? Previous backups will be archived."), T("После обновления Steam", "After a Steam update"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                Run(() => { NeedRoot(); Installation.RebaseAfterPlatformUpdate(_settings.LauncherPath); RefreshState(); Render(); }, T("Новые оригиналы приняты; старые копии сохранены. Можно установить моды заново.", "New originals accepted; previous backups archived. Install mods again."));
        }));
    }
    private string DiagnosticText() => $"SS14 ModLauncher {Program.Version}\r\nWindows: {Environment.OSVersion.Version}\r\nArchitecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}\r\nState: {_installationState}\r\n{T("Выбрано модов", "Selected mods")}: {string.Join(", ", _settings.SelectedModIds)}\r\n{T("Состояние", "Status")}: {_installationDetail.Replace(_settings.LauncherPath.Length > 0 ? _settings.LauncherPath : "<none>", "<game-folder>")}\r\nSettings: {(_settings.IsReadOnly ? "read-only / damaged" : "OK")}\r\n{T("Отчёт не включает аккаунты, токены или игровые сообщения.", "Report excludes accounts, tokens and game messages.")}";
    private void About()
    {
        var page = Page(T("Создан для вашего клиента", "Built for your client"), "SS14 ModLauncher by actemendes  /  v" + Program.Version);
        var card = Section(page, 290); var stack = Stack(card);
        stack.Controls.Add(Theme.Label(T("Независимый проект сообщества", "An independent community project"), 17, Theme.Mint, true));
        stack.Controls.Add(Theme.Label(T("Не является официальным продуктом Space Wizards Federation или Valve.\nМоды загружаются локально, вне кэша серверного контента.\nАвторизация и проверка подписи движка остаются штатными.\n\nПервая версия: Windows x64. Базовая совместимость: Launcher 0.40.2 / Robust 275.\nСерверные сборки различаются; учитывайте правила сервера.\nЯзык лаунчера: русский / английский. Язык каждого мода зависит от его реализации.", "Not an official Space Wizards Federation or Valve product.\nMods load locally, outside the server content cache.\nNormal authentication and engine signature verification remain intact.\n\nFirst release: Windows x64. Baseline: Launcher 0.40.2 / Robust 275.\nServer builds differ; follow the rules of the server you join.\nLauncher languages: Russian / English. Individual mods manage their own language."), 10, Theme.Muted));
        var guide = Section(page, 150); stack = Stack(guide);
        stack.Controls.Add(Theme.Label(T("От модов к своей станции", "From loadout to station"), 15, bold: true));
        stack.Controls.Add(Theme.Label(T("01   Выберите папку игры во вкладке «Установка».\n02   Включите моды и сохраните свой профиль.\n03   Нажмите «Запустить SS14» и подключитесь через обычный лаунчер.", "01   Choose your game folder in Installation.\n02   Enable mods and save your profile.\n03   Launch SS14 and connect through the original launcher."), 10, Theme.Muted));
    }
    private void NeedRoot() { if (_settings.IsReadOnly) throw new InvalidOperationException(T("Сначала восстановите повреждённые настройки на вкладке «Диагностика».", "Recover damaged settings in Diagnostics first.")); if (string.IsNullOrWhiteSpace(_settings.LauncherPath)) { _view = "installation"; Render(); throw new InvalidOperationException(T("Сначала выберите папку SS14 Launcher.", "Choose the SS14 Launcher folder first.")); } }
    private Dictionary<string, byte[]> CurrentPayload()
    {
        return Payload.ForInstallation(_settings.LauncherPath);
    }
    private string ModVersion(ModDefinition mod)
    {
        try { return !string.IsNullOrWhiteSpace(_settings.LauncherPath) && _settings.VersionsFor(_settings.LauncherPath).TryGetValue(mod.Id, out var version) ? version : mod.Version; }
        catch { return mod.Version; }
    }
    private void RecordBundledVersions(IReadOnlyDictionary<string, byte[]> payload)
    {
        var bundled = Payload.Read();
        _settings.RecordInstalledVersions(_settings.LauncherPath, Catalog.Bundled.Where(mod => payload.TryGetValue(mod.File, out var bytes) && bytes.AsSpan().SequenceEqual(bundled[mod.File])).Select(mod => new ModUpdate { Id = mod.Id, Version = mod.Version }));
    }
    private void Install() => Run(() => { NeedRoot(); var payload = CurrentPayload(); AppRuntime.Install(_settings.LauncherPath, payload, SelectedFiles, _settings.Language); RecordBundledVersions(payload); Save(); RefreshState(); Render(); }, T("Установка завершена. Для Steam запуск ModLauncher включается автоматически, если вы его не отключали.", "Installation complete. Steam opens ModLauncher automatically unless you previously opted out."));
    private void Launch(bool clean)
    {
        Run(() =>
        {
            NeedRoot(); RefreshState();
            if (_installationState == "installed") Installation.SetSelection(_settings.LauncherPath, clean ? [] : SelectedFiles, _settings.Language);
            else if (!clean) { var payload = CurrentPayload(); AppRuntime.Install(_settings.LauncherPath, payload, SelectedFiles, _settings.Language); RecordBundledVersions(payload); }
            else if (_installationState != "clean") throw new InvalidOperationException(T("Сначала восстановите чистую установку на вкладке «Установка».", "Restore a clean installation in the Installation tab first."));
            var executable = Installation.GetLaunchExecutable(_settings.LauncherPath);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = _settings.LauncherPath };
            foreach (var arg in Program.ForwardedArguments) start.ArgumentList.Add(arg);
            if (clean) start.Environment["SS14_MODS_DISABLED"] = "1";
            else start.Environment.Remove("SS14_MODS_DISABLED");
            Save(); Process.Start(start); RefreshState(); Render();
        }, clean ? T("SS14 запущен без модов. Следующий запуск снова применит профиль.", "SS14 launched without mods. Next launch restores your profile.") : T("SS14 Launcher открыт. Подключитесь к серверу как обычно.", "SS14 Launcher opened. Connect to a server as usual."));
    }
    private void Open(string location) => Process.Start(new ProcessStartInfo(location) { UseShellExecute = true });
    private void Run(Action action, string success)
    {
        if (_busy) return;
        try { action(); SetStatus(success); }
        catch (Exception e) { SetStatus(T("Ошибка: ", "Error: ") + ErrorText.Message(e, _settings.Language), true); }
    }
    private async Task RunAsync(Func<Task> action, string success)
    {
        if (_busy) return;
        _busy = true; _page.Enabled = false; _navigation.Enabled = false; UseWaitCursor = true; SetStatus(T("Выполняется…", "Working…"));
        try { await action(); SetStatus(success); }
        catch (Exception e) { SetStatus(T("Ошибка: ", "Error: ") + ErrorText.Message(e, _settings.Language), true); }
        finally { _busy = false; _page.Enabled = true; _navigation.Enabled = true; UseWaitCursor = false; }
    }
}
