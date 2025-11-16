using System.IO;
using Gtk;

public static class IconThemeHelper
{
    private static bool _installed;

    public static void EnsureCustomIcons()
    {
        if (_installed)
        {
            return;
        }

        string? baseDir = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(baseDir))
        {
            return;
        }

        string iconsDir = Path.Combine(baseDir, "Resources", "icons");
        if (!Directory.Exists(iconsDir))
        {
            return;
        }

        var display = Gdk.Display.GetDefault();
        if (display is null)
        {
            return;
        }

        var theme = IconTheme.GetForDisplay(display);
        theme.AddSearchPath(iconsDir);

        _installed = true;
    }
}
