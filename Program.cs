// .NET 8 + GirCore 0.6.x
// Live preview via gtk4paintablesink + overlay UI:
//  - Auto AE/AGC toggle
//  - DSLR-like shutter dropdown (1/8000 … 30s)
//  - ISO dropdown (100 … 12800)
//  - Zoom (1x..8x) and Pan (X/Y) for focus check (videocrop + videoscale)
//  - "Capture DNG" button
//  - Live HUD from libcamerasrc (t, g), plus ISO≈… only in MANUAL
//  - Last capture HUD (via exiftool JSON if present, or libcamera-still --metadata)
//
// Preview uses NV12 at 1280x720 on the *source side*.
// On the final link to gtk4paintablesink we DO NOT force NV12; we allow RGB(A) negotiation.
// Still capture is serialized by releasing the camera (pipeline -> NULL),
// running rpicam-still/libcamera-still with optional --shutter/--gain,
// saving into /ssd/RAW, parsing metadata if available, then resuming preview.

using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using SysTask = System.Threading.Tasks.Task;
using Gtk;
using Gdk;
using Gst;
using GObject; // Value
using GLib;

static class GirNativeResolver
{
    static readonly Dictionary<string, string> Map = new()
    {
        ["Gst"]        = "libgstreamer-1.0.so.0",
        ["GstBase"]    = "libgstbase-1.0.so.0",
        ["GstVideo"]   = "libgstvideo-1.0.so.0",
        ["GstApp"]     = "libgstapp-1.0.so.0",
        ["GstPbutils"] = "libgstpbutils-1.0.so.0",
    };
    static IntPtr Resolve(string name, Assembly asm, DllImportSearchPath? path)
        => Map.TryGetValue(name, out var real) ? NativeLibrary.Load(real) : IntPtr.Zero;
    public static void RegisterFor(params Assembly[] assemblies)
    {
        foreach (var a in assemblies) NativeLibrary.SetDllImportResolver(a, Resolve);
    }
}

public static class Program
{
    private static Pipeline? _pipeline;
    private static Element?  _src;
    private static Element?  _sink;
    private static Element?  _crop;     // videocrop (may be null if plugin missing)
    private static Element?  _vscale;   // videoscale (may be null)
    private static Picture?  _picture;

    private static bool _autoAE = true;
    private static ComboBoxText? _isoBox;
    private static ComboBoxText? _shutBox;
    private static ComboBoxText? _resBox;
    private static Label? _hud;

    // Zoom UI
    private static Scale? _zoomScale;
    private static Scale? _panXScale;
    private static Scale? _panYScale;

    private static uint _livePollId = 0;

    // Last-capture effective values
    private static long?   _lastExpUs;
    private static int?    _lastIso;
    private static double? _lastAG;

    // Source size we request from libcamerasrc
    private const int SRC_W = 1280;
    private const int SRC_H = 720;

    // Zoom/crop state in source pixel space
    private static double _zoom = 1.0;       // 1..8
    private static double _centerX = SRC_W / 2.0;
    private static double _centerY = SRC_H / 2.0;

    private static readonly double[] ShutterSteps =
    {
        1.0/8000, 1.0/4000, 1.0/2000, 1.0/1000, 1.0/500, 1.0/250, 1.0/125, 1.0/60, 1.0/50, 1.0/30,
        1.0/25, 1.0/20, 1.0/15, 1.0/10, 1.0/8, 1.0/6, 1.0/5, 1.0/4, 1.0/3, 1.0/2, 1.0,
        2, 4, 8, 15, 30
    };
    private static readonly int[] IsoSteps = { 100, 200, 400, 800, 1600, 3200, 6400, 12800 };
    private static readonly (string Label, int Width, int Height)[] ResolutionOptions =
    {
        ("1928 x 1090 (2K crop)", 1928, 1090),
        ("3856 x 2180 (4K full)", 3856, 2180)
    };

    public static int Main(string[] args)
    {
        // Quiet some spam
        Environment.SetEnvironmentVariable("GTK_A11Y", "none");
        Environment.SetEnvironmentVariable("NO_AT_BRIDGE", "1");
        Environment.SetEnvironmentVariable("LIBCAMERA_LOG_LEVELS", "*:2");

        GirNativeResolver.RegisterFor(
            typeof(Gst.Application).Assembly,
            typeof(GstVideo.VideoInfo).Assembly
        );
        Gst.Application.Init();

        var app = Gtk.Application.New("dev.poc.gtk4.libcamera", Gio.ApplicationFlags.FlagsNone);
        app.OnActivate += (_, __) => OnActivate(app);
        return app.Run(args.Length, args);
    }

    private static void OnActivate(Gtk.Application app)
    {
        var win = ApplicationWindow.New(app);
        win.Title = "openDSLM – Live Preview + RAW (DNG)";
        win.SetDefaultSize(1280, 720);

        var overlay = Overlay.New();
        _picture = Picture.New();
        overlay.SetChild(_picture);

        _hud = Label.New("");
        _hud.Halign = Align.Start;
        _hud.Valign = Align.Start;
        _hud.MarginTop = 8; _hud.MarginStart = 8;
        _hud.AddCssClass("hud-readout");
        overlay.AddOverlay(_hud);

        var panel = Box.New(Orientation.Vertical, 8);
        panel.Halign = Align.End; panel.Valign = Align.Start;
        panel.MarginTop = 12; panel.MarginEnd = 12;
        panel.AddCssClass("control-panel");

        // AE/AGC toggle
        var autoRow = Box.New(Orientation.Horizontal, 6);
        autoRow.AddCssClass("control-row");
        var autoLabel = Label.New("Auto AE/AGC");
        autoLabel.AddCssClass("control-inline-label");
        autoRow.Append(autoLabel);
        var autoChk = CheckButton.New();
        autoChk.Active = _autoAE;
        autoChk.AddCssClass("control-toggle");
        autoChk.OnToggled += (_, __) =>
        {
            _autoAE = autoChk.Active;
            if (_isoBox != null)  _isoBox.Sensitive  = !_autoAE;
            if (_shutBox != null) _shutBox.Sensitive = !_autoAE;
            RebuildPreview();
        };
        autoRow.Append(autoChk);
        panel.Append(autoRow);

        // Resolution
        var resLabel = Label.New("Still Resolution");
        resLabel.AddCssClass("control-section-label");
        panel.Append(resLabel);
        _resBox = ComboBoxText.New();
        foreach (var opt in ResolutionOptions) _resBox.AppendText(opt.Label);
        _resBox.Active = 0;
        _resBox.AddCssClass("control-input");
        panel.Append(_resBox);

        // ISO
        var isoLabel = Label.New("ISO");
        isoLabel.AddCssClass("control-section-label");
        panel.Append(isoLabel);
        _isoBox = ComboBoxText.New();
        foreach (var iso in IsoSteps) _isoBox.AppendText(iso.ToString());
        _isoBox.Active = System.Array.IndexOf(IsoSteps, 400);
        _isoBox.Sensitive = !_autoAE;
        _isoBox.AddCssClass("control-input");
        _isoBox.OnChanged += (_, __) => { if (!_autoAE) RebuildPreview(); };
        panel.Append(_isoBox);

        // Shutter
        var shutLabel = Label.New("Shutter");
        shutLabel.AddCssClass("control-section-label");
        panel.Append(shutLabel);
        _shutBox = ComboBoxText.New();
        foreach (var sec in ShutterSteps) _shutBox.AppendText(ShutterLabel(sec));
        _shutBox.Active = System.Array.IndexOf(ShutterSteps, 1.0/60);
        _shutBox.Sensitive = !_autoAE;
        _shutBox.AddCssClass("control-input");
        _shutBox.OnChanged += (_, __) => { if (!_autoAE) RebuildPreview(); };
        panel.Append(_shutBox);

        // Zoom & Pan
        var zoomLabel = Label.New("Zoom");
        zoomLabel.AddCssClass("control-section-label");
        panel.Append(zoomLabel);
        var zoomAdj = Adjustment.New(1.0, 1.0, 8.0, 0.1, 0.5, 0.0);
        _zoomScale = Scale.New(Orientation.Horizontal, zoomAdj);
        _zoomScale.Digits = 2;
        _zoomScale.AddCssClass("control-input");
        ((Gtk.Range)_zoomScale).SetValue(1.0);
        _zoomScale.OnValueChanged += (_, __) =>
        {
            var zv = ((Gtk.Range)_zoomScale!).GetValue();
            _zoom = Math.Max(1.0, Math.Min(8.0, zv));
            UpdatePanSensitivity();
            UpdateCropRect();
        };
        panel.Append(_zoomScale);

        var panLabel = Label.New("Pan X / Pan Y");
        panLabel.AddCssClass("control-section-label");
        panel.Append(panLabel);
        var panXAdj = Adjustment.New(0.5, 0.0, 1.0, 0.01, 0.1, 0.0);
        var panYAdj = Adjustment.New(0.5, 0.0, 1.0, 0.01, 0.1, 0.0);
        _panXScale = Scale.New(Orientation.Horizontal, panXAdj);
        _panYScale = Scale.New(Orientation.Horizontal, panYAdj);
        _panXScale.Digits = 2; _panYScale.Digits = 2;
        _panXScale.AddCssClass("control-input");
        _panYScale.AddCssClass("control-input");
        _panXScale.OnValueChanged += (_, __) =>
        {
            _centerX = SRC_W * ((Gtk.Range)_panXScale!).GetValue();
            UpdateCropRect();
        };
        _panYScale.OnValueChanged += (_, __) =>
        {
            _centerY = SRC_H * ((Gtk.Range)_panYScale!).GetValue();
            UpdateCropRect();
        };
        panel.Append(_panXScale);
        panel.Append(_panYScale);

        // Capture
        var btn = Button.NewWithLabel("● Capture DNG");
        btn.AddCssClass("control-button");
        btn.OnClicked += async (_, __) =>
        {
            btn.Sensitive = false;
            try { await CaptureDngSerializedAsync(); }
            finally { btn.Sensitive = true; }
        };
        panel.Append(btn);

        overlay.AddOverlay(panel);
        TryInstallCss();

        win.SetChild(overlay);
        win.Present();

        RebuildPreview();
    }

    private static void TryInstallCss()
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
";
            css.LoadFromData(cssStr, (nint)cssStr.Length);
            var display = Gdk.Display.GetDefault();
            if (display is not null) StyleContext.AddProviderForDisplay(display, css, 800);
        }
        catch { }
    }

    private static void RebuildPreview()
    {
        if (_livePollId != 0) { GLib.Functions.SourceRemove(_livePollId); _livePollId = 0; }
        try { _pipeline?.SetState(State.Null); } catch { }
        _pipeline = null; _src = null; _sink = null; _crop = null; _vscale = null;

        bool haveLibcamera = ElementFactory.Find("libcamerasrc") is not null;
        var (iso, us) = GetRequestedIsoAndShutter();
        double gain = Math.Max(1.0, iso / 100.0);

        int fpsNum = 30;
        if (!_autoAE && us > 0)
        {
            double maxFps = Math.Max(1.0, Math.Floor(1_000_000.0 / us));
            if (maxFps > 30.0) maxFps = 30.0;
            fpsNum = (int)maxFps;
            if (fpsNum < 1) fpsNum = 1;
        }

        _pipeline = Pipeline.New("p");

        _src = ElementFactory.Make(haveLibcamera ? "libcamerasrc" : "v4l2src", "src")
               ?? throw new Exception("Failed to create source");

        var queue = ElementFactory.Make("queue", "q")
                   ?? throw new Exception("Failed to create queue");
        SetPropInt(queue, "max-size-buffers", 4);
        SetPropInt(queue, "leaky", 2);

        var convert1 = ElementFactory.Make("videoconvert", "vc1")
                        ?? throw new Exception("Failed to create videoconvert vc1");

        // Try to create videocrop + videoscale (for zoom)
        Element? crop = ElementFactory.Make("videocrop", "crop");
        Element? vscale = ElementFactory.Make("videoscale", "vscale");
        _crop = crop;
        _vscale = vscale;

        var convert2 = ElementFactory.Make("videoconvert", "vc2")
                        ?? throw new Exception("Failed to create videoconvert vc2");

        _sink = ElementFactory.Make("gtk4paintablesink", "psink")
                ?? throw new Exception("gtk4paintablesink not available (install gstreamer1.0-gtk4)");

        _pipeline.Add(_src); _pipeline.Add(queue); _pipeline.Add(convert1);
        if (crop is not null && vscale is not null)
        {
            _pipeline.Add(crop); _pipeline.Add(vscale);
        }
        _pipeline.Add(convert2); _pipeline.Add(_sink);

        // Force/manual controls on libcamerasrc
        if (haveLibcamera)
        {
            TrySetInt(_src, "exposure-time-mode", _autoAE ? 0 : 1);
            TrySetInt(_src, "analogue-gain-mode", _autoAE ? 0 : 1);

            if (_autoAE)
            {
                SetPropInt(_src, "exposure-time", 0);
                SetPropDouble(_src, "analogue-gain", 0.0);
            }
            else
            {
                SetPropInt(_src, "exposure-time", us);
                SetPropDouble(_src, "analogue-gain", gain);
            }
        }

        // Caps near the source: fix format/size/fps we want to work with (NV12 1280x720)
        var capsSrc = Caps.FromString($"video/x-raw,format=NV12,width={SRC_W},height={SRC_H},framerate={fpsNum}/1");
        if (!_src.Link(queue)) throw new Exception("Link src->queue failed");
        if (!queue.LinkFiltered(convert1, capsSrc)) throw new Exception("Link queue->vc1 (caps) failed");

        // Zoom path if we have crop+scale
        bool zoomPath = (crop != null && vscale != null);
        if (zoomPath)
        {
            if (!convert1.Link(crop!)) throw new Exception("Link vc1->crop failed");
            if (!crop!.Link(vscale!)) throw new Exception("Link crop->vscale failed");

            // Keep a constant *size* after scaling, but do NOT force a color format here.
            var capsSizeOnly = Caps.FromString($"video/x-raw,width={SRC_W},height={SRC_H},framerate={fpsNum}/1");
            if (!vscale!.LinkFiltered(convert2, capsSizeOnly)) throw new Exception("Link vscale->vc2 (caps) failed");
        }
        else
        {
            // No zoom path: just go forward without forcing the final format
            if (!convert1.Link(convert2)) throw new Exception("Link vc1->vc2 failed");
        }

        // Final link into gtk4paintablesink: no caps filter (let vc2 pick RGB(A) for the sink)
        if (!convert2.Link(_sink)) throw new Exception("Link vc2->sink failed");

        RebindPaintable();

        if (_pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            throw new Exception("Pipeline failed to start");

        GLib.Functions.IdleAdd(0, () => { RebindPaintable(); return false; });

        // Re-apply manual after PLAYING + init zoom
        GLib.Functions.TimeoutAdd(0, 150, () =>
        {
            ApplyManualControlsIfNeeded();
            _zoom = _zoomScale is null ? 1.0 : ((Gtk.Range)_zoomScale).GetValue();
            _centerX = SRC_W * (_panXScale is null ? 0.5 : ((Gtk.Range)_panXScale).GetValue());
            _centerY = SRC_H * (_panYScale is null ? 0.5 : ((Gtk.Range)_panYScale).GetValue());
            UpdatePanSensitivity();
            UpdateCropRect();
            return false;
        });

        _livePollId = GLib.Functions.TimeoutAdd(0, 200, () =>
        {
            UpdateLiveHudFromSrc();
            return true;
        });
    }

    private static void UpdatePanSensitivity()
    {
        bool enablePan = _zoom > 1.0001 && _crop != null && _vscale != null;
        if (_panXScale != null) _panXScale.Sensitive = enablePan;
        if (_panYScale != null) _panYScale.Sensitive = enablePan;
    }

    private static void UpdateCropRect()
    {
        if (_crop == null) return;

        double z = Math.Max(1.0, _zoom);
        int cropW = Math.Max(16, (int)Math.Round(SRC_W / z));
        int cropH = Math.Max(16, (int)Math.Round(SRC_H / z));

        // clamp center so crop stays inside frame
        double halfW = cropW / 2.0, halfH = cropH / 2.0;
        _centerX = Math.Max(halfW, Math.Min(SRC_W - halfW, _centerX));
        _centerY = Math.Max(halfH, Math.Min(SRC_H - halfH, _centerY));

        int left   = (int)Math.Round(_centerX - halfW);
        int top    = (int)Math.Round(_centerY - halfH);
        left   = Math.Max(0, Math.Min(SRC_W - cropW, left));
        top    = Math.Max(0, Math.Min(SRC_H - cropH, top));
        int right  = SRC_W - (left + cropW);
        int bottom = SRC_H - (top + cropH);

        SetPropInt(_crop, "left", left);
        SetPropInt(_crop, "right", right);
        SetPropInt(_crop, "top", top);
        SetPropInt(_crop, "bottom", bottom);
    }

    private static void ApplyManualControlsIfNeeded()
    {
        if (_src == null || _autoAE) return;
        var (iso, us) = GetRequestedIsoAndShutter();
        double gain = Math.Max(1.0, iso / 100.0);
        TrySetInt(_src, "exposure-time-mode", 1);
        TrySetInt(_src, "analogue-gain-mode", 1);
        SetPropInt(_src, "exposure-time", us);
        SetPropDouble(_src, "analogue-gain", gain);
    }

    private static void RebindPaintable()
    {
        if (_sink == null || _picture == null) return;
        try
        {
            Value v = new();
            _sink.GetProperty("paintable", v);
            var obj = v.GetObject();
            if (obj is Paintable p) _picture.SetPaintable(p);
            v.Unset();
        }
        catch { }
    }

    private static void UpdateLiveHudFromSrc()
    {
        if (_hud == null) return;
        string live = "Live: —";
        long? expUs = null;
        double? ag = null;
        if (_src != null)
        {
            if (TryGetInt(_src,  "exposure-time", out int  l))  expUs = (long)l;
            if (TryGetNumberAsDouble(_src, "analogue-gain", out double g)) ag = g;
        }

        var parts = new List<string>();
        if (expUs.HasValue) parts.Add($"t={FormatShutter(expUs.Value)}");
        if (ag.HasValue) parts.Add($"g={ag.Value:0.##}" + (_autoAE ? "" : $" (ISO≈{Math.Round(ag.Value*100)})"));
        if (parts.Count > 0) live = "Live: " + string.Join("  ", parts);

        string last = "Last: —";
        if (_lastExpUs.HasValue || _lastIso.HasValue || _lastAG.HasValue)
        {
            string s = _lastExpUs.HasValue ? FormatShutter(_lastExpUs.Value) : "?";
            string isoStr = _lastIso.HasValue ? _lastIso.Value.ToString() :
                            (_lastAG.HasValue ? (Math.Round(_lastAG.Value*100)).ToString() : "?");
            string agStr = _lastAG.HasValue ? _lastAG.Value.ToString("0.###") : "—";
            last = $"Last: t={s}  ISO={isoStr}  AG={agStr}";
        }

        _hud.SetText($"{live}\n{last}");
    }

    // --- Capture ---
    private static async SysTask CaptureDngSerializedAsync()
    {
        if (_pipeline == null) return;
        _pipeline.SetState(State.Null);
        await SysTask.Delay(300);

        try
        {
            string outDir = "/ssd/RAW";
            Directory.CreateDirectory(outDir);
            string ts = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

            bool preferRpi  = HasCmd("rpicam-still");
            bool haveLibcam = HasCmd("libcamera-still");
            if (!preferRpi && !haveLibcam)
                throw new Exception("Neither rpicam-still nor libcamera-still found in PATH.");

            var (iso, us) = GetRequestedIsoAndShutter();
            double gain = Math.Max(1.0, iso / 100.0);
            string manualArgs = _autoAE ? "" : $" --shutter {us} --gain {gain:0.###} ";
            var (capWidth, capHeight) = GetSelectedStillResolution();

            int exitCode = -1;
            string? dngPathFinal = null;
            string? metaPath = null;

            const int maxAttempts = 8;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (preferRpi)
                {
                    string baseName = Path.Combine(outDir, $"RPICAM_{ts}");
                    string dngPath  = baseName + ".dng";
                    string cmd = $"rpicam-still -n --immediate -t 1 --raw -o \"{dngPath}\"{manualArgs} --width {capWidth} --height {capHeight}";
                    exitCode = await RunShellAsync(cmd);
                    if (exitCode == 0 && File.Exists(dngPath)) { dngPathFinal = dngPath; break; }
                }
                else if (haveLibcam)
                {
                    string basePath = Path.Combine(outDir, $"LIBCAM_{ts}");
                    string jpgPath  = basePath + ".jpg";
                    string dngA     = basePath + ".dng";
                    string dngB     = basePath + ".jpg.dng";
                    metaPath        = basePath + ".json";

                    string cmd = $"libcamera-still -n --immediate -t 1 -o \"{jpgPath}\" --raw --width {capWidth} --height {capHeight}{manualArgs} --metadata \"{metaPath}\"";
                    exitCode = await RunShellAsync(cmd);
                    if (exitCode == 0)
                    {
                        string produced = File.Exists(dngA) ? dngA : (File.Exists(dngB) ? dngB : "");
                        if (string.IsNullOrEmpty(produced)) throw new Exception("libcamera-still did not produce a DNG.");
                        string finalDng = basePath + ".dng";
                        if (!string.Equals(produced, finalDng, StringComparison.Ordinal))
                            File.Move(produced, finalDng, overwrite: true);
                        if (File.Exists(jpgPath)) File.Delete(jpgPath);
                        dngPathFinal = finalDng; break;
                    }
                }
                int backoffMs = 100 + attempt * 150;
                Console.WriteLine($"Capture attempt {attempt} failed (exit {exitCode}). Retrying in {backoffMs} ms...");
                await SysTask.Delay(backoffMs);
            }

            if (exitCode != 0 || dngPathFinal is null)
                throw new Exception($"Still tool failed after retries. Last exit code: {exitCode}.");

            if (metaPath != null && File.Exists(metaPath)) ParseLibcameraJson(metaPath);
            if (HasCmd("exiftool")) await ParseExiftoolAsync(dngPathFinal);

            RebuildPreview();
        }
        finally
        {
            if (_pipeline != null)
            {
                _pipeline.SetState(State.Playing);
                await SysTask.Delay(300);
                GLib.Functions.IdleAdd(0, () => { RebindPaintable(); return false; });
            }
        }
    }

    // --- Metadata parsing ---
    private static void ParseLibcameraJson(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            long? expUs = null; double? ag = null;

            void Scan(JsonElement e)
            {
                if (e.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in e.EnumerateObject())
                    {
                        string n = p.Name.ToLowerInvariant();
                        if (n is "exposuretime" or "exposure_time" or "shutter")
                        { if (p.Value.TryGetInt64(out long v64)) expUs = v64; else if (p.Value.TryGetDouble(out double vd)) expUs = (long)vd; }
                        else if (n is "analoguegain" or "analoggain" or "ag")
                        { if (p.Value.TryGetDouble(out double v)) ag = v; }
                        else Scan(p.Value);
                    }
                }
                else if (e.ValueKind == JsonValueKind.Array)
                    foreach (var x in e.EnumerateArray()) Scan(x);
            }

            Scan(doc.RootElement);
            _lastExpUs = expUs;
            _lastAG    = ag;
            _lastIso   = ag.HasValue ? (int)Math.Round(ag.Value * 100.0) : null;
        }
        catch (Exception ex) { Console.WriteLine($"Metadata parse failed: {ex.Message}"); }
    }

    private static async SysTask ParseExiftoolAsync(string dngPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                ArgumentList = { "-lc", $"exiftool -j -ExposureTime -ISO \"{dngPath}\"" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi)!;
            string so = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            var arr = JsonSerializer.Deserialize<JsonElement>(so);
            if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
            {
                var obj = arr[0];
                long? us = null;
                if (obj.TryGetProperty("ExposureTime", out var et))
                {
                    if (et.ValueKind == JsonValueKind.String)
                    { var s = et.GetString() ?? ""; us = ParseExposureTimeToUs(s); }
                    else if (et.ValueKind == JsonValueKind.Number && et.TryGetDouble(out double secs))
                    { us = (long)Math.Round(secs * 1_000_000.0); }
                }
                int? iso = null;
                if (obj.TryGetProperty("ISO", out var isoNode) && isoNode.TryGetInt32(out int isoVal)) iso = isoVal;
                _lastExpUs = us ?? _lastExpUs;
                _lastIso   = iso ?? _lastIso;
                _lastAG    = (_lastIso.HasValue) ? _lastIso.Value / 100.0 : _lastAG;
            }
        }
        catch (Exception ex) { Console.WriteLine($"exiftool parse failed: {ex.Message}"); }
    }

    // --- HUD / helpers ---
    private static (int iso, int shutterUs) GetRequestedIsoAndShutter()
    {
        int iso = 400;
        int us  = 16_666;
        if (_isoBox != null)
        {
            var t = _isoBox.GetActiveText();
            if (!string.IsNullOrEmpty(t) && int.TryParse(t, out var parsed)) iso = parsed;
        }
        if (_shutBox != null)
        {
            int idx = _shutBox.Active;
            if (idx >= 0 && idx < ShutterSteps.Length)
            {
                var sec = ShutterSteps[idx];
                us = Math.Max(1, (int)Math.Round(sec * 1_000_000.0));
            }
        }
        return (iso, us);
    }

    private static (int width, int height) GetSelectedStillResolution()
    {
        int idx = _resBox?.Active ?? -1;
        if (idx >= 0 && idx < ResolutionOptions.Length)
            return (ResolutionOptions[idx].Width, ResolutionOptions[idx].Height);
        return (ResolutionOptions[0].Width, ResolutionOptions[0].Height);
    }

    private static string ShutterLabel(double sec)
    {
        if (sec < 1.0) { int denom = (int)Math.Round(1.0 / sec); return $"1/{denom}"; }
        return $"{sec:0}s";
    }
    private static string FormatShutter(long us)
    {
        double sec = us / 1_000_000.0;
        if (sec >= 0.5) return $"{sec:0.###}s";
        double denom = Math.Round(1.0 / Math.Max(1e-9, sec));
        return $"1/{denom:0}";
    }
    private static long? ParseExposureTimeToUs(string et)
    {
        try
        {
            et = et.Trim().ToLowerInvariant();
            if (et.Contains("/"))
            {
                var parts = et.Split('/');
                if (parts.Length == 2 && double.TryParse(parts[0], out double n) && double.TryParse(parts[1], out double d) && d != 0)
                    return (long)Math.Round((n / d) * 1_000_000.0);
            }
            if (et.EndsWith("s")) et = et[..^1];
            if (double.TryParse(et, out double secs)) return (long)Math.Round(secs * 1_000_000.0);
        }
        catch { }
        return null;
    }

    // Property helpers
    private static void TrySetInt(Element e, string name, int i) { try { SetPropInt(e, name, i); } catch { } }
    private static void SetPropInt(Element e, string name, int i)
    {
        Value v = new(); v.Init(GObject.Type.Int); v.SetInt(i); e.SetProperty(name, v); v.Unset();
    }
    private static void SetPropDouble(Element e, string name, double d)
    {
        Value v = new(); v.Init(GObject.Type.Double); v.SetDouble(d); e.SetProperty(name, v); v.Unset();
    }
    private static bool TryGetInt(Element e, string name, out int value)
    {
        try { Value v = new(); e.GetProperty(name, v); value = v.GetInt(); v.Unset(); return true; }
        catch { value = default; return false; }
    }
    private static bool TryGetNumberAsDouble(Element e, string name, out double value)
    {
        try { Value v = new(); v.Init(GObject.Type.Double); e.GetProperty(name, v); value = v.GetDouble(); v.Unset(); return true; }
        catch { value = default; return false; }
    }

    // Shell helpers
    private static async System.Threading.Tasks.Task<int> RunShellAsync(string cmd)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "bash",
            ArgumentList = { "-lc", cmd },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi)!;
        string so = await p.StandardOutput.ReadToEndAsync();
        string se = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        Console.WriteLine($"CMD: {cmd}\nEXIT: {p.ExitCode}\nSTDOUT:\n{so}\nSTDERR:\n{se}");
        return p.ExitCode;
    }
    private static bool HasCmd(string name)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "bash",
                ArgumentList = { "-lc", $"command -v {name}" },
                RedirectStandardOutput = true,
                UseShellExecute = false
            });
            p!.WaitForExit();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
