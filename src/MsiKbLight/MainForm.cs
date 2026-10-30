using System.Diagnostics;
using Microsoft.Win32;

namespace MsiKbLight;

/// <summary>Settings window. Changes are previewed on the keyboard immediately; Save makes them persistent.</summary>
internal sealed class MainForm : Form
{
    // The controller accepts 0-10 but caps anything above 8.
    private const int UiMaxBrightness = 8;

    private static readonly LightMode[] Modes =
    [
        LightMode.Off, LightMode.Static, LightMode.Breathing, LightMode.Flashing,
        LightMode.DoubleFlashing, LightMode.ColorShift, LightMode.Rainbow,
    ];

    private static readonly ThemePreference[] Themes = [ThemePreference.System, ThemePreference.Light, ThemePreference.Dark];
    private static readonly UiLanguage[] Languages = [UiLanguage.Auto, UiLanguage.Japanese, UiLanguage.English];

    private static readonly Color[] DefaultShiftColors =
    [
        Color.FromArgb(255, 0, 0), Color.FromArgb(255, 80, 0), Color.FromArgb(255, 255, 0),
        Color.FromArgb(0, 255, 0), Color.FromArgb(0, 0, 255), Color.FromArgb(150, 0, 255),
        Color.FromArgb(255, 0, 255),
    ];

    private readonly Label _deviceLabel = new() { AutoSize = true };
    private readonly Label _msiCenterWarning = new() { AutoSize = true, Visible = false };

    private readonly Label _lightingHeader = SectionHeader();
    private readonly Label _autoApplyHeader = SectionHeader();
    private readonly Label _displayHeader = SectionHeader();
    private readonly Label _releaseHeader = SectionHeader();

    private readonly Label _modeLabel = RowLabel();
    private readonly Label _colorLabel = RowLabel();
    private readonly Label _brightnessLabel = RowLabel();
    private readonly Label _speedLabel = RowLabel();
    private readonly Label _themeLabel = RowLabel();
    private readonly Label _languageLabel = RowLabel();

    private readonly ComboBox _mode = DropDown(200);
    private readonly ComboBox _colorCount = DropDown(60);
    private readonly Label _colorCountLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly FlowLayoutPanel _colorPanel = new() { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
    private readonly Button[] _colorButtons = new Button[KeyboardLight.MaxColors];
    private readonly TrackBar _brightness = new()
    {
        Minimum = 0, Maximum = UiMaxBrightness, TickStyle = TickStyle.None, Width = 220, LargeChange = 1, AutoSize = false, Height = 30,
    };
    private readonly Label _brightnessValue = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly ComboBox _speed = DropDown(120);
    private readonly Button _save = new() { AutoSize = true, MinimumSize = new Size(88, 30) };
    private readonly Button _revert = new() { AutoSize = true, MinimumSize = new Size(88, 30) };

    private readonly Label _autoApplyStatus = new() { AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(320, 0) };
    private readonly Button _autoApplyToggle = new() { AutoSize = true, MinimumSize = new Size(88, 30), Anchor = AnchorStyles.Right };

    private readonly ComboBox _theme = DropDown(220);
    private readonly ComboBox _language = DropDown(220);

    private readonly Label _releaseDescription = new() { AutoSize = true, MaximumSize = new Size(320, 0), Anchor = AnchorStyles.Left };
    private readonly Button _release = new() { AutoSize = true, MinimumSize = new Size(88, 30), Anchor = AnchorStyles.Right };

    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(2, 10, 0, 0) };
    private readonly System.Windows.Forms.Timer _applyTimer = new() { Interval = 120 };

    private readonly List<CardPanel> _cards = [];
    private readonly Button[] _textButtons;
    private readonly ComboBox[] _combos;
    private readonly Label[] _headers;

    private readonly UiPreferences _prefs = UiPreferences.Load();
    private Palette _palette;
    private Font? _headerFont;

    private string? _savedSnapshot;
    private string? _baselineSnapshot;
    private LightSettings? _initialSettings;
    private bool _loading;
    private bool? _deviceFound;
    private ushort _deviceProductId;
    private AutoApplyState _autoApplyState;
    private bool _skipClosePrompt;

    private static Strings S => Strings.Current;

    public MainForm()
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        }
        catch (Exception)
        {
            // Default icon is fine.
        }

        _textButtons = [_save, _revert, _autoApplyToggle, _release];
        _combos = [_mode, _colorCount, _speed, _theme, _language];
        _headers = [_lightingHeader, _autoApplyHeader, _displayHeader, _releaseHeader];

        BuildLayout();

        for (int i = 1; i <= KeyboardLight.MaxColors; i++)
            _colorCount.Items.Add(i.ToString());

        Strings.Use(_prefs.Language);
        _palette = Theme.Resolve(_prefs.Theme);
        ApplyTexts();
        ApplyTheme();

        foreach (var combo in _combos)
        {
            var c = combo;
            c.HandleCreated += (_, _) => Theme.ApplyControlTheme(c, _palette.IsDark);
        }

        _mode.SelectedIndexChanged += (_, _) => { UpdateControlStates(); OnSettingChanged(); };
        _colorCount.SelectedIndexChanged += (_, _) => { UpdateControlStates(); OnSettingChanged(); };
        _brightness.ValueChanged += (_, _) => { _brightnessValue.Text = $"{_brightness.Value} / {UiMaxBrightness}"; OnSettingChanged(); };
        _speed.SelectedIndexChanged += (_, _) => OnSettingChanged();
        _theme.SelectedIndexChanged += (_, _) => OnThemeSelected();
        _language.SelectedIndexChanged += (_, _) => OnLanguageSelected();
        _applyTimer.Tick += (_, _) => { _applyTimer.Stop(); ApplyToDevice(CurrentSettings()); };
        _save.Click += (_, _) => Save();
        _save.EnabledChanged += (_, _) => StyleButton(_save);
        _revert.Click += (_, _) => Revert();
        _autoApplyToggle.Click += (_, _) => ToggleAutoApply();
        _release.Click += (_, _) => ReleaseControl();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        Load += (_, _) => Initialize();
        FormClosing += OnFormClosing;
    }

    private static Label SectionHeader() => new() { AutoSize = true, Margin = new Padding(2, 14, 0, 6) };

    private static Label RowLabel() => new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 16, 8) };

    private static ComboBox DropDown(int width) => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };

    private CardPanel NewCard(int columns)
    {
        var card = new CardPanel
        {
            AutoSize = true,
            ColumnCount = columns,
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 8, 14, 8),
            Margin = new Padding(0),
        };
        for (int i = 0; i < columns; i++)
            card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _cards.Add(card);
        return card;
    }

    private static void AddRow(TableLayoutPanel grid, Label label, Control control)
    {
        grid.Controls.Add(label);
        control.Margin = new Padding(0, 4, 0, 4);
        grid.Controls.Add(control);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(18, 12, 18, 14),
            Dock = DockStyle.Fill,
        };

        var header = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(2, 0, 0, 0) };
        header.Controls.Add(_deviceLabel);
        header.Controls.Add(_msiCenterWarning);
        root.Controls.Add(header);

        // Lighting
        root.Controls.Add(_lightingHeader);
        var lighting = NewCard(2);
        AddRow(lighting, _modeLabel, _mode);

        var countRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        _colorCountLabel.Margin = new Padding(0, 7, 6, 0);
        countRow.Controls.Add(_colorCountLabel);
        countRow.Controls.Add(_colorCount);
        var colorBlock = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        colorBlock.Controls.Add(countRow);
        colorBlock.Controls.Add(_colorPanel);
        for (int i = 0; i < _colorButtons.Length; i++)
        {
            var index = i;
            var button = new Button
            {
                Width = 34,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 2, 6, 2),
                Tag = index,
            };
            button.Click += (_, _) => PickColor(index);
            _colorButtons[i] = button;
            _colorPanel.Controls.Add(button);
        }
        AddRow(lighting, _colorLabel, colorBlock);

        var brightnessRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        brightnessRow.Controls.Add(_brightness);
        _brightnessValue.Margin = new Padding(4, 6, 0, 0);
        brightnessRow.Controls.Add(_brightnessValue);
        AddRow(lighting, _brightnessLabel, brightnessRow);
        AddRow(lighting, _speedLabel, _speed);
        root.Controls.Add(lighting);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 10, 0, 0),
        };
        _revert.Margin = new Padding(0, 0, 8, 0);
        _save.Margin = Padding.Empty;
        buttons.Controls.Add(_save);
        buttons.Controls.Add(_revert);
        root.Controls.Add(buttons);

        // Auto-apply
        root.Controls.Add(_autoApplyHeader);
        var autoApply = NewCard(2);
        autoApply.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100);
        autoApply.Controls.Add(_autoApplyStatus);
        autoApply.Controls.Add(_autoApplyToggle);
        root.Controls.Add(autoApply);

        // Appearance
        root.Controls.Add(_displayHeader);
        var display = NewCard(2);
        AddRow(display, _themeLabel, _theme);
        AddRow(display, _languageLabel, _language);
        root.Controls.Add(display);

        // Release
        root.Controls.Add(_releaseHeader);
        var release = NewCard(2);
        release.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100);
        _releaseDescription.Margin = new Padding(0, 6, 16, 6);
        release.Controls.Add(_releaseDescription);
        release.Controls.Add(_release);
        root.Controls.Add(release);

        root.Controls.Add(_status);

        Controls.Add(root);
        AcceptButton = _save;
    }

    // ---- Language and theme ----

    private void ApplyTexts()
    {
        var s = S;
        SuspendLayout();
        var oldFont = Font;
        Font = new Font(s.FontFamily, 9.75f);
        if (!ReferenceEquals(oldFont, DefaultFont))
            oldFont.Dispose();
        var oldHeaderFont = _headerFont;
        _headerFont = new Font(s.FontFamily + " Semibold", 11f);
        foreach (var h in _headers)
            h.Font = _headerFont;
        oldHeaderFont?.Dispose();

        _lightingHeader.Text = s.LightingSection;
        _autoApplyHeader.Text = s.AutoApplySection;
        _displayHeader.Text = s.DisplaySection;
        _releaseHeader.Text = s.ReleaseSection;
        _modeLabel.Text = s.ModeLabel;
        _colorLabel.Text = s.ColorLabel;
        _colorCountLabel.Text = s.ColorCountLabel;
        _brightnessLabel.Text = s.BrightnessLabel;
        _speedLabel.Text = s.SpeedLabel;
        _themeLabel.Text = s.ThemeLabel;
        _languageLabel.Text = s.LanguageLabel;
        _save.Text = s.Save;
        _revert.Text = s.Revert;
        _release.Text = s.ReleaseButton;
        _releaseDescription.Text = s.ReleaseDescription;
        _msiCenterWarning.Text = s.MsiCenterWarning;

        ReplaceItems(_mode, Modes.Select(s.ModeName));
        ReplaceItems(_speed, [s.SpeedSlow, s.SpeedNormal, s.SpeedFast]);
        ReplaceItems(_theme, [s.ThemeSystem, s.ThemeLight, s.ThemeDark], Array.IndexOf(Themes, _prefs.Theme));
        ReplaceItems(_language, [s.LanguageAuto, "日本語", "English"], Array.IndexOf(Languages, _prefs.Language));

        UpdateDeviceLabel();
        UpdateAutoApplyLabels();
        UpdateDirtyState();
        if (_mode.SelectedIndex < 0)
            Text = s.AppTitle;
        ResumeLayout(true);
    }

    private void ReplaceItems(ComboBox combo, IEnumerable<string> items, int? select = null)
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            var index = select ?? combo.SelectedIndex;
            combo.BeginUpdate();
            combo.Items.Clear();
            combo.Items.AddRange(items.Cast<object>().ToArray());
            combo.SelectedIndex = index >= 0 && index < combo.Items.Count ? index : -1;
            combo.EndUpdate();
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private void ApplyTheme()
    {
        var p = _palette = Theme.Resolve(_prefs.Theme);
        SuspendLayout();
        BackColor = p.Window;
        ForeColor = p.Text;
        foreach (var card in _cards)
        {
            card.BackColor = p.Card;
            card.BorderColor = p.CardBorder;
            card.Invalidate();
        }
        foreach (var h in _headers)
            h.ForeColor = p.Text;
        foreach (var b in _textButtons)
            StyleButton(b);
        foreach (var b in _colorButtons)
            b.FlatAppearance.BorderColor = p.ButtonBorder;
        foreach (var combo in _combos)
        {
            combo.BackColor = p.Input;
            combo.ForeColor = p.Text;
            Theme.ApplyControlTheme(combo, p.IsDark);
        }
        _colorCountLabel.ForeColor = p.Text;
        _brightness.BackColor = p.Card;
        _brightnessValue.ForeColor = p.SubtleText;
        _releaseDescription.ForeColor = p.SubtleText;
        _status.ForeColor = p.SubtleText;
        _msiCenterWarning.ForeColor = p.Error;
        UpdateDeviceLabel();
        UpdateAutoApplyLabels();
        Theme.ApplyFrame(this, p);
        ResumeLayout(true);
        Invalidate(true);
    }

    private void StyleButton(Button b)
    {
        var p = _palette;
        var primary = b == _save && b.Enabled;
        b.FlatStyle = FlatStyle.Flat;
        b.UseVisualStyleBackColor = false;
        b.BackColor = primary ? p.Accent : p.Button;
        b.ForeColor = primary ? p.OnAccent : p.Text;
        b.FlatAppearance.BorderColor = primary ? p.Accent : p.ButtonBorder;
        b.FlatAppearance.MouseOverBackColor = primary ? ControlPaint.Light(p.Accent, 0.1f) : p.ButtonHover;
        b.FlatAppearance.MouseDownBackColor = primary ? ControlPaint.Dark(p.Accent, 0.05f) : p.ButtonBorder;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyFrame(this, _palette);
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if ((e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.Accessibility)
            && IsHandleCreated && !IsDisposed)
            BeginInvoke(ApplyTheme);
    }

    private void OnThemeSelected()
    {
        if (_loading || _theme.SelectedIndex < 0)
            return;
        _prefs.Theme = Themes[_theme.SelectedIndex];
        _prefs.Save();
        ApplyTheme();
    }

    private void OnLanguageSelected()
    {
        if (_loading || _language.SelectedIndex < 0)
            return;
        _prefs.Language = Languages[_language.SelectedIndex];
        _prefs.Save();
        Strings.Use(_prefs.Language);
        ApplyTexts();
        SetStatus(""); // The old message is in the previous language.
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.OnFormClosed(e);
    }

    // ---- Lighting ----

    private void Initialize()
    {
        _loading = true;
        try
        {
            SetStatus("");
            _savedSnapshot = null;
            LightSettings? settings = null;
            try
            {
                settings = LightSettings.Load();
                _savedSnapshot = settings?.ToString();
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(S.StatusLoadFailed, ex.Message));
            }

            using (var light = KeyboardLight.Open())
            {
                _deviceFound = light is not null;
                _deviceProductId = light?.ProductId ?? 0;
                if (light is not null && settings is null)
                {
                    try
                    {
                        settings = light.Read();
                    }
                    catch (Exception ex)
                    {
                        SetStatus(string.Format(S.StatusReadFailed, ex.Message));
                    }
                }
            }
            UpdateDeviceLabel();

            _initialSettings = settings;
            ShowSettings(settings ?? new LightSettings());
            _baselineSnapshot = CurrentSettings().ToString();
            UpdateDirtyState();
            _msiCenterWarning.Visible = IsMsiCenterRunning();
            RefreshAutoApplyState();
            if (string.IsNullOrEmpty(_status.Text))
                SetStatus(_savedSnapshot is null ? S.StatusNoSaved : S.StatusLoadedSaved);
        }
        finally
        {
            _loading = false;
        }
    }

    private void UpdateDeviceLabel()
    {
        if (_deviceFound is null)
        {
            _deviceLabel.Text = "";
            return;
        }
        _deviceLabel.Text = _deviceFound.Value ? string.Format(S.DeviceFound, _deviceProductId) : S.DeviceNotFound;
        _deviceLabel.ForeColor = _deviceFound.Value ? _palette.SubtleText : _palette.Error;
    }

    private void ShowSettings(LightSettings settings)
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            _mode.SelectedIndex = Math.Max(0, Array.IndexOf(Modes, settings.Mode));
            _brightness.Value = Math.Clamp(settings.Brightness, 0, UiMaxBrightness);
            _brightnessValue.Text = $"{_brightness.Value} / {UiMaxBrightness}";
            _speed.SelectedIndex = Math.Clamp(settings.Speed, 0, KeyboardLight.MaxSpeed);

            List<(byte R, byte G, byte B)> colors;
            try
            {
                colors = settings.ParseColors();
            }
            catch (ArgumentException)
            {
                colors = [(255, 255, 255)];
            }
            for (int i = 0; i < _colorButtons.Length; i++)
            {
                _colorButtons[i].BackColor = i < colors.Count
                    ? Color.FromArgb(colors[i].R, colors[i].G, colors[i].B)
                    : DefaultShiftColors[i];
            }
            var count = settings.Mode == LightMode.ColorShift
                ? Math.Clamp(colors.Count, 1, KeyboardLight.MaxColors)
                : KeyboardLight.MaxColors;
            _colorCount.SelectedIndex = count - 1;
            UpdateControlStates();
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private LightMode SelectedMode => Modes[Math.Max(0, _mode.SelectedIndex)];

    private int VisibleColorCount => SelectedMode switch
    {
        LightMode.Off or LightMode.Rainbow => 0,
        LightMode.ColorShift => Math.Max(0, _colorCount.SelectedIndex) + 1,
        _ => 1,
    };

    private void UpdateControlStates()
    {
        var mode = SelectedMode;
        var count = VisibleColorCount;
        for (int i = 0; i < _colorButtons.Length; i++)
            _colorButtons[i].Visible = i < count;
        _colorPanel.Visible = count > 0;
        _colorCountLabel.Visible = _colorCount.Visible = mode == LightMode.ColorShift;
        _speed.Enabled = mode is not (LightMode.Off or LightMode.Static);
        _brightness.Enabled = mode != LightMode.Off;
        UpdateDirtyState();
    }

    private LightSettings CurrentSettings()
    {
        var count = Math.Max(1, VisibleColorCount);
        return new LightSettings
        {
            Mode = SelectedMode,
            Speed = Math.Max(0, _speed.SelectedIndex),
            Brightness = _brightness.Value,
            Colors = _colorButtons.Take(count).Select(b => $"{b.BackColor.R:X2}{b.BackColor.G:X2}{b.BackColor.B:X2}").ToList(),
        };
    }

    private void OnSettingChanged()
    {
        if (_loading)
            return;
        UpdateDirtyState();
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    // Compared against what the window showed when it opened / was last saved, so a fresh start with
    // nothing saved is not reported as a change.
    private bool IsDirty => _baselineSnapshot is not null && CurrentSettings().ToString() != _baselineSnapshot;

    private void UpdateDirtyState()
    {
        if (_mode.SelectedIndex < 0)
            return;
        var dirty = IsDirty;
        _save.Enabled = dirty || _savedSnapshot is null;
        _revert.Enabled = dirty;
        Text = dirty ? S.AppTitle + " *" : S.AppTitle;
    }

    private bool ApplyToDevice(LightSettings settings)
    {
        try
        {
            using var light = KeyboardLight.Open();
            if (light is null)
            {
                SetStatus(S.StatusDeviceMissing);
                return false;
            }
            LightSettings.CaptureOriginal(light);
            light.Write(settings);
            SetStatus(IsDirty ? S.StatusPreview : S.StatusApplied);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(S.StatusApplyFailed, ex.Message));
            return false;
        }
    }

    private void PickColor(int index)
    {
        using var dialog = new ColorDialog { Color = _colorButtons[index].BackColor, FullOpen = true, AnyColor = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        _colorButtons[index].BackColor = dialog.Color;
        OnSettingChanged();
    }

    private void Save()
    {
        var settings = CurrentSettings();
        try
        {
            settings.Validate();
            // Record the pre-tool state before the first config.json exists.
            using (var light = KeyboardLight.Open())
            {
                if (light is not null)
                    LightSettings.CaptureOriginal(light);
            }
            settings.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(S.SaveFailed, ex.Message), S.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _savedSnapshot = settings.ToString();
        _baselineSnapshot = _savedSnapshot;
        UpdateDirtyState();
        if (ApplyToDevice(settings))
            SetStatus(TaskInstaller.IsRegistered() ? S.StatusSavedPersistent : S.StatusSavedNeedsAutoApply);
    }

    private void Revert()
    {
        var target = LightSettings.Load() ?? _initialSettings;
        if (target is null)
            return;
        ShowSettings(target);
        ApplyToDevice(target);
    }

    // ---- Auto-apply ----

    private void RefreshAutoApplyState()
    {
        _autoApplyState = TaskInstaller.GetState();
        UpdateAutoApplyLabels();
    }

    private void UpdateAutoApplyLabels()
    {
        var (status, button, color) = _autoApplyState switch
        {
            AutoApplyState.Enabled => (S.AutoApplyEnabled, S.Disable, _palette.Success),
            AutoApplyState.NeedsRepair => (S.AutoApplyNeedsRepair, S.Repair, _palette.Error),
            AutoApplyState.Outdated => (S.AutoApplyOutdated, S.Update, _palette.Error),
            _ => (S.AutoApplyDisabled, S.Enable, _palette.SubtleText),
        };
        _autoApplyStatus.Text = status;
        _autoApplyStatus.ForeColor = color;
        _autoApplyToggle.Text = button;
    }

    private void ToggleAutoApply()
    {
        var disabling = _autoApplyState == AutoApplyState.Enabled;
        if (_autoApplyState == AutoApplyState.Disabled && _savedSnapshot is null)
        {
            MessageBox.Show(this, S.SaveFirst, S.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        UseWaitCursor = true;
        _autoApplyToggle.Enabled = false;
        try
        {
            var result = TaskInstaller.RunElevated(disabling ? "uninstall" : "install");
            if (result is null)
                SetStatus(S.StatusCancelled);
            else if (result.Value.ExitCode != 0)
                ShowError(string.Format(S.OperationFailedCode, result.Value.ExitCode, result.Value.Output).TrimEnd());
            else
                SetStatus(disabling ? S.StatusAutoApplyDisabled : string.Format(S.StatusAutoApplyEnabled, TaskInstaller.InstallDirectory));
        }
        catch (Exception ex)
        {
            ShowError(string.Format(S.OperationFailed, ex.Message));
        }
        finally
        {
            UseWaitCursor = false;
            _autoApplyToggle.Enabled = true;
            RefreshAutoApplyState();
        }
    }

    // ---- Release ----

    private void ReleaseControl()
    {
        if (MessageBox.Show(this, S.ReleaseConfirm, S.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        _applyTimer.Stop();
        var hadOriginal = LightSettings.LoadOriginal() is not null;
        var closeAfter = TaskInstaller.IsRunningFromInstallDirectory;
        UseWaitCursor = true;
        _release.Enabled = false;
        try
        {
            bool rebootPending;
            if (TaskInstaller.ReleaseRequiresElevation())
            {
                var result = TaskInstaller.RunElevated("release");
                if (result is null)
                {
                    SetStatus(S.StatusCancelled);
                    return;
                }
                if (result.Value.ExitCode is not (0 or Cli.ExitRebootPending))
                {
                    ShowError(string.Format(S.OperationFailedCode, result.Value.ExitCode, result.Value.Output).TrimEnd());
                    return;
                }
                rebootPending = result.Value.ExitCode == Cli.ExitRebootPending;
            }
            else
            {
                rebootPending = TaskInstaller.Release();
            }

            var message = hadOriginal ? S.ReleasedRestored : S.ReleasedKeptCurrent;
            if (rebootPending)
                message += "\n\n" + S.ReleaseRebootPending;
            if (closeAfter)
            {
                MessageBox.Show(this, message + "\n\n" + S.ReleaseClosing, S.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _skipClosePrompt = true;
                Close();
                return;
            }
            MessageBox.Show(this, message, S.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Initialize();
            SetStatus(message);
        }
        catch (Exception ex)
        {
            ShowError(string.Format(S.OperationFailed, ex.Message));
        }
        finally
        {
            UseWaitCursor = false;
            _release.Enabled = true;
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_skipClosePrompt || !IsDirty)
            return;

        var answer = MessageBox.Show(this, S.UnsavedChangesPrompt, S.AppTitle,
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        switch (answer)
        {
            case DialogResult.Yes:
                Save();
                break;
            case DialogResult.No:
                // Put the keyboard back to what it showed when the window opened.
                var baseline = LightSettings.Load();
                if (baseline is null && _initialSettings is not null)
                    baseline = _initialSettings;
                if (baseline is not null)
                    ApplyToDevice(baseline);
                break;
            default:
                e.Cancel = true;
                break;
        }
    }

    private static bool IsMsiCenterRunning()
    {
        foreach (var name in new[] { "LEDKeeper2", "Mystic_Light_Service", "LightKeeperService" })
        {
            var processes = Process.GetProcessesByName(name);
            var found = processes.Length > 0;
            foreach (var p in processes)
                p.Dispose();
            if (found)
                return true;
        }
        return false;
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, S.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);

    private void SetStatus(string message) => _status.Text = message;
}
