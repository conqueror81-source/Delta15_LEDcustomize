using System.Diagnostics;

namespace MsiKbLight;

/// <summary>Settings window. Changes are previewed on the keyboard immediately; 保存 makes them persistent.</summary>
internal sealed class MainForm : Form
{
    // The controller accepts 0-10 but caps anything above 8.
    private const int UiMaxBrightness = 8;

    private static readonly (LightMode Mode, string Label)[] Modes =
    [
        (LightMode.Off, "消灯"),
        (LightMode.Static, "点灯（単色）"),
        (LightMode.Breathing, "呼吸"),
        (LightMode.Flashing, "点滅"),
        (LightMode.DoubleFlashing, "2 回点滅"),
        (LightMode.ColorShift, "カラーシフト"),
        (LightMode.Rainbow, "レインボー"),
    ];

    private static readonly Color[] DefaultShiftColors =
    [
        Color.FromArgb(255, 0, 0), Color.FromArgb(255, 80, 0), Color.FromArgb(255, 255, 0),
        Color.FromArgb(0, 255, 0), Color.FromArgb(0, 0, 255), Color.FromArgb(150, 0, 255),
        Color.FromArgb(255, 0, 255),
    ];

    private readonly Label _deviceLabel = new() { AutoSize = true };
    private readonly Label _msiCenterWarning = new()
    {
        AutoSize = true,
        ForeColor = Color.Firebrick,
        Text = "⚠ MSI Center が動作中です。設定が上書きされる場合があります。",
        Visible = false,
    };
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly NumericUpDown _colorCount = new() { Minimum = 1, Maximum = KeyboardLight.MaxColors, Width = 60 };
    private readonly Label _colorCountLabel = new() { Text = "色数", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly FlowLayoutPanel _colorPanel = new() { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
    private readonly Button[] _colorButtons = new Button[KeyboardLight.MaxColors];
    private readonly TrackBar _brightness = new()
    {
        Minimum = 0, Maximum = UiMaxBrightness, TickStyle = TickStyle.BottomRight, Width = 220, LargeChange = 1,
    };
    private readonly Label _brightnessValue = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly ComboBox _speed = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly Button _save = new() { Text = "保存", AutoSize = true };
    private readonly Button _revert = new() { Text = "元に戻す", AutoSize = true };
    private readonly Label _autoApplyStatus = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _autoApplyToggle = new() { AutoSize = true };
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly System.Windows.Forms.Timer _applyTimer = new() { Interval = 120 };

    private string? _savedSnapshot;
    private string? _baselineSnapshot;
    private LightSettings? _initialSettings;
    private bool _loading;

    public MainForm()
    {
        Text = "MSI キーボード ライティング";
        Font = new Font("Yu Gothic UI", 9.75f);
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

        BuildLayout();

        foreach (var (_, label) in Modes)
            _mode.Items.Add(label);
        _speed.Items.AddRange(["遅い", "普通", "速い"]);

        _mode.SelectedIndexChanged += (_, _) => { UpdateControlStates(); OnSettingChanged(); };
        _colorCount.ValueChanged += (_, _) => { UpdateControlStates(); OnSettingChanged(); };
        _brightness.ValueChanged += (_, _) => { _brightnessValue.Text = $"{_brightness.Value} / {UiMaxBrightness}"; OnSettingChanged(); };
        _speed.SelectedIndexChanged += (_, _) => OnSettingChanged();
        _applyTimer.Tick += (_, _) => { _applyTimer.Stop(); ApplyToDevice(CurrentSettings()); };
        _save.Click += (_, _) => Save();
        _revert.Click += (_, _) => Revert();
        _autoApplyToggle.Click += (_, _) => ToggleAutoApply();

        Load += (_, _) => Initialize();
        FormClosing += OnFormClosing;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(14, 12, 14, 8),
            Dock = DockStyle.Fill,
        };

        var header = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 0, 0, 6) };
        header.Controls.Add(_deviceLabel);
        header.Controls.Add(_msiCenterWarning);
        root.Controls.Add(header);

        var lighting = new GroupBox { Text = "ライティング", AutoSize = true, Padding = new Padding(10, 6, 10, 8), Dock = DockStyle.Fill };
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        void AddRow(string label, Control control)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 12, 8) });
            control.Margin = new Padding(0, 4, 0, 4);
            grid.Controls.Add(control);
        }

        AddRow("モード", _mode);

        var countRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        _colorCountLabel.Margin = new Padding(0, 6, 4, 0);
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
            button.FlatAppearance.BorderColor = SystemColors.ControlDarkDark;
            button.Click += (_, _) => PickColor(index);
            _colorButtons[i] = button;
            _colorPanel.Controls.Add(button);
        }
        AddRow("色", colorBlock);

        var brightnessRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        brightnessRow.Controls.Add(_brightness);
        _brightnessValue.Margin = new Padding(4, 10, 0, 0);
        brightnessRow.Controls.Add(_brightnessValue);
        AddRow("明るさ", brightnessRow);

        AddRow("速度", _speed);

        lighting.Controls.Add(grid);
        root.Controls.Add(lighting);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 8),
        };
        buttons.Controls.Add(_save);
        buttons.Controls.Add(_revert);
        root.Controls.Add(buttons);

        var autoApply = new GroupBox { Text = "再起動・スリープ復帰時の自動適用", AutoSize = true, Padding = new Padding(10, 6, 10, 8), Dock = DockStyle.Fill };
        var autoRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        autoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        autoRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        autoRow.Controls.Add(_autoApplyStatus);
        autoRow.Controls.Add(_autoApplyToggle);
        autoApply.Controls.Add(autoRow);
        root.Controls.Add(autoApply);

        var statusStrip = new StatusStrip { SizingGrip = false };
        statusStrip.Items.Add(_status);

        Controls.Add(root);
        Controls.Add(statusStrip);
        AcceptButton = _save;
    }

    private void Initialize()
    {
        _loading = true;
        try
        {
            LightSettings? settings = null;
            try
            {
                settings = LightSettings.Load();
                _savedSnapshot = settings?.ToString();
            }
            catch (Exception ex)
            {
                SetStatus($"保存済み設定を読み込めませんでした: {ex.Message}");
            }

            using (var light = KeyboardLight.Open())
            {
                if (light is null)
                {
                    _deviceLabel.Text = "デバイス: 未検出（MysticLight MS-1562 が見つかりません）";
                    _deviceLabel.ForeColor = Color.Firebrick;
                }
                else
                {
                    _deviceLabel.Text = $"デバイス: MysticLight MS-1562（PID 0x{light.ProductId:X4}）";
                    if (settings is null)
                    {
                        try
                        {
                            settings = light.Read();
                        }
                        catch (Exception ex)
                        {
                            SetStatus($"現在の設定を読み出せませんでした: {ex.Message}");
                        }
                    }
                }
            }

            _initialSettings = settings;
            ShowSettings(settings ?? new LightSettings());
            _baselineSnapshot = CurrentSettings().ToString();
            UpdateDirtyState();
            _msiCenterWarning.Visible = IsMsiCenterRunning();
            RefreshAutoApplyState();
            if (string.IsNullOrEmpty(_status.Text))
                SetStatus(_savedSnapshot is null ? "保存済みの設定はありません。" : "保存済みの設定を読み込みました。");
        }
        finally
        {
            _loading = false;
        }
    }

    private void ShowSettings(LightSettings settings)
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            _mode.SelectedIndex = Math.Max(0, Array.FindIndex(Modes, m => m.Mode == settings.Mode));
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
            _colorCount.Value = settings.Mode == LightMode.ColorShift
                ? Math.Clamp(colors.Count, 1, KeyboardLight.MaxColors)
                : KeyboardLight.MaxColors;
            UpdateControlStates();
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private LightMode SelectedMode => Modes[Math.Max(0, _mode.SelectedIndex)].Mode;

    private int VisibleColorCount => SelectedMode switch
    {
        LightMode.Off or LightMode.Rainbow => 0,
        LightMode.ColorShift => (int)_colorCount.Value,
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
        Text = dirty ? "MSI キーボード ライティング *" : "MSI キーボード ライティング";
    }

    private bool ApplyToDevice(LightSettings settings)
    {
        try
        {
            using var light = KeyboardLight.Open();
            if (light is null)
            {
                SetStatus("デバイスが見つかりません。");
                return false;
            }
            light.Write(settings);
            SetStatus(IsDirty ? "プレビュー中（未保存）" : "適用しました。");
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"適用に失敗しました: {ex.Message}");
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
            settings.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存できませんでした。\n\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _savedSnapshot = settings.ToString();
        _baselineSnapshot = _savedSnapshot;
        UpdateDirtyState();
        if (ApplyToDevice(settings))
            SetStatus(TaskInstaller.IsRegistered()
                ? "保存しました。再起動後も維持されます。"
                : "保存しました。再起動後も維持するには自動適用を有効にしてください。");
    }

    private void Revert()
    {
        var target = LightSettings.Load() ?? _initialSettings;
        if (target is null)
            return;
        ShowSettings(target);
        ApplyToDevice(target);
    }

    private void RefreshAutoApplyState()
    {
        var registered = TaskInstaller.IsRegistered();
        _autoApplyStatus.Text = registered ? "状態: 有効" : "状態: 無効";
        _autoApplyStatus.ForeColor = registered ? Color.SeaGreen : SystemColors.GrayText;
        _autoApplyToggle.Text = registered ? "無効にする" : "有効にする";
        _autoApplyToggle.Tag = registered;
    }

    private void ToggleAutoApply()
    {
        var registered = _autoApplyToggle.Tag is true;
        if (!registered && _savedSnapshot is null)
        {
            MessageBox.Show(this, "先に「保存」で設定を保存してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        UseWaitCursor = true;
        _autoApplyToggle.Enabled = false;
        try
        {
            var result = TaskInstaller.RunElevated(registered ? "uninstall" : "install");
            if (result is null)
            {
                SetStatus("キャンセルされました。");
            }
            else if (result.Value.ExitCode != 0)
            {
                MessageBox.Show(this, $"処理に失敗しました（終了コード {result.Value.ExitCode}）。\n\n{result.Value.Output}".TrimEnd(),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                SetStatus(registered
                    ? "自動適用を無効にしました。"
                    : $"自動適用を有効にしました（{TaskInstaller.InstallDirectory}、スタートメニューに登録）。");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"処理に失敗しました。\n\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _autoApplyToggle.Enabled = true;
            RefreshAutoApplyState();
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!IsDirty)
            return;

        var answer = MessageBox.Show(this, "変更が保存されていません。保存しますか？", Text,
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

    private void SetStatus(string message) => _status.Text = message;
}
