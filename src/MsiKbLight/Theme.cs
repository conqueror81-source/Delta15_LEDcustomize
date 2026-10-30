using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MsiKbLight;

internal enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <param name="FullMica">
/// The whole client area shows the Mica backdrop. GDI paints pure black as transparent there and other colors are
/// added on top of the backdrop, so the window background is black and cards are a faint gray layer.
/// </param>
internal sealed record Palette(
    bool IsDark,
    bool FullMica,
    Color Window,
    Color Card,
    Color CardBorder,
    Color Input,
    Color Text,
    Color SubtleText,
    Color Button,
    Color ButtonHover,
    Color ButtonBorder,
    Color Accent,
    Color OnAccent,
    Color Error,
    Color Success);

internal static class Theme
{
    private static readonly Palette Light = new(
        IsDark: false, FullMica: false,
        Window: Rgb(0xF3F3F3), Card: Rgb(0xFBFBFB), CardBorder: Rgb(0xE5E5E5), Input: Rgb(0xFFFFFF),
        Text: Rgb(0x1B1B1B), SubtleText: Rgb(0x5F5F5F),
        Button: Rgb(0xFDFDFD), ButtonHover: Rgb(0xF0F0F0), ButtonBorder: Rgb(0xD1D1D1),
        Accent: Rgb(0x005FB8), OnAccent: Rgb(0xFFFFFF), Error: Rgb(0xC42B1C), Success: Rgb(0x0F7B0F));

    private static readonly Palette Dark = new(
        IsDark: true, FullMica: false,
        Window: Rgb(0x202020), Card: Rgb(0x2B2B2B), CardBorder: Rgb(0x1D1D1D), Input: Rgb(0x2D2D2D),
        Text: Rgb(0xFFFFFF), SubtleText: Rgb(0xC8C8C8),
        Button: Rgb(0x373737), ButtonHover: Rgb(0x3F3F3F), ButtonBorder: Rgb(0x454545),
        Accent: Rgb(0x60CDFF), OnAccent: Rgb(0x000000), Error: Rgb(0xFF99A4), Success: Rgb(0x6CCB5F));

    // Colors here are added to the backdrop, hence darker than their opaque counterparts above.
    private static readonly Palette DarkMica = Dark with
    {
        FullMica = true,
        Window = Color.Black,
        Card = Rgb(0x121212),
        CardBorder = Rgb(0x2A2A2A),
        Button = Rgb(0x262626),
        ButtonHover = Rgb(0x303030),
        ButtonBorder = Rgb(0x3A3A3A),
        Accent = Rgb(0x3FA9DB),
    };

    private static Palette HighContrast => new(
        IsDark: false, FullMica: false,
        Window: SystemColors.Window, Card: SystemColors.Window, CardBorder: SystemColors.WindowText, Input: SystemColors.Window,
        Text: SystemColors.WindowText, SubtleText: SystemColors.WindowText,
        Button: SystemColors.ButtonFace, ButtonHover: SystemColors.Highlight, ButtonBorder: SystemColors.ButtonShadow,
        Accent: SystemColors.ButtonFace, OnAccent: SystemColors.ControlText, Error: SystemColors.WindowText,
        Success: SystemColors.WindowText);

    private static Color Rgb(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    /// <summary>DWMWA_SYSTEMBACKDROP_TYPE (Mica) exists from Windows 11 22H2, build 22621.</summary>
    public static bool IsMicaSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static Palette Resolve(ThemePreference preference)
    {
        if (SystemInformation.HighContrast)
            return HighContrast;
        var dark = preference == ThemePreference.Dark || (preference == ThemePreference.System && SystemPrefersDark());
        if (!dark)
            return Light;
        return IsMicaSupported ? DarkMica : Dark;
    }

    /// <summary>Dark title bar and Mica backdrop. Every call is optional eye candy, so failures are ignored.</summary>
    public static void ApplyFrame(Form form, Palette palette)
    {
        if (!form.IsHandleCreated)
            return;
        var hwnd = form.Handle;
        try
        {
            int dark = palette.IsDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            if (IsMicaSupported)
            {
                // In light mode Mica shows in the title bar only; the client area stays opaque because dark text
                // cannot be drawn with GDI on a transparent surface.
                int backdrop = SystemInformation.HighContrast ? DWMSBT_NONE : DWMSBT_MAINWINDOW;
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            }
            var margins = palette.FullMica ? new MARGINS(-1) : new MARGINS(0);
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // DWM unavailable: plain window.
        }
    }

    /// <summary>Native dark rendering for common controls (combo boxes). Applied again whenever the handle is recreated.</summary>
    public static void ApplyControlTheme(Control control, bool dark)
    {
        if (!control.IsHandleCreated)
            return;
        try
        {
            SetWindowTheme(control.Handle, dark ? "DarkMode_CFD" : null, null);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Keep the default look.
        }
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_MAINWINDOW = 2;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MARGINS(int all)
    {
        public readonly int Left = all;
        public readonly int Right = all;
        public readonly int Top = all;
        public readonly int Bottom = all;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);
}

/// <summary>A section card: a table layout with a 1px border.</summary>
internal sealed class CardPanel : TableLayoutPanel
{
    public Color BorderColor { get; set; } = Color.Gray;

    public CardPanel()
    {
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        ControlPaint.DrawBorder(e.Graphics, ClientRectangle, BorderColor, ButtonBorderStyle.Solid);
    }
}
