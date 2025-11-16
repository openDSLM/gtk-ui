using Gtk;

public static class StyleInstaller
{
    public static void TryInstall()
    {
        try
        {
            var css = CssProvider.New();
            string cssStr = @"
@define-color panel_bg shade(@window_bg_color, 0.96);
@define-color panel_border @borders;
@define-color control_bg shade(@window_bg_color, 1.02);
@define-color control_border @borders;

window.camera-window {
  background-color: #000000;
  color: #fefefe;
}

overlay.live-view-root {
  padding: 0;
  background-color: transparent;
}

overlay.live-view-root picture.live-view-picture {
  background-color: transparent;
  border: none;
  border-radius: 0;
  box-shadow: none;
}

overlay.live-view-root .live-view-scrim {
  background-image: radial-gradient(circle at center, rgba(0,0,0,0.35), transparent 70%);
}

overlay label.hud-readout {
  background-color: rgba(0, 0, 0, 0.55);
  color: #fefefe;
  padding: 12px 16px;
  border-radius: 12px;
  box-shadow: 0 6px 22px rgba(0,0,0,0.5);
}

.bottom-control-bar {
  background-color: rgba(0, 0, 0, 0.65);
  border-radius: 999px;
  padding: 14px 24px;
  box-shadow: 0 18px 44px rgba(0,0,0,0.55);
}

.photo-controls-row {
  color: #fefefe;
}

.photo-controls-row .inline-control {
  background-color: rgba(255,255,255,0.08);
  border-radius: 28px;
  padding: 6px 12px;
}

.photo-controls-row .inline-control .inline-label {
  font-weight: 600;
  letter-spacing: 0.6px;
  color: #fefefe;
}

.photo-controls-row .control-input,
.photo-controls-row .control-toggle {
  background-color: rgba(25, 25, 25, 0.75);
  border: 1px solid rgba(255,255,255,0.25);
  border-radius: 999px;
  color: #fefefe;
  padding: 4px 10px;
  min-height: 32px;
}

.photo-controls-row .control-input button,
.photo-controls-row .control-input entry {
  min-height: 0;
  padding: 0 6px;
  color: #fefefe;
}

.photo-controls-row .control-input popover,
.photo-controls-row .control-input listview row label {
  background-color: rgba(20,20,20,0.95);
  color: #fefefe;
}

.photo-controls-row .control-button.capture-button {
  background-color: @theme_selected_bg_color;
  color: #000000;
  border-radius: 999px;
  padding: 10px 28px;
  font-weight: 700;
  letter-spacing: 1px;
  border: none;
}

.side-button-column {
  margin-top: 24px;
  margin-bottom: 24px;
}

.side-button {
  border-radius: 18px;
  font-weight: 700;
  letter-spacing: 2px;
  background-color: rgba(0,0,0,0.65);
  color: #fefefe;
  border: 1px solid rgba(255,255,255,0.2);
  min-width: 56px;
  min-height: 56px;
  padding: 16px;
}

.icon-image {
  min-width: 24px;
  min-height: 24px;
}

.capture-label {
  font-weight: 700;
  letter-spacing: 1px;
}

box.settings-page {
  background-color: rgba(0,0,0,0.85);
  border-radius: 32px;
  box-shadow: 0 22px 48px rgba(0,0,0,0.5);
  color: #fefefe;
  border: 1px solid rgba(255,255,255,0.12);
  padding: 32px;
}

box.settings-page label {
  color: #fefefe;
}

label.settings-title {
  font-weight: 700;
  font-size: 20px;
  letter-spacing: 0.4px;
}

.settings-page button.control-button,
box.settings-page button.control-button {
  color: #000000;
  background-color: #fefefe;
  border: none;
  border-radius: 999px;
  padding: 6px 18px;
  min-height: 36px;
  box-shadow: none;
}

label.settings-section-label {
  font-weight: 600;
  letter-spacing: 0.4px;
  margin-bottom: 4px;
}

.settings-page .settings-input,
box.settings-page .settings-input {
  background-color: rgba(20,20,20,0.9);
  color: #fefefe;
  border-radius: 14px;
  border: 1px solid rgba(255,255,255,0.15);
  padding: 6px 10px;
  min-height: 36px;
}

box.settings-page entry.settings-input {
  min-width: 280px;
}

label.gallery-title {
  font-weight: 700;
  font-size: 24px;
  letter-spacing: 0.5px;
  color: #fefefe;
}

flowboxchild.gallery-thumb {
  background-color: rgba(0,0,0,0.7);
  border-radius: 20px;
  padding: 12px;
  box-shadow: 0 10px 32px rgba(0,0,0,0.55);
  min-width: 240px;
  border: 1px solid rgba(255,255,255,0.15);
}

flowboxchild.gallery-thumb picture.gallery-thumb-picture {
  background-color: rgba(0, 0, 0, 0.35);
  border-radius: 14px;
  min-width: 240px;
  min-height: 160px;
}

flowboxchild.gallery-thumb picture.gallery-thumb-picture-missing {
  background-color: rgba(64, 64, 64, 0.5);
  min-width: 240px;
  min-height: 160px;
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
