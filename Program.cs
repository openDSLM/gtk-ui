// .NET 8 + GirCore 0.6.x
// Live preview via gtk4paintablesink + overlay UI:
//  - Auto AE/AGC toggle (controls whether we pass manual args for capture & live)
//  - DSLR-like shutter dropdown (1/8000 … 30s)
//  - ISO dropdown (100 … 12800)
//  - "Capture DNG" button
//  - Live HUD from libcamerasrc (AE, exposure-time, analogue-gain)
//  - Last capture HUD (via exiftool JSON if present, or libcamera-still --metadata)
//
// Preview uses NV12 at 1280x720 (fast rebuilds).
// Still capture is serialized by releasing the camera (pipeline -> NULL),
// running rpicam-still/libcamera-still with optional --shutter/--gain,
// saving into /ssd/RAW, parsing metadata if available, then resuming preview.

using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using SysTask = System.Threading.Tasks.Task; // avoid clash with Gst.Task
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
        foreach (var a in assemblies)
            NativeLibrary.SetDllImportResolver(a, Resolve);
    }
}

public static class Program
{
    private static Pipeline? _pipeline;
    private static Element?  _src;
    private static Element?  _sink;
    private static Picture?  _picture;

    // UI state
    private static bool _autoAE = true;
    private static ComboBoxText? _isoBox;
    private static ComboBoxText? _shutBox;
    private static Label? _hud;

    // live poll
    private static uint _livePollId = 0;

    // Last-capture effective values
    private static long?   _lastExpUs;
    private static int?    _lastIso;
    private static double? _lastAG;

    // DSLR-ish shutter steps (seconds)
    private static readonly double[] ShutterSteps =
    {
        1.0/8000, 1.0/4000, 1.0/2000, 1.0/1000, 1.0/500, 1.0/250, 1.0/125, 1.0/60, 1.0/50, 1.0/30,
        1.0/25, 1.0/20, 1.0/15, 1.0/10, 1.0/8, 1.0/6, 1.0/5, 1.0/4, 1.0/3, 1.0/2, 1.0,
        2, 4, 8, 15, 30
    };
    private static readonly int[] IsoSteps = { 100, 200, 400, 800, 1600, 3200, 6400, 12800 };

    public static int Main(string[] args)
    {
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

        // Main video
        _picture = Picture.New();
        overlay.SetChild(_picture);

        // HUD (top-left)
        _hud = Label.New("");
        _hud.Halign = Align.Start;
        _hud.Valign = Align.Start;
        _hud.MarginTop = 8; _hud.MarginStart = 8;
        overlay.AddOverlay(_hud);

        // Controls panel (top-right)
        var panel = Box.New(Orientation.Vertical, 8);
        panel.Halign = Align.End; panel.Valign = Align.Start;
        panel.MarginTop = 12; panel.MarginEnd = 12;

        // Auto toggle
        var autoRow = Box.New(Orientation.Horizontal, 6);
        autoRow.Append(Label.New("Auto AE/AGC"));
        var autoChk = CheckButton.New();
        autoChk.Active = _autoAE;
        autoChk.OnToggled += (_, __) =>
        {
            _autoAE = autoChk.Active;
            if (_isoBox != null)  _isoBox.Sensitive  = !_autoAE;
            if (_shutBox != null) _shutBox.Sensitive = !_autoAE;
            RebuildPreview();
        };
        autoRow.Append(autoChk);
        panel.Append(autoRow);

        // ISO dropdown
        panel.Append(Label.New("ISO"));
        _isoBox = ComboBoxText.New();
        foreach (var iso in IsoSteps) _isoBox.AppendText(iso.ToString());
        _isoBox.Active = System.Array.IndexOf(IsoSteps, 400); // default ISO 400
        _isoBox.Sensitive = !_autoAE;
        _isoBox.OnChanged += (_, __) => { if(!_autoAE) RebuildPreview(); };
        panel.Append(_isoBox);

        // Shutter dropdown
        panel.Append(Label.New("Shutter"));
        _shutBox = ComboBoxText.New();
        foreach (var sec in ShutterSteps) _shutBox.AppendText(ShutterLabel(sec));
        _shutBox.Active = System.Array.IndexOf(ShutterSteps, 1.0/60); // default 1/60
        _shutBox.Sensitive = !_autoAE;
        _shutBox.OnChanged += (_, __) => { if(!_autoAE) RebuildPreview(); };
        panel.Append(_shutBox);

        // Capture button
        var btn = Button.NewWithLabel("● Capture DNG");
        btn.OnClicked += async (_, __) =>
        {
            btn.Sensitive = false;
            try { await CaptureDngSerializedAsync(); }
            finally { btn.Sensitive = true; }
        };
        panel.Append(btn);

        overlay.AddOverlay(panel);

        // Simple overlay styling for readability
        TryInstallCss();

        win.SetChild(overlay);
        win.Present();

        // Build initial preview
        RebuildPreview();
    }

    private static void TryInstallCss()
    {
        try
        {
            var css = CssProvider.New();
            string cssStr = @"
overlay > box, label, button, checkbutton, combobox {
  background-color: rgba(0,0,0,0.55);
  color: #fff;
  padding: 2px;
}
";
            css.LoadFromData(cssStr, (nint)cssStr.Length);
            var display = Gdk.Display.GetDefault();
            if (display is not null)
                StyleContext.AddProviderForDisplay(display, css, 800);
        }
        catch { /* best-effort */ }
    }

    // Build pipeline programmatically and set properties via GLib.Value
    private static void RebuildPreview()
    {
        // Stop live poll
        if (_livePollId != 0) { GLib.Functions.SourceRemove(_livePollId); _livePollId = 0; }

        // Tear down previous pipeline
        try { _pipeline?.SetState(State.Null); } catch { }
        _pipeline = null; _src = null; _sink = null;

        bool haveLibcamera = ElementFactory.Find("libcamerasrc") is not null;
        var (iso, us) = GetRequestedIsoAndShutter();
        double gain = Math.Max(1.0, iso / 100.0);

        // === Choose a framerate compatible with requested shutter (avoid exposure clipping) ===
        int fpsNum = 30, fpsDen = 1; // default
        if (!_autoAE && us > 0)
        {
            // max integer fps that still allows this shutter: <= 1 / (us in seconds)
            double maxFps = Math.Max(1.0, Math.Floor(1_000_000.0 / us)); // ≥ 1
            if (maxFps > 30.0) maxFps = 30.0; // cap preview at 30 fps
            fpsNum = (int)maxFps;
            if (fpsNum < 1) fpsNum = 1;
        }

        // Create elements
        _pipeline = Pipeline.New("p");

        _src = ElementFactory.Make(haveLibcamera ? "libcamerasrc" : "v4l2src", "src")
               ?? throw new Exception("Failed to create source");
        var queue = ElementFactory.Make("queue", "q")
                   ?? throw new Exception("Failed to create queue");
        // keep queue small & leaky as in the Python prototype
        SetPropInt(queue, "max-size-buffers", 4);
        SetPropInt(queue, "leaky", 2 /* downstream */);

        var convert = ElementFactory.Make("videoconvert", "vc")
                      ?? throw new Exception("Failed to create videoconvert");
        _sink = ElementFactory.Make("gtk4paintablesink", "psink")
                ?? throw new Exception("gtk4paintablesink not available (install gstreamer1.0-gtk4)");

        _pipeline.Add(_src); _pipeline.Add(queue); _pipeline.Add(convert); _pipeline.Add(_sink);

        // Set libcamerasrc properties (child + manual when Auto is off)
        if (haveLibcamera)
        {
            // child property like gst-launch: src::stream-role=view-finder
            SetPropString(_src, "src::stream-role", "view-finder");

            if (_autoAE)
            {
                SetPropBool(_src, "ae-enable", true);
                // zeros typically hand control back to AE across builds
                SetPropInt(_src, "exposure-time", 0);
                SetPropDouble(_src, "analogue-gain", 0.0);
            }
            else
            {
                SetPropBool(_src, "ae-enable", false);
                SetPropInt(_src, "exposure-time", us);
                SetPropDouble(_src, "analogue-gain", gain);
            }
        }

        // Link with caps (NV12, 1280x720, fps controlled above)
        var caps = Caps.FromString($"video/x-raw,format=NV12,width=1280,height=720,framerate={fpsNum}/{fpsDen}");
        if (!_src.Link(queue)) throw new Exception("Link src->queue failed");
        if (!queue.LinkFiltered(convert, caps)) throw new Exception("Link queue->convert (filtered caps) failed");
        if (!convert.Link(_sink)) throw new Exception("Link convert->sink failed");

        // Bind paintable to Picture
        RebindPaintable();

        // Start playback
        if (_pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            throw new Exception("Pipeline failed to start");

        // Paintable often becomes available after PLAYING
        GLib.Functions.IdleAdd(0, () => { RebindPaintable(); return false; });

        // Start live poll of src properties (what camera actually uses)
        _livePollId = GLib.Functions.TimeoutAdd(0, 200, () =>
        {
            UpdateLiveHudFromSrc();
            return true;
        });
    }

    private static void RebindPaintable()
    {
        if (_sink == null || _picture == null) return;
        try
        {
            Value v = new();
            _sink.GetProperty("paintable", v);
            var obj = v.GetObject();
            if (obj is Paintable p)
                _picture.SetPaintable(p);
            v.Unset();
        }
        catch { }
    }

    private static void UpdateLiveHudFromSrc()
    {
        if (_hud == null) return;

        string live = "Live: —";
        bool? ae = null;
        long? expUs = null;
        double? ag = null;

        if (_src != null)
        {
            if (TryGetBool(_src, "ae-enable", out bool aeVal)) ae = aeVal;
            if (TryGetLong(_src, "exposure-time", out long l))  expUs = l;
            if (TryGetDouble(_src, "analogue-gain", out double g)) ag = g;
        }

        var parts = new List<string>();
        if (ae.HasValue) parts.Add($"AE={(ae.Value ? "AUTO" : "MAN")}");
        if (expUs.HasValue) parts.Add($"t={FormatShutter(expUs.Value)}");
        if (ag.HasValue) parts.Add($"g={ag.Value:0.###} (ISO≈{Math.Round(ag.Value*100)})");
        if (parts.Count > 0) live = "Live: " + string.Join("  ", parts);

        // Last capture line
        string last = "Last: —";
        if (_lastExpUs.HasValue || _lastIso.HasValue || _lastAG.HasValue)
        {
            string s = _lastExpUs.HasValue ? FormatShutter(_lastExpUs.Value) : "?";
            string isoStr = _lastIso.HasValue ? _lastIso.Value.ToString() : (_lastAG.HasValue ? (Math.Round(_lastAG.Value*100)).ToString() : "?");
            string agStr = _lastAG.HasValue ? _lastAG.Value.ToString("0.###") : "—";
            last = $"Last: t={s}  ISO={isoStr}  AG={agStr}";
        }

        _hud.SetText($"{live}\n{last}");
    }

    // --- Capture (serialized) ---
    private static async SysTask CaptureDngSerializedAsync()
    {
        if (_pipeline == null) return;

        // Release preview so still tool can grab device
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

            // Manual args only if Auto is OFF
            string manualArgs = _autoAE ? "" : $" --shutter {us} --gain {gain:0.###}";

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

                    string cmd = $"rpicam-still -n --immediate -t 1 -o \"{dngPath}\"{manualArgs}";
                    exitCode = await RunShellAsync(cmd);

                    if (exitCode == 0 && File.Exists(dngPath))
                    {
                        dngPathFinal = dngPath;
                        break;
                    }
                }
                else if (haveLibcam)
                {
                    string basePath = Path.Combine(outDir, $"LIBCAM_{ts}");
                    string jpgPath  = basePath + ".jpg";
                    string dngA     = basePath + ".dng";
                    string dngB     = basePath + ".jpg.dng";
                    metaPath        = basePath + ".json";

                    string cmd = $"libcamera-still -n --immediate -t 1 -o \"{jpgPath}\" --raw{manualArgs} --metadata \"{metaPath}\"";
                    exitCode = await RunShellAsync(cmd);

                    if (exitCode == 0)
                    {
                        string produced = File.Exists(dngA) ? dngA : (File.Exists(dngB) ? dngB : "");
                        if (string.IsNullOrEmpty(produced))
                            throw new Exception("libcamera-still did not produce a DNG alongside the JPEG.");

                        string finalDng = basePath + ".dng";
                        if (!string.Equals(produced, finalDng, StringComparison.Ordinal))
                            File.Move(produced, finalDng, overwrite: true);

                        if (File.Exists(jpgPath)) File.Delete(jpgPath);

                        dngPathFinal = finalDng;
                        break;
                    }
                }

                int backoffMs = 100 + attempt * 150;
                Console.WriteLine($"Capture attempt {attempt} failed (exit {exitCode}). Retrying in {backoffMs} ms...");
                await SysTask.Delay(backoffMs);
            }

            if (exitCode != 0 || dngPathFinal is null)
                throw new Exception($"Still tool failed after retries. Last exit code: {exitCode}.");

            // Parse metadata from libcamera JSON if present
            if (metaPath != null && File.Exists(metaPath))
                ParseLibcameraJson(metaPath);

            // Try exiftool as a universal fallback (works for both tools)
            if (HasCmd("exiftool"))
                await ParseExiftoolAsync(dngPathFinal);

            // Rebuild preview with current UI state (so live matches requested again)
            RebuildPreview();
        }
        finally
        {
            // If something exploded before rebuild, at least resume preview
            if (_pipeline != null)
            {
                _pipeline.SetState(State.Playing);
                await SysTask.Delay(300);
                GLib.Functions.IdleAdd(0, () => { RebindPaintable(); return false; });
            }
        }
    }

    // --- Metadata parsing for last capture ---
    private static void ParseLibcameraJson(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);

            long? expUs = null;
            double? ag = null;

            void Scan(JsonElement e)
            {
                if (e.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in e.EnumerateObject())
                    {
                        string n = p.Name.ToLowerInvariant();
                        if (n is "exposuretime" or "exposure_time" or "shutter")
                        {
                            if (p.Value.TryGetInt64(out long v64)) expUs = v64;
                            else if (p.Value.TryGetDouble(out double vd)) expUs = (long)vd;
                        }
                        else if (n is "analoguegain" or "analoggain" or "ag")
                        {
                            if (p.Value.TryGetDouble(out double v)) ag = v;
                        }
                        else
                        {
                            Scan(p.Value);
                        }
                    }
                }
                else if (e.ValueKind == JsonValueKind.Array)
                {
                    foreach (var x in e.EnumerateArray()) Scan(x);
                }
            }

            Scan(doc.RootElement);

            _lastExpUs = expUs;
            _lastAG    = ag;
            _lastIso   = ag.HasValue ? (int)Math.Round(ag.Value * 100.0) : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Metadata parse failed: {ex.Message}");
        }
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
                    {
                        var s = et.GetString() ?? "";
                        us = ParseExposureTimeToUs(s);
                    }
                    else if (et.ValueKind == JsonValueKind.Number && et.TryGetDouble(out double secs))
                    {
                        us = (long)Math.Round(secs * 1_000_000.0);
                    }
                }
                int? iso = null;
                if (obj.TryGetProperty("ISO", out var isoNode) && isoNode.TryGetInt32(out int isoVal))
                    iso = isoVal;

                _lastExpUs = us ?? _lastExpUs;
                _lastIso   = iso ?? _lastIso;
                _lastAG    = (_lastIso.HasValue) ? _lastIso.Value / 100.0 : _lastAG;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"exiftool parse failed: {ex.Message}");
        }
    }

    // --- HUD / helpers ---
    private static (int iso, int shutterUs) GetRequestedIsoAndShutter()
    {
        int iso = 400;
        int us  = 16_666; // ~1/60

        if (_isoBox != null)
        {
            var t = _isoBox.GetActiveText();
            if (!string.IsNullOrEmpty(t) && int.TryParse(t, out var parsed))
                iso = parsed;
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

    private static string ShutterLabel(double sec)
    {
        if (sec < 1.0)
        {
            int denom = (int)Math.Round(1.0 / sec);
            return $"1/{denom}";
        }
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
            if (et.EndsWith("s"))
                et = et[..^1];
            if (double.TryParse(et, out double secs))
                return (long)Math.Round(secs * 1_000_000.0);
        }
        catch { }
        return null;
    }

    // Typed property setters via GLib.Value (no Gst.Parse.Launch needed)
    private static void SetPropBool(Element e, string name, bool b)
    {
        try { Value v = new(); v.Init(GObject.Type.Boolean); v.SetBoolean(b); e.SetProperty(name, v); v.Unset(); }
        catch { }
    }
    private static void SetPropInt(Element e, string name, int i)
    {
        try { Value v = new(); v.Init(GObject.Type.Int); v.SetInt(i); e.SetProperty(name, v); v.Unset(); }
        catch { }
    }
    private static void SetPropDouble(Element e, string name, double d)
    {
        try { Value v = new(); v.Init(GObject.Type.Double); v.SetDouble(d); e.SetProperty(name, v); v.Unset(); }
        catch { }
    }
    private static void SetPropString(Element e, string name, string s)
    {
        try { Value v = new(); v.Init(GObject.Type.String); v.SetString(s); e.SetProperty(name, v); v.Unset(); }
        catch { }
    }

    // Typed property getters for HUD
    private static bool TryGetBool(Element e, string name, out bool value)
    {
        try { Value v = new(); e.GetProperty(name, v); value = v.GetBoolean(); v.Unset(); return true; }
        catch { value = default; return false; }
    }
    private static bool TryGetLong(Element e, string name, out long value)
    {
        try { Value v = new(); e.GetProperty(name, v); value = v.GetInt(); v.Unset(); return true; }
        catch { value = default; return false; }
    }
    private static bool TryGetDouble(Element e, string name, out double value)
    {
        try { Value v = new(); e.GetProperty(name, v); value = v.GetDouble(); v.Unset(); return true; }
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
