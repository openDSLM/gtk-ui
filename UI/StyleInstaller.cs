using Gtk;

public static class StyleInstaller
{
    public static void TryInstall()
    {
        try
        {
            var css = CssProvider.New();
            string cssStr = @"
overlay > box.control-panel {
  background-color: rgba(36, 36, 36, 0.78);
  border-radius: 16px;
  padding: 18px;
  box-shadow: 0 10px 28px rgba(0,0,0,0.45);
}

overlay > box#mode_overlay {
  background-color: rgba(36, 36, 36, 0.78);
  border-radius: 16px;
  padding: 10px 16px;
  box-shadow: 0 10px 28px rgba(0,0,0,0.45);
}

overlay > box#mode_overlay .control-button {
  margin-left: 4px;
  margin-right: 4px;
}

overlay > box.control-panel label.control-section-label {
  color: #f5f5f5;
  font-weight: 600;
  margin-top: 10px;
  margin-bottom: 4px;
  letter-spacing: 0.4px;
}

overlay > box.control-panel label.control-inline-label {
  color: #f0f0f0;
  font-weight: 600;
  margin-right: 10px;
}

overlay > box.control-panel .control-input,
overlay > box.control-panel .control-toggle,
overlay > box.control-panel .control-button {
  background-color: rgba(64, 64, 64, 0.88);
  color: #fefefe;
  border-radius: 12px;
  padding: 6px 12px;
  border: 1px solid rgba(255,255,255,0.12);
  box-shadow: 0 4px 18px rgba(0,0,0,0.45);
}

overlay > box.control-panel scale.control-input trough {
  background-color: rgba(28, 28, 28, 0.65);
  border-radius: 999px;
}

overlay > box.control-panel scale.control-input highlight {
  background-color: #f1b733;
  border-radius: 999px;
}

overlay label.hud-readout {
  background-color: rgba(0, 0, 0, 0.55);
  color: #fefefe;
  padding: 12px 16px;
  border-radius: 12px;
  box-shadow: 0 6px 22px rgba(0,0,0,0.5);
}

/* Overrides: black text for dropdowns and Capture DNG button */
overlay > box.control-panel .control-input,
overlay > box.control-panel .control-button {
  color: #000000;
  background-color: rgba(255,255,255,0.92);
  border: 1px solid rgba(0,0,0,0.12);
  box-shadow: 0 4px 18px rgba(0,0,0,0.18);
}
overlay > box.control-panel combobox popover,
overlay > box.control-panel combobox popover label {
  color: #000000;
}
overlay > box.control-panel combobox popover {
  background-color: rgba(255,255,255,0.98);
}

box.settings-page {
  background-color: rgba(36, 36, 36, 0.82);
  border-radius: 24px;
  box-shadow: 0 12px 32px rgba(0,0,0,0.42);
  color: #f5f5f5;
}

box.settings-page label {
  color: #f5f5f5;
}

label.settings-title {
  font-weight: 700;
  font-size: 20px;
  letter-spacing: 0.6px;
  color: #f5f5f5;
}

.settings-page button.control-button,
box.settings-page button.control-button {
  color: #000000;
  background-color: rgba(255,255,255,0.92);
  border: 1px solid rgba(0,0,0,0.12);
  box-shadow: 0 4px 18px rgba(0,0,0,0.18);
}

.settings-page button.control-button label,
box.settings-page button.control-button label {
  color: #000000;
}

label.settings-section-label {
  font-weight: 600;
  letter-spacing: 0.4px;
  margin-bottom: 4px;
}

.settings-page .settings-input,
box.settings-page .settings-input {
  background-color: rgba(255,255,255,0.92);
  color: #000000;
  border-radius: 12px;
  border: 1px solid rgba(0,0,0,0.12);
  padding: 6px 12px;
  box-shadow: 0 4px 18px rgba(0,0,0,0.18);
}

box.settings-page entry.settings-input {
  min-width: 280px;
}
.text-black {
  color: #000000;
}

label.gallery-title {
  font-weight: 700;
  font-size: 24px;
  letter-spacing: 0.6px;
  color: #f5f5f5;
}

flowboxchild.gallery-thumb {
  background-color: rgba(36, 36, 36, 0.78);
  border-radius: 18px;
  padding: 12px;
  box-shadow: 0 8px 24px rgba(0,0,0,0.45);
  min-width: 240px;
}

flowboxchild.gallery-thumb picture.gallery-thumb-picture {
  background-color: rgba(0, 0, 0, 0.35);
  border-radius: 12px;
  min-width: 240px;
  min-height: 160px;
}

flowboxchild.gallery-thumb picture.gallery-thumb-picture-missing {
  background-color: rgba(64, 64, 64, 0.65);
  min-width: 240px;
  min-height: 160px;
}

.gallery-caption {
  font-weight: 600;
  color: #f5f5f5;
}

.gallery-error {
  color: #ffb347;
  font-size: 12px;
}

label.gallery-page-label {
  color: #f5f5f5;
  font-weight: 600;
}

label.gallery-settings-hint {
  color: rgba(255,255,255,0.78);
  font-size: 12px;
}

stackswitcher.settings-tab-switcher button,
stackswitcher.settings-tab-switcher button label {
  color: #000000;
  background-color: rgba(255,255,255,0.92);
}

stackswitcher.settings-tab-switcher button:checked,
stackswitcher.settings-tab-switcher button:checked label {
  color: #000000;
  background-color: rgba(238,238,238,0.92);
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
