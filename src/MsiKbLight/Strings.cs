using System.Globalization;

namespace MsiKbLight;

internal enum UiLanguage
{
    Auto,
    Japanese,
    English,
}

/// <summary>
/// All user-facing GUI text. Every property is <c>required</c>, so a language that misses a string does not compile.
/// The CLI stays English-only.
/// </summary>
internal sealed class Strings
{
    public static readonly Strings Japanese = new()
    {
        FontFamily = "Yu Gothic UI",
        AppTitle = "MSI キーボード ライティング",

        ModeOff = "消灯",
        ModeStatic = "点灯（単色）",
        ModeBreathing = "呼吸",
        ModeFlashing = "点滅",
        ModeDoubleFlashing = "2 回点滅",
        ModeColorShift = "カラーシフト",
        ModeRainbow = "レインボー",
        SpeedSlow = "遅い",
        SpeedNormal = "普通",
        SpeedFast = "速い",

        LightingSection = "ライティング",
        ModeLabel = "モード",
        ColorLabel = "色",
        ColorCountLabel = "色数",
        BrightnessLabel = "明るさ",
        SpeedLabel = "速度",
        Save = "保存",
        Revert = "元に戻す",

        AutoApplySection = "再起動・スリープ復帰時の自動適用",
        AutoApplyEnabled = "状態: 有効",
        AutoApplyDisabled = "状態: 無効",
        AutoApplyNeedsRepair = "状態: 要修復（登録先のファイルが見つかりません）",
        AutoApplyOutdated = "状態: 有効（インストール済みのバージョンが古い）",
        Enable = "有効にする",
        Disable = "無効にする",
        Repair = "修復する",
        Update = "更新する",

        DisplaySection = "表示",
        ThemeLabel = "テーマ",
        ThemeSystem = "システム設定に従う",
        ThemeLight = "ライト",
        ThemeDark = "ダーク",
        LanguageLabel = "言語 / Language",
        LanguageAuto = "自動（Windows に合わせる）",

        ReleaseSection = "ライティング制御の解除",
        ReleaseDescription = "自動適用タスク・スタートメニュー登録・保存済み設定・インストール先のファイルを削除し、" +
            "ライティングを本ツール使用前の状態に戻します。MSI Center などに制御を戻すときに使います。",
        ReleaseButton = "解除…",
        ReleaseConfirm = "ライティング制御を解除しますか？\n\n" +
            "・自動適用タスクとスタートメニュー登録を削除\n" +
            "・保存済みの設定を削除\n" +
            "・インストール先（Program Files）のファイルを削除\n" +
            "・ライティングを本ツール使用前の状態に戻す（記録がある場合）",
        ReleasedRestored = "ライティング制御を解除し、本ツール使用前のライティングに戻しました。",
        ReleasedKeptCurrent = "ライティング制御を解除しました。使用前の状態の記録がないため、ライティングは現在のままです。",
        ReleaseRebootPending = "使用中だった一部のファイルは、アプリの終了後または次回の再起動時に削除されます。",
        ReleaseClosing = "アプリを終了します。",

        DeviceFound = "デバイス: MysticLight MS-1562（PID 0x{0:X4}）",
        DeviceNotFound = "デバイス: 未検出（MysticLight MS-1562 が見つかりません）",
        MsiCenterWarning = "⚠ MSI Center が動作中です。設定が上書きされる場合があります。",

        StatusLoadFailed = "保存済み設定を読み込めませんでした: {0}",
        StatusReadFailed = "現在の設定を読み出せませんでした: {0}",
        StatusNoSaved = "保存済みの設定はありません。",
        StatusLoadedSaved = "保存済みの設定を読み込みました。",
        StatusPreview = "プレビュー中（未保存）",
        StatusApplied = "適用しました。",
        StatusDeviceMissing = "デバイスが見つかりません。",
        StatusApplyFailed = "適用に失敗しました: {0}",
        SaveFailed = "保存できませんでした。\n\n{0}",
        StatusSavedPersistent = "保存しました。再起動後も維持されます。",
        StatusSavedNeedsAutoApply = "保存しました。再起動後も維持するには自動適用を有効にしてください。",
        SaveFirst = "先に「保存」で設定を保存してください。",
        StatusCancelled = "キャンセルされました。",
        OperationFailedCode = "処理に失敗しました（終了コード {0}）。\n\n{1}",
        OperationFailed = "処理に失敗しました。\n\n{0}",
        StatusAutoApplyDisabled = "自動適用を無効にしました。",
        StatusAutoApplyEnabled = "自動適用を有効にしました（{0}、スタートメニューに登録）。",
        UnsavedChangesPrompt = "変更が保存されていません。保存しますか？",
        ShortcutDescription = "MSI キーボード ライティングの設定",
    };

    public static readonly Strings English = new()
    {
        FontFamily = "Segoe UI",
        AppTitle = "MSI Keyboard Lighting",

        ModeOff = "Off",
        ModeStatic = "Static",
        ModeBreathing = "Breathing",
        ModeFlashing = "Flashing",
        ModeDoubleFlashing = "Double flashing",
        ModeColorShift = "Color shift",
        ModeRainbow = "Rainbow",
        SpeedSlow = "Slow",
        SpeedNormal = "Normal",
        SpeedFast = "Fast",

        LightingSection = "Lighting",
        ModeLabel = "Mode",
        ColorLabel = "Color",
        ColorCountLabel = "Colors",
        BrightnessLabel = "Brightness",
        SpeedLabel = "Speed",
        Save = "Save",
        Revert = "Revert",

        AutoApplySection = "Re-apply at startup and after sleep",
        AutoApplyEnabled = "Status: On",
        AutoApplyDisabled = "Status: Off",
        AutoApplyNeedsRepair = "Status: Needs repair (registered files are missing)",
        AutoApplyOutdated = "Status: On (installed version is outdated)",
        Enable = "Turn on",
        Disable = "Turn off",
        Repair = "Repair",
        Update = "Update",

        DisplaySection = "Appearance",
        ThemeLabel = "Theme",
        ThemeSystem = "Use system setting",
        ThemeLight = "Light",
        ThemeDark = "Dark",
        LanguageLabel = "Language / 言語",
        LanguageAuto = "Automatic (match Windows)",

        ReleaseSection = "Release lighting control",
        ReleaseDescription = "Removes the auto-apply task, Start menu shortcut, saved settings and installed files, " +
            "and restores the lighting to how it was before this tool was first used. Use this to hand control back to MSI Center.",
        ReleaseButton = "Release…",
        ReleaseConfirm = "Release lighting control?\n\n" +
            "• Remove the auto-apply task and the Start menu shortcut\n" +
            "• Delete the saved settings\n" +
            "• Delete the installed files (Program Files)\n" +
            "• Restore the lighting from before this tool was first used (if recorded)",
        ReleasedRestored = "Lighting control has been released and the original lighting restored.",
        ReleasedKeptCurrent = "Lighting control has been released. The original lighting was not recorded, so the current lighting is kept.",
        ReleaseRebootPending = "Some files that were in use will be deleted after the app closes or at the next restart.",
        ReleaseClosing = "The app will now close.",

        DeviceFound = "Device: MysticLight MS-1562 (PID 0x{0:X4})",
        DeviceNotFound = "Device: not found (no MysticLight MS-1562 controller)",
        MsiCenterWarning = "⚠ MSI Center is running and may overwrite these settings.",

        StatusLoadFailed = "Could not load the saved settings: {0}",
        StatusReadFailed = "Could not read the current settings: {0}",
        StatusNoSaved = "No saved settings.",
        StatusLoadedSaved = "Loaded the saved settings.",
        StatusPreview = "Previewing (not saved)",
        StatusApplied = "Applied.",
        StatusDeviceMissing = "Device not found.",
        StatusApplyFailed = "Could not apply: {0}",
        SaveFailed = "Could not save.\n\n{0}",
        StatusSavedPersistent = "Saved. It will be kept after restarting.",
        StatusSavedNeedsAutoApply = "Saved. Turn on auto-apply to keep it after restarting.",
        SaveFirst = "Save the settings first.",
        StatusCancelled = "Cancelled.",
        OperationFailedCode = "The operation failed (exit code {0}).\n\n{1}",
        OperationFailed = "The operation failed.\n\n{0}",
        StatusAutoApplyDisabled = "Auto-apply turned off.",
        StatusAutoApplyEnabled = "Auto-apply turned on ({0}, added to the Start menu).",
        UnsavedChangesPrompt = "You have unsaved changes. Save them?",
        ShortcutDescription = "MSI keyboard lighting settings",
    };

    /// <summary>Every shortcut name any language has used, so uninstall removes them all.</summary>
    public static readonly string[] AllAppTitles = [Japanese.AppTitle, English.AppTitle];

    public static Strings Current { get; private set; } = Resolve(UiPreferences.Load().Language);

    public static void Use(UiLanguage language) => Current = Resolve(language);

    public static Strings Resolve(UiLanguage language) => language switch
    {
        UiLanguage.Japanese => Japanese,
        UiLanguage.English => English,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? Japanese : English,
    };

    public string ModeName(LightMode mode) => mode switch
    {
        LightMode.Off => ModeOff,
        LightMode.Static => ModeStatic,
        LightMode.Breathing => ModeBreathing,
        LightMode.Flashing => ModeFlashing,
        LightMode.DoubleFlashing => ModeDoubleFlashing,
        LightMode.ColorShift => ModeColorShift,
        LightMode.Rainbow => ModeRainbow,
        _ => mode.ToString(),
    };

    public required string FontFamily { get; init; }
    public required string AppTitle { get; init; }

    public required string ModeOff { get; init; }
    public required string ModeStatic { get; init; }
    public required string ModeBreathing { get; init; }
    public required string ModeFlashing { get; init; }
    public required string ModeDoubleFlashing { get; init; }
    public required string ModeColorShift { get; init; }
    public required string ModeRainbow { get; init; }
    public required string SpeedSlow { get; init; }
    public required string SpeedNormal { get; init; }
    public required string SpeedFast { get; init; }

    public required string LightingSection { get; init; }
    public required string ModeLabel { get; init; }
    public required string ColorLabel { get; init; }
    public required string ColorCountLabel { get; init; }
    public required string BrightnessLabel { get; init; }
    public required string SpeedLabel { get; init; }
    public required string Save { get; init; }
    public required string Revert { get; init; }

    public required string AutoApplySection { get; init; }
    public required string AutoApplyEnabled { get; init; }
    public required string AutoApplyDisabled { get; init; }
    public required string AutoApplyNeedsRepair { get; init; }
    public required string AutoApplyOutdated { get; init; }
    public required string Enable { get; init; }
    public required string Disable { get; init; }
    public required string Repair { get; init; }
    public required string Update { get; init; }

    public required string DisplaySection { get; init; }
    public required string ThemeLabel { get; init; }
    public required string ThemeSystem { get; init; }
    public required string ThemeLight { get; init; }
    public required string ThemeDark { get; init; }
    public required string LanguageLabel { get; init; }
    public required string LanguageAuto { get; init; }

    public required string ReleaseSection { get; init; }
    public required string ReleaseDescription { get; init; }
    public required string ReleaseButton { get; init; }
    public required string ReleaseConfirm { get; init; }
    public required string ReleasedRestored { get; init; }
    public required string ReleasedKeptCurrent { get; init; }
    public required string ReleaseRebootPending { get; init; }
    public required string ReleaseClosing { get; init; }

    public required string DeviceFound { get; init; }
    public required string DeviceNotFound { get; init; }
    public required string MsiCenterWarning { get; init; }

    public required string StatusLoadFailed { get; init; }
    public required string StatusReadFailed { get; init; }
    public required string StatusNoSaved { get; init; }
    public required string StatusLoadedSaved { get; init; }
    public required string StatusPreview { get; init; }
    public required string StatusApplied { get; init; }
    public required string StatusDeviceMissing { get; init; }
    public required string StatusApplyFailed { get; init; }
    public required string SaveFailed { get; init; }
    public required string StatusSavedPersistent { get; init; }
    public required string StatusSavedNeedsAutoApply { get; init; }
    public required string SaveFirst { get; init; }
    public required string StatusCancelled { get; init; }
    public required string OperationFailedCode { get; init; }
    public required string OperationFailed { get; init; }
    public required string StatusAutoApplyDisabled { get; init; }
    public required string StatusAutoApplyEnabled { get; init; }
    public required string UnsavedChangesPrompt { get; init; }
    public required string ShortcutDescription { get; init; }
}
