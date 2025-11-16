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
}

overlay.live-view-root {
  padding: 0;
  background-color: transparent;
}

overlay.live-view-root picture.live-view-picture {
  background-color: transparent;
  border-radius: 0;
  border: none;
  box-shadow: none;
}

overlay.live-view-root .live-view-scrim {
  background-image: radial-gradient(circle at center, rgba(0,0,0,0.35), transparent 70%);
  border-radius: 0;
}

overlay > box.control-panel {
  background-color: @panel_bg;
  border-radius: 28px;
  padding: 20px;
  border: 1px solid @panel_border;
  box-shadow: 0 18px 44px rgba(0, 0, 0, 0.35);
  color: @window_fg_color;
}

overlay > box#mode_overlay {
  background-color: @panel_bg;
  border-radius: 999px;
  padding: 8px 14px;
  border: 1px solid @panel_border;
  box-shadow: 0 12px 32px rgba(0,0,0,0.32);
}

overlay > box#mode_overlay .control-button {
  margin-left: 4px;
  margin-right: 4px;
}

overlay > box.control-panel label.control-section-label {
  color: @window_fg_color;
  font-weight: 600;
  margin-top: 10px;
  margin-bottom: 4px;
  letter-spacing: 0.3px;
}

overlay > box.control-panel label.control-inline-label {
  color: @window_fg_color;
  font-weight: 600;
  margin-right: 10px;
}

overlay > box.control-panel .control-input,
overlay > box.control-panel .control-toggle,
overlay > box.control-panel .control-button {
  background-color: @control_bg;
  color: @window_fg_color;
  border-radius: 16px;
  padding: 4px 12px;
  border: 1px solid @control_border;
  min-height: 36px;
  margin: 0;
  box-shadow: none;
}

overlay > box.control-panel scale.control-input trough {
  background-color: rgba(28, 28, 28, 0.45);
  border-radius: 999px;
}

overlay > box.control-panel scale.control-input highlight {
  background-color: @theme_selected_bg_color;
  border-radius: 999px;
}

overlay > box.control-panel combobox box,
overlay > box.control-panel combobox button,
overlay > box.control-panel combobox entry {
  padding: 0;
  min-height: inherit;
}

overlay > box.control-panel combobox popover {
  background-color: @control_bg;
  color: @window_fg_color;
  border-radius: 14px;
  padding: 8px;
}

overlay label.hud-readout {
  background-color: rgba(0, 0, 0, 0.55);
  color: #fefefe;
  padding: 12px 16px;
  border-radius: 12px;
  box-shadow: 0 6px 22px rgba(0,0,0,0.5);
}

box.settings-page {
  background-color: color-mix(in srgb, @window_bg_color 97%, transparent);
  border-radius: 32px;
  box-shadow: 0 22px 48px rgba(0,0,0,0.3);
  color: @window_fg_color;
  border: 1px solid @panel_border;
  padding: 32px;
}

box.settings-page label {
  color: @window_fg_color;
}

label.settings-title {
  font-weight: 700;
  font-size: 20px;
  letter-spacing: 0.4px;
}

.settings-page button.control-button,
box.settings-page button.control-button {
  color: @window_fg_color;
  background-color: @control_bg;
  border: 1px solid @control_border;
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
  background-color: @control_bg;
  color: @window_fg_color;
  border-radius: 14px;
  border: 1px solid @control_border;
  padding: 6px 10px;
  min-height: 36px;
}

box.settings-page entry.settings-input {
  min-width: 280px;
}

.text-black {
  color: @window_fg_color;
}

label.gallery-title {
  font-weight: 700;
  font-size: 24px;
  letter-spacing: 0.5px;
  color: @window_fg_color;
}

flowboxchild.gallery-thumb {
  background-color: @panel_bg;
  border-radius: 20px;
  padding: 12px;
  box-shadow: 0 10px 32px rgba(0,0,0,0.45);
  min-width: 240px;
  border: 1px solid @panel_border;
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

.gallery-caption {
  font-weight: 600;
  color: @window_fg_color;
}

.gallery-error {
  color: @error_color;
  font-size: 12px;
}

label.gallery-page-label {
  color: @window_fg_color;
  font-weight: 600;
}

label.gallery-settings-hint {
  color: color-mix(in srgb, @window_fg_color 70%, transparent);
  font-size: 12px;
}

stackswitcher.settings-tab-switcher {
  margin-bottom: 12px;
}

stackswitcher.settings-tab-switcher button {
  border-radius: 999px;
  padding: 4px 16px;
  border: 1px solid transparent;
  color: @window_fg_color;
}

stackswitcher.settings-tab-switcher button:checked {
  background-color: @control_bg;
  border-color: @control_border;
}

stackswitcher.settings-tab-switcher button:checked label {
  color: @window_fg_color;
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
