using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Gtk;
using Gst;
using GObject;
using GLib;
using Gdk;
using GLibFunctions = GLib.Functions;
using Task = System.Threading.Tasks.Task;
using TaskInt = System.Threading.Tasks.Task<int>;

public sealed class CameraController : IDisposable
{
    private const int SRC_W = 1280;
    private const int SRC_H = 720;

    private readonly CameraState _state;
    private readonly ActionDispatcher _dispatcher;

    private Pipeline? _pipeline;
    private Element? _src;
    private Element? _sink;
    private Element? _crop;
    private Element? _vscale;
    private Picture? _picture;
    private Label? _hud;
    private uint _livePollId;

    private bool _supportsZoomCropping;

    public event EventHandler? ZoomInfrastructureChanged;

    public bool SupportsZoomCropping => _supportsZoomCropping;

    public CameraController(CameraState state, ActionDispatcher dispatcher)
    {
        _state = state;
        _dispatcher = dispatcher;

        RegisterActions();
    }

    public void AttachView(Picture picture, Label hud)
    {
        _picture = picture;
        _hud = hud;
    }

    public void Dispose()
    {
        if (_livePollId != 0)
        {
            GLibFunctions.SourceRemove(_livePollId);
            _livePollId = 0;
        }

        try
        {
            _pipeline?.SetState(State.Null);
        }
        catch
        {
        }

        _pipeline?.Dispose();
        _pipeline = null;
    }

    private void RegisterActions()
    {
        _dispatcher.Register(AppActionId.InitializePreview, () =>
        {
            RebuildPreview();
            return Task.CompletedTask;
        });

        _dispatcher.Register<ToggleAutoExposurePayload>(AppActionId.ToggleAutoExposure, payload =>
        {
            _state.AutoExposureEnabled = payload.Enabled;
            RebuildPreview();
        });

        _dispatcher.Register<SelectIndexPayload>(AppActionId.SelectIso, payload =>
        {
            _state.IsoIndex = payload.Index;
            if (!_state.AutoExposureEnabled)
            {
                RebuildPreview();
            }
        });

        _dispatcher.Register<SelectIndexPayload>(AppActionId.SelectShutter, payload =>
        {
            _state.ShutterIndex = payload.Index;
            if (!_state.AutoExposureEnabled)
            {
                RebuildPreview();
            }
        });

        _dispatcher.Register<SelectIndexPayload>(AppActionId.SelectResolution, payload =>
        {
            _state.ResolutionIndex = payload.Index;
        });

        _dispatcher.Register<AdjustZoomPayload>(AppActionId.AdjustZoom, payload =>
        {
            _state.Zoom = payload.Zoom;
            UpdateCropRect();
        });

        _dispatcher.Register<AdjustPanPayload>(AppActionId.AdjustPan, payload =>
        {
            _state.PanX = payload.X;
            _state.PanY = payload.Y;
            UpdateCropRect();
        });

        _dispatcher.Register(AppActionId.RefreshPreview, () =>
        {
            RebuildPreview();
            return Task.CompletedTask;
        });

        _dispatcher.Register(AppActionId.CaptureStill, async () =>
        {
            await CaptureDngSerializedAsync().ConfigureAwait(false);
        });

        _dispatcher.Register(AppActionId.RefreshHud, () =>
        {
            UpdateLiveHudFromSrc();
            return Task.CompletedTask;
        });
    }

    public void RebuildPreview()
    {
        if (_livePollId != 0)
        {
            GLibFunctions.SourceRemove(_livePollId);
            _livePollId = 0;
        }

        try
        {
            _pipeline?.SetState(State.Null);
        }
        catch
        {
        }

        _pipeline = null;
        _src = null;
        _sink = null;
        _crop = null;
        _vscale = null;

        bool haveLibcamera = ElementFactory.Find("libcamerasrc") is not null;
        var (iso, us) = _state.GetManualRequest();
        double gain = Math.Max(1.0, iso / 100.0);

        int fpsNum = 30;
        if (!_state.AutoExposureEnabled && us > 0)
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

        Element? crop = ElementFactory.Make("videocrop", "crop");
        Element? vscale = ElementFactory.Make("videoscale", "vscale");
        _crop = crop;
        _vscale = vscale;

        var convert2 = ElementFactory.Make("videoconvert", "vc2")
                        ?? throw new Exception("Failed to create videoconvert vc2");

        _sink = ElementFactory.Make("gtk4paintablesink", "psink")
                ?? throw new Exception("gtk4paintablesink not available (install gstreamer1.0-gtk4)");

        _pipeline.Add(_src);
        _pipeline.Add(queue);
        _pipeline.Add(convert1);
        if (crop is not null && vscale is not null)
        {
            _pipeline.Add(crop);
            _pipeline.Add(vscale);
        }
        _pipeline.Add(convert2);
        _pipeline.Add(_sink);

        if (haveLibcamera)
        {
            TrySetInt(_src, "exposure-time-mode", _state.AutoExposureEnabled ? 0 : 1);
            TrySetInt(_src, "analogue-gain-mode", _state.AutoExposureEnabled ? 0 : 1);

            if (_state.AutoExposureEnabled)
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

        var capsSrc = Caps.FromString($"video/x-raw,format=NV12,width={SRC_W},height={SRC_H},framerate={fpsNum}/1");
        if (!_src.Link(queue)) throw new Exception("Link src->queue failed");
        if (!queue.LinkFiltered(convert1, capsSrc)) throw new Exception("Link queue->vc1 (caps) failed");

        bool zoomPath = (crop != null && vscale != null);
        if (zoomPath)
        {
            if (!convert1.Link(crop!)) throw new Exception("Link vc1->crop failed");
            if (!crop!.Link(vscale!)) throw new Exception("Link crop->vscale failed");

            var capsSizeOnly = Caps.FromString($"video/x-raw,width={SRC_W},height={SRC_H},framerate={fpsNum}/1");
            if (!vscale!.LinkFiltered(convert2, capsSizeOnly)) throw new Exception("Link vscale->vc2 (caps) failed");
        }
        else
        {
            if (!convert1.Link(convert2)) throw new Exception("Link vc1->vc2 failed");
        }

        if (!convert2.Link(_sink)) throw new Exception("Link vc2->sink failed");

        UpdateZoomInfrastructureFlag(zoomPath);

        RebindPaintable();

        if (_pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
        {
            throw new Exception("Pipeline failed to start");
        }

        GLibFunctions.IdleAdd(0, () =>
        {
            RebindPaintable();
            return false;
        });

        GLibFunctions.TimeoutAdd(0, 150, () =>
        {
            ApplyManualControlsIfNeeded();
            UpdateCropRect();
            return false;
        });

        _livePollId = GLibFunctions.TimeoutAdd(0, 200, () =>
        {
            UpdateLiveHudFromSrc();
            return true;
        });
    }

    private void UpdateZoomInfrastructureFlag(bool newValue)
    {
        if (_supportsZoomCropping == newValue) return;
        _supportsZoomCropping = newValue;
        ZoomInfrastructureChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyManualControlsIfNeeded()
    {
        if (_src == null || _state.AutoExposureEnabled) return;
        var (iso, us) = _state.GetManualRequest();
        double gain = Math.Max(1.0, iso / 100.0);
        TrySetInt(_src, "exposure-time-mode", 1);
        TrySetInt(_src, "analogue-gain-mode", 1);
        SetPropInt(_src, "exposure-time", us);
        SetPropDouble(_src, "analogue-gain", gain);
    }

    private void UpdateCropRect()
    {
        if (_crop == null) return;

        double zoom = Math.Max(1.0, _state.Zoom);
        int cropW = Math.Max(16, (int)Math.Round(SRC_W / zoom));
        int cropH = Math.Max(16, (int)Math.Round(SRC_H / zoom));

        double centerX = SRC_W * Math.Clamp(_state.PanX, 0.0, 1.0);
        double centerY = SRC_H * Math.Clamp(_state.PanY, 0.0, 1.0);

        double halfW = cropW / 2.0;
        double halfH = cropH / 2.0;
        centerX = Math.Max(halfW, Math.Min(SRC_W - halfW, centerX));
        centerY = Math.Max(halfH, Math.Min(SRC_H - halfH, centerY));

        int left = (int)Math.Round(centerX - halfW);
        int top = (int)Math.Round(centerY - halfH);
        left = Math.Max(0, Math.Min(SRC_W - cropW, left));
        top = Math.Max(0, Math.Min(SRC_H - cropH, top));
        int right = SRC_W - (left + cropW);
        int bottom = SRC_H - (top + cropH);

        SetPropInt(_crop, "left", left);
        SetPropInt(_crop, "right", right);
        SetPropInt(_crop, "top", top);
        SetPropInt(_crop, "bottom", bottom);

        double normalizedX = (left + cropW / 2.0) / SRC_W;
        double normalizedY = (top + cropH / 2.0) / SRC_H;

        if (Math.Abs(_state.PanX - normalizedX) > 0.0001)
        {
            _state.PanX = normalizedX;
        }
        if (Math.Abs(_state.PanY - normalizedY) > 0.0001)
        {
            _state.PanY = normalizedY;
        }
    }

    private void RebindPaintable()
    {
        if (_sink == null || _picture == null) return;
        try
        {
            Value v = new();
            _sink.GetProperty("paintable", v);
            var obj = v.GetObject();
            if (obj is Paintable p)
            {
                _picture.SetPaintable(p);
            }
            v.Unset();
        }
        catch
        {
        }
    }

    private void UpdateLiveHudFromSrc()
    {
        if (_hud == null) return;
        string live = "Live: —";
        long? expUs = null;
        double? ag = null;
        if (_src != null)
        {
            if (TryGetInt(_src, "exposure-time", out int l)) expUs = (long)l;
            if (TryGetNumberAsDouble(_src, "analogue-gain", out double g)) ag = g;
        }

        if (expUs.HasValue || ag.HasValue)
        {
            var parts = new List<string>();
            if (expUs.HasValue) parts.Add($"t={FormatShutter(expUs.Value)}");
            if (ag.HasValue)
            {
                string isoApprox = _state.AutoExposureEnabled ? string.Empty : $" (ISO≈{Math.Round(ag.Value * 100)})";
                parts.Add($"g={ag.Value:0.##}{isoApprox}");
            }
            if (parts.Count > 0) live = "Live: " + string.Join("  ", parts);
        }

        string last = "Last: —";
        if (_state.LastExposureMicroseconds.HasValue || _state.LastIso.HasValue || _state.LastAnalogueGain.HasValue)
        {
            string s = _state.LastExposureMicroseconds.HasValue ? FormatShutter(_state.LastExposureMicroseconds.Value) : "?";
            string isoStr = _state.LastIso.HasValue ? _state.LastIso.Value.ToString() :
                            (_state.LastAnalogueGain.HasValue ? Math.Round(_state.LastAnalogueGain.Value * 100).ToString() : "?");
            string agStr = _state.LastAnalogueGain.HasValue ? _state.LastAnalogueGain.Value.ToString("0.###") : "—";
            last = $"Last: t={s}  ISO={isoStr}  AG={agStr}";
        }

        _hud.SetText($"{live}\n{last}");
    }

    private async Task CaptureDngSerializedAsync()
    {
        if (_pipeline == null) return;
        _pipeline.SetState(State.Null);
        await Task.Delay(300).ConfigureAwait(false);

        try
        {
            string outDir = "/ssd/RAW";
            Directory.CreateDirectory(outDir);
            string ts = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

            bool preferRpi = HasCmd("rpicam-still");
            bool haveLibcam = HasCmd("libcamera-still");
            if (!preferRpi && !haveLibcam)
            {
                throw new Exception("Neither rpicam-still nor libcamera-still found in PATH.");
            }

            var (iso, us) = _state.GetManualRequest();
            double gain = Math.Max(1.0, iso / 100.0);
            string manualArgs = _state.AutoExposureEnabled ? string.Empty : $" --shutter {us} --gain {gain:0.###} ";
            var (capWidth, capHeight) = _state.GetSelectedStillResolution();

            int exitCode = -1;
            string? dngPathFinal = null;
            string? metaPath = null;

            const int maxAttempts = 8;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (preferRpi)
                {
                    string baseName = Path.Combine(outDir, $"RPICAM_{ts}");
                    string dngPath = baseName + ".dng";
                    string cmd = $"rpicam-still -n --immediate -t 1 --raw -o \"{dngPath}\"{manualArgs} --width {capWidth} --height {capHeight}";
                    exitCode = await RunShellAsync(cmd).ConfigureAwait(false);
                    if (exitCode == 0 && File.Exists(dngPath))
                    {
                        dngPathFinal = dngPath;
                        break;
                    }
                }
                else if (haveLibcam)
                {
                    string basePath = Path.Combine(outDir, $"LIBCAM_{ts}");
                    string jpgPath = basePath + ".jpg";
                    string dngA = basePath + ".dng";
                    string dngB = basePath + ".jpg.dng";
                    metaPath = basePath + ".json";

                    string cmd = $"libcamera-still -n --immediate -t 1 -o \"{jpgPath}\" --raw --width {capWidth} --height {capHeight}{manualArgs} --metadata \"{metaPath}\"";
                    exitCode = await RunShellAsync(cmd).ConfigureAwait(false);
                    if (exitCode == 0)
                    {
                        string produced = File.Exists(dngA) ? dngA : (File.Exists(dngB) ? dngB : string.Empty);
                        if (string.IsNullOrEmpty(produced))
                        {
                            throw new Exception("libcamera-still did not produce a DNG.");
                        }
                        string finalDng = basePath + ".dng";
                        if (!string.Equals(produced, finalDng, StringComparison.Ordinal))
                        {
                            File.Move(produced, finalDng, overwrite: true);
                        }
                        if (File.Exists(jpgPath)) File.Delete(jpgPath);
                        dngPathFinal = finalDng;
                        break;
                    }
                }

                int backoffMs = 100 + attempt * 150;
                Console.WriteLine($"Capture attempt {attempt} failed (exit {exitCode}). Retrying in {backoffMs} ms...");
                await Task.Delay(backoffMs).ConfigureAwait(false);
            }

            if (exitCode != 0 || dngPathFinal is null)
            {
                throw new Exception($"Still tool failed after retries. Last exit code: {exitCode}.");
            }

            if (metaPath != null && File.Exists(metaPath))
            {
                ParseLibcameraJson(metaPath);
            }

            if (HasCmd("exiftool"))
            {
                await ParseExiftoolAsync(dngPathFinal).ConfigureAwait(false);
            }

            RebuildPreview();
        }
        finally
        {
            if (_pipeline != null)
            {
                _pipeline.SetState(State.Playing);
                await Task.Delay(300).ConfigureAwait(false);
                GLibFunctions.IdleAdd(0, () =>
                {
                    RebindPaintable();
                    return false;
                });
            }
        }
    }

    private void ParseLibcameraJson(string path)
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
                            if (p.Value.TryGetDouble(out double gd)) ag = gd;
                        }
                        else
                        {
                            Scan(p.Value);
                        }
                    }
                }
                else if (e.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in e.EnumerateArray())
                    {
                        Scan(item);
                    }
                }
            }

            Scan(doc.RootElement);
            int? approxIso = ag.HasValue ? (int)Math.Round(ag.Value * 100.0) : null;
            _state.UpdateLastCapture(expUs, approxIso, ag);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to parse libcamera JSON: {ex.Message}");
        }
    }

    private async Task ParseExiftoolAsync(string dngPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                ArgumentList = { "-lc", $"exiftool -j -ExposureTime -ISO -AnalogGain \"{dngPath}\"" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var process = Process.Start(psi)!;
            string stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            string stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                Console.WriteLine($"exiftool exited with {process.ExitCode}: {stderr}");
                return;
            }

            using var doc = JsonDocument.Parse(stdout);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var obj = doc.RootElement[0];
                long? expUs = null;
                int? iso = null;
                double? ag = null;

                if (obj.TryGetProperty("ExposureTime", out var expProp))
                {
                    if (expProp.ValueKind == JsonValueKind.Number)
                    {
                        expUs = (long)Math.Round(expProp.GetDouble() * 1_000_000.0);
                    }
                    else if (expProp.ValueKind == JsonValueKind.String)
                    {
                        expUs = ParseExposureTimeToUs(expProp.GetString() ?? string.Empty);
                    }
                }

                if (obj.TryGetProperty("ISO", out var isoProp))
                {
                    if (isoProp.ValueKind == JsonValueKind.Number) iso = isoProp.GetInt32();
                    else if (isoProp.ValueKind == JsonValueKind.String && int.TryParse(isoProp.GetString(), out int isov)) iso = isov;
                }

                if (obj.TryGetProperty("AnalogGain", out var agProp))
                {
                    if (agProp.ValueKind == JsonValueKind.Number) ag = agProp.GetDouble();
                    else if (agProp.ValueKind == JsonValueKind.String && double.TryParse(agProp.GetString(), out double agv)) ag = agv;
                }

                long? finalExp = expUs ?? _state.LastExposureMicroseconds;
                int? finalIso = iso ?? _state.LastIso;
                double? finalAg = ag ?? _state.LastAnalogueGain;
                _state.UpdateLastCapture(finalExp, finalIso, finalAg);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to parse exiftool data: {ex.Message}");
        }
    }

    private static string FormatShutter(double seconds)
    {
        if (seconds <= 0) seconds = 0.0001;
        double denom = 1.0 / seconds;
        if (denom >= 1)
        {
            double rounded = Math.Round(denom);
            if (Math.Abs(denom - rounded) < 0.01)
            {
                return $"1/{rounded:0}";
            }
        }

        if (seconds >= 1.0)
        {
            return $"{seconds:0.#} s";
        }

        return $"{seconds * 1000:0.#} ms";
    }

    public static string ShutterLabel(double seconds)
    {
        return FormatShutter(seconds);
    }

    private static void SetPropInt(Element? element, string name, long value)
    {
        int clamped = value < int.MinValue ? int.MinValue :
                      (value > int.MaxValue ? int.MaxValue : (int)value);
        SetPropInt(element, name, clamped);
    }

    private static void SetPropInt(Element? element, string name, int value)
    {
        if (element is null) return;
        Value v = new();
        v.Init(GObject.Type.Int);
        v.SetInt(value);
        element.SetProperty(name, v);
        v.Unset();
    }

    private static void SetPropDouble(Element? element, string name, double value)
    {
        if (element is null) return;
        Value v = new();
        v.Init(GObject.Type.Double);
        v.SetDouble(value);
        element.SetProperty(name, v);
        v.Unset();
    }

    private static bool TrySetInt(Element? element, string name, int value)
    {
        try
        {
            SetPropInt(element, name, value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetInt(Element element, string name, out int value)
    {
        try
        {
            Value v = new();
            element.GetProperty(name, v);
            value = v.GetInt();
            v.Unset();
            return true;
        }
        catch
        {
            value = 0;
            return false;
        }
    }

    private static bool TryGetNumberAsDouble(Element element, string name, out double value)
    {
        try
        {
            Value v = new();
            v.Init(GObject.Type.Double);
            element.GetProperty(name, v);
            value = v.GetDouble();
            v.Unset();
            return true;
        }
        catch
        {
            value = 0;
            return false;
        }
    }

    private static bool HasCmd(string name)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "bash",
                ArgumentList = { "-lc", $"command -v {name}" },
                RedirectStandardOutput = true,
                UseShellExecute = false
            });
            process!.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async TaskInt RunShellAsync(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "bash",
            ArgumentList = { "-lc", command },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi)!;
        string stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        string stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        Console.WriteLine($"CMD: {command}\nEXIT: {process.ExitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        return process.ExitCode;
    }

    private static long? ParseExposureTimeToUs(string value)
    {
        try
        {
            string v = value.Trim().ToLowerInvariant();
            if (v.Contains('/'))
            {
                var parts = v.Split('/');
                if (parts.Length == 2 && double.TryParse(parts[0], out double num) && double.TryParse(parts[1], out double den) && Math.Abs(den) > double.Epsilon)
                {
                    return (long)Math.Round((num / den) * 1_000_000.0);
                }
            }

            if (v.EndsWith("s")) v = v[..^1];
            if (double.TryParse(v, out double seconds))
            {
                return (long)Math.Round(seconds * 1_000_000.0);
            }
        }
        catch
        {
        }

        return null;
    }
}
