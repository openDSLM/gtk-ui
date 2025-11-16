using Gtk;

public static class StyleInstaller
{
    public static void TryInstall()
    {
        try
        {
            var css = CssProvider.New();
            string cssStr = @"
@define-color panel_bg shade(@window_bg_color, 0.92);
@define-color panel_border alpha(@borders, 0.7);
@define-color control_bg shade(@window_bg_color, 0.98);
@define-color control_border @borders;

window.camera-window {
  background-color: #040404;
  color: #f6f6f6;
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
  background-color: rgba(0, 0, 0, 0.35);
}

overlay label.hud-readout {
  background-color: rgba(10, 10, 10, 0.7);
  color: #f8f8f8;
  padding: 12px 16px;
  border-radius: 8px;
  box-shadow: 0 6px 24px rgba(0,0,0,0.65);
  border: 1px solid rgba(255,255,255,0.08);
}

.bottom-control-bar {
  background-color: rgba(8, 8, 8, 0.85);
  border-radius: 14px;
  padding: 16px 24px;
  box-shadow: 0 22px 42px rgba(0,0,0,0.55);
  border: 1px solid rgba(255,255,255,0.08);
}

.photo-controls-row {
  color: #fff;
}

.photo-controls-row .inline-control {
  background-color: rgba(255,255,255,0.05);
  border-radius: 10px;
  padding: 6px 12px;
  border: 1px solid rgba(255,255,255,0.08);
}

.photo-controls-row .inline-control .inline-label {
  font-weight: 600;
  letter-spacing: 0.5px;
  color: #fff;
}

.photo-controls-row .control-input,
.photo-controls-row .control-toggle {
  background-color: rgba(20, 20, 20, 0.85);
  border: 1px solid rgba(255,255,255,0.2);
  border-radius: 8px;
  color: #f4f4f4;
  padding: 4px 10px;
  min-height: 30px;
}

.photo-controls-row .control-input button,
.photo-controls-row .control-input entry {
  min-height: 0;
  padding: 0 6px;
  color: #f8f8f8;
}

.photo-controls-row .control-input popover,
.photo-controls-row .control-input listview row label {
  background-color: rgba(18,18,18,0.96);
  color: #f6f6f6;
  border-radius: 0;
}

.photo-controls-row .control-button.capture-button {
  background-color: #f6d365;
  color: #131313;
  border-radius: 12px;
  padding: 12px 28px;
  font-weight: 700;
  letter-spacing: 1px;
  border: none;
  text-transform: uppercase;
}

.side-button-column {
  margin-top: 8px;
  margin-bottom: 8px;
  row-gap: 12px;
}

.side-button {
  border-radius: 10px;
  font-weight: 600;
  letter-spacing: 1px;
  background-color: rgba(8,8,8,0.9);
  color: #fff;
  border: 1px solid rgba(255,255,255,0.15);
  min-width: 64px;
  min-height: 64px;
  padding: 16px;
  box-shadow: 0 10px 28px rgba(0,0,0,0.5);
}

.side-button image {
  color: #fff;
}

.back-button {
  background-color: rgba(10, 10, 10, 0.85);
  color: #fefefe;
  border-radius: 8px;
  border: 1px solid rgba(255,255,255,0.4);
  padding: 6px 14px;
  box-shadow: 0 6px 14px rgba(0,0,0,0.4);
}

.back-button:hover {
  background-color: rgba(30,30,30,0.9);
}

.icon-image {
  min-width: 28px;
  min-height: 28px;
  color: #fff;
}

.capture-label {
  font-weight: 700;
  letter-spacing: 0.8px;
  text-transform: uppercase;
}

.settings-sidebar-frame {
  background-color: shade(@window_bg_color, 1.08);
  border-radius: 0;
  padding: 0;
  border: none;
  min-width: 220px;
}

.settings-sidebar-frame stacksidebar {
  min-width: 220px;
  padding: 18px 12px;
  background-color: transparent;
  border-right: 1px solid @panel_border;
}

.settings-sidebar-frame stacksidebar row {
  border-radius: 4px;
  padding: 10px 12px;
  font-weight: 600;
}

.settings-sidebar-frame stacksidebar row:selected {
  background-color: shade(@window_bg_color, 0.8);
  color: @window_fg_color;
  border-left: 3px solid @theme_selected_bg_color;
  border-radius: 4px;
}

box.settings-page {
  background-color: shade(@window_bg_color, 1.04);
  border-radius: 0;
  box-shadow: none;
  color: @window_fg_color;
  border: none;
  padding: 32px 48px;
}

.settings-content {
  column-gap: 32px;
  row-gap: 0;
}

.settings-content-panel {
  background-color: shade(@window_bg_color, 1.1);
  border-radius: 8px;
  border: 1px solid @panel_border;
  padding: 24px;
}

box.settings-page label {
  color: @window_fg_color;
}

label.settings-title {
  font-weight: 700;
  font-size: 18px;
  letter-spacing: 0.2px;
}

.settings-page button.control-button,
box.settings-page button.control-button {
  color: @window_fg_color;
  background-color: @control_bg;
  border: 1px solid @control_border;
  border-radius: 8px;
  padding: 6px 14px;
  min-height: 32px;
  box-shadow: none;
}

label.settings-section-label {
  font-weight: 600;
  letter-spacing: 0.2px;
  margin-bottom: 2px;
  text-transform: uppercase;
  font-size: 12px;
}

.settings-page .settings-input,
box.settings-page .settings-input {
  background-color: shade(@window_bg_color, 0.94);
  color: @window_fg_color;
  border-radius: 6px;
  border: 1px solid @control_border;
  padding: 6px 10px;
  min-height: 32px;
}

box.settings-page entry.settings-input {
  min-width: 240px;
}

.gallery-page {
  background-color: shade(@window_bg_color, 1.02);
  padding: 0;
  row-gap: 12px;
}

.gallery-header {
  padding: 20px 32px 10px 32px;
  border-bottom: 1px solid @panel_border;
  column-gap: 12px;
  align-items: center;
}

.gallery-rows-control {
  margin: 0;
  padding: 6px 12px;
  border-radius: 8px;
  border: 1px solid @panel_border;
  background-color: shade(@window_bg_color, 1.06);
  color: @window_fg_color;
  column-gap: 12px;
  align-items: center;
}

.gallery-rows-control button {
  min-width: 32px;
}

.gallery-rows-label {
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.15em;
  font-size: 11px;
}

.gallery-rows-value {
  font-weight: 700;
  min-width: 32px;
  text-align: center;
  color: @window_fg_color;
}

.gallery-content-panel {
  background-color: shade(@window_bg_color, 1.08);
  border-radius: 8px;
  border: 1px solid @panel_border;
  padding: 16px;
  margin: 0 32px;
}

.gallery-footer {
  margin-top: 0;
  padding: 16px 32px 24px 32px;
  border-top: 1px solid @panel_border;
  column-gap: 18px;
  align-items: center;
}

.gallery-footer button {
  background-color: rgba(10,10,10,0.8);
  color: #fefefe;
  border-radius: 6px;
  border: 1px solid rgba(255,255,255,0.15);
  padding: 6px 16px;
  font-weight: 600;
}

.gallery-viewer-overlay {
  background-color: #000;
}

.gallery-viewer-top-bar {
  background-color: rgba(10,10,10,0.65);
  border-radius: 8px;
  padding: 6px 10px;
}

.gallery-full-picture {
  min-width: 0;
  min-height: 0;
}

.gallery-viewer-back-button {
  background-color: rgba(12,12,12,0.75);
  border: 1px solid rgba(255,255,255,0.2);
  border-radius: 6px;
  color: #fff;
  padding: 6px 12px;
  box-shadow: 0 10px 22px rgba(0,0,0,0.45);
}

.gallery-full-info {
  background-color: rgba(10,10,10,0.65);
  border-radius: 8px;
  padding: 10px 16px;
  color: #fff;
}

.gallery-full-label {
  color: inherit;
  font-weight: 600;
}

label.gallery-title {
  font-weight: 700;
  font-size: 22px;
  letter-spacing: 0.4px;
  color: #fefefe;
}

flowboxchild.gallery-thumb {
  background-color: transparent;
  border-radius: 0;
  padding: 0;
  box-shadow: none;
  min-width: 0;
  border: none;
  margin: 0;
}

box.gallery-thumb-body {
  padding: 0;
  margin: 0;
  min-width: 0;
}

flowboxchild.gallery-thumb picture.gallery-thumb-picture {
  background-color: transparent;
  border-radius: 0;
  min-width: 0;
  min-height: 0;
  border: none;
}

flowboxchild.gallery-thumb picture.gallery-thumb-picture-missing {
  background-color: rgba(64, 64, 64, 0.6);
  border: none;
}

.gallery-page-label {
  color: @window_fg_color;
  font-weight: 600;
  letter-spacing: 0.5px;
  min-width: 120px;
  text-align: center;
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
