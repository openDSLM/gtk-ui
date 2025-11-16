using Gtk;

public static class StyleInstaller
{
    public static void TryInstall()
    {
        try
        {
            var css = CssProvider.New();
            string cssStr = @"
window.camera-window {
  background-color: @window_bg_color;
  color: @window_fg_color;
}

overlay.live-view-root picture.live-view-picture {
  background-color: #000;
}

overlay label.hud-readout {
  background-color: rgba(0, 0, 0, 0.65);
  color: @window_fg_color;
  border-radius: 12px;
  padding: 10px 14px;
  font-weight: 600;
}

overlay .osd {
  background-color: rgba(20, 20, 20, 0.82);
  border-radius: 24px;
  padding: 18px 24px;
  box-shadow: 0 18px 48px rgba(0, 0, 0, 0.35);
}

.dim-label {
  opacity: 0.72;
}
";
            css.LoadFromData(cssStr, (nint)cssStr.Length);
            var display = Gdk.Display.GetDefault();
            if (display is not null)
            {
                StyleContext.AddProviderForDisplay(display, css, 800);
            }
        }
        catch
        {
        }
    }
}
