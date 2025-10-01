using System;
using System.Globalization;
using Gtk;
using Gst;
using GObject;
using GLib;
using GLibFunctions = GLib.Functions;
using Task = System.Threading.Tasks.Task;
using Gdk;

public sealed class CameraController : IDisposable
{
    private const int StatusPollIntervalMs = 1000;

    private readonly CameraState _state;
    private readonly ActionDispatcher _dispatcher;
    private readonly CameraDaemonClient _daemonClient;

    private Pipeline? _pipeline;
    private Element? _src;
    private Element? _sink;
    private Picture? _picture;
    private Label? _hud;

    private uint _statusPollId;
    private bool _statusRefreshInFlight;
    private bool _supportsZoomCropping;
    private string? _previewSocketPath;
    private string? _previewCaps;
    private DaemonSettings? _lastSettings;
    private string? _lastCaptureToken;

    public event EventHandler? ZoomInfrastructureChanged;

    public bool SupportsZoomCropping => _supportsZoomCropping;

    public CameraController(CameraState state, ActionDispatcher dispatcher)
    {
        _state = state;
        _dispatcher = dispatcher;

        string baseUrl = Environment.GetEnvironmentVariable("OPENDSLM_DAEMON_URL") ?? "http://127.0.0.1:8400/";
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        _daemonClient = new CameraDaemonClient(new global::System.Uri(baseUrl, UriKind.Absolute));

        RegisterActions();
    }

    public void AttachView(Picture picture, Label hud)
    {
        _picture = picture;
        _hud = hud;
    }

    public void Dispose()
    {
        if (_statusPollId != 0)
        {
            GLibFunctions.SourceRemove(_statusPollId);
            _statusPollId = 0;
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
        _src = null;
        _sink = null;

        _daemonClient.Dispose();
    }

    private void RegisterActions()
    {
        _dispatcher.Register(AppActionId.InitializePreview, async () =>
        {
            await InitializeFromDaemonAsync().ConfigureAwait(false);
        });

        _dispatcher.Register<ToggleAutoExposurePayload>(AppActionId.ToggleAutoExposure, async payload =>
        {
            _state.AutoExposureEnabled = payload.Enabled;
            await PushSettingsToDaemonAsync(includeAuto: true).ConfigureAwait(false);
        });

        _dispatcher.Register<SelectIndexPayload>(AppActionId.SelectIso, async payload =>
        {
            _state.IsoIndex = payload.Index;
            if (!_state.AutoExposureEnabled)
            {
                await PushSettingsToDaemonAsync().ConfigureAwait(false);
            }
        });

        _dispatcher.Register<SelectIndexPayload>(AppActionId.SelectShutter, async payload =>
        {
            _state.ShutterIndex = payload.Index;
            if (!_state.AutoExposureEnabled)
            {
                await PushSettingsToDaemonAsync().ConfigureAwait(false);
            }
        });

        _dispatcher.Register<SelectIndexPayload>(AppActionId.SelectResolution, async payload =>
        {
            _state.ResolutionIndex = payload.Index;
            await PushSettingsToDaemonAsync(includeMode: true).ConfigureAwait(false);
        });

        _dispatcher.Register<AdjustZoomPayload>(AppActionId.AdjustZoom, payload =>
        {
            _state.Zoom = payload.Zoom;
            UpdateZoomInfrastructureFlag(false);
        });

        _dispatcher.Register<AdjustPanPayload>(AppActionId.AdjustPan, payload =>
        {
            _state.PanX = payload.X;
            _state.PanY = payload.Y;
        });

        _dispatcher.Register(AppActionId.RefreshPreview, () =>
        {
            RebuildPreview();
            return Task.CompletedTask;
        });

        _dispatcher.Register(AppActionId.CaptureStill, async () =>
        {
            await CaptureStillViaDaemonAsync().ConfigureAwait(false);
        });

        _dispatcher.Register(AppActionId.RefreshHud, () =>
        {
            UpdateHudFromCachedSettings();
            return Task.CompletedTask;
        });
    }

    private async Task InitializeFromDaemonAsync()
    {
        await RefreshDaemonStatusAsync(forcePreviewRebuild: true).ConfigureAwait(false);
        RebuildPreview();
        EnsureStatusPolling();
    }

    private async Task PushSettingsToDaemonAsync(bool includeAuto = false, bool includeMode = false)
    {
        try
        {
            var patch = BuildSettingsPatch(includeAuto, includeMode);
            if (patch != null)
            {
                var updated = await _daemonClient.UpdateSettingsAsync(patch).ConfigureAwait(false);
                if (updated != null)
                {
                    _lastSettings = updated;
                    ApplySettingsToState(updated);
                    UpdateHudFromCachedSettings();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to push settings to daemon: {ex.Message}");
        }
    }

    private DaemonSettingsPatch? BuildSettingsPatch(bool includeAuto, bool includeMode)
    {
        var patch = new DaemonSettingsPatch();
        bool hasChange = false;

        if (includeAuto)
        {
            patch = patch with { AutoExposure = _state.AutoExposureEnabled };
            hasChange = true;
        }

        if (includeMode)
        {
            patch = patch with { Mode = _state.GetSelectedSensorMode() };
            hasChange = true;
        }

        if (!_state.AutoExposureEnabled)
        {
            var (iso, shutterUs) = _state.GetManualRequest();
            double gain = Math.Max(1.0, iso / 100.0);
            patch = patch with
            {
                ShutterUs = shutterUs,
                AnalogueGain = gain,
                Fps = CalculateManualFps(shutterUs)
            };
            hasChange = true;
        }

        if (!hasChange)
        {
            return null;
        }

        return patch;
    }

    private static double CalculateManualFps(long shutterUs)
    {
        if (shutterUs <= 0) return 30.0;
        double maxFps = Math.Floor(1_000_000.0 / shutterUs);
        if (maxFps < 1.0) maxFps = 1.0;
        if (maxFps > 30.0) maxFps = 30.0;
        return maxFps;
    }

    private async Task RefreshDaemonStatusAsync(bool forcePreviewRebuild = false)
    {
        if (_statusRefreshInFlight)
        {
            return;
        }

        _statusRefreshInFlight = true;
        try
        {
            var status = await _daemonClient.GetStatusAsync().ConfigureAwait(false);
            if (status == null)
            {
                return;
            }

            if (status.Settings != null)
            {
                _lastSettings = status.Settings;
                ApplySettingsToState(status.Settings);
            }

            if (!string.IsNullOrWhiteSpace(status.PreviewClientPipeline))
            {
                var (socket, caps) = ExtractPreviewInfo(status.PreviewClientPipeline!);
                bool socketChanged = !string.IsNullOrEmpty(socket) && !string.Equals(socket, _previewSocketPath, StringComparison.Ordinal);
                bool capsChanged = !string.IsNullOrEmpty(caps) && !string.Equals(caps, _previewCaps, StringComparison.Ordinal);
                if (!string.IsNullOrEmpty(socket))
                {
                    _previewSocketPath = socket;
                }
                if (!string.IsNullOrEmpty(caps))
                {
                    _previewCaps = caps;
                }

                if ((socketChanged || capsChanged) && !forcePreviewRebuild)
                {
                    GLibFunctions.IdleAdd(0, () =>
                    {
                        RebuildPreview();
                        return false;
                    });
                }
            }

            if (status.LastCapture != null)
            {
                ProcessLastCapture(status.LastCapture);
            }

            UpdateHudFromCachedSettings();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to refresh daemon status: {ex.Message}");
        }
        finally
        {
            _statusRefreshInFlight = false;
        }
    }

    private void RebuildPreview()
    {
        if (_statusPollId != 0)
        {
            GLibFunctions.SourceRemove(_statusPollId);
            _statusPollId = 0;
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
        _src = null;
        _sink = null;

        string socketPath = _previewSocketPath ?? "/tmp/opendslm-preview.sock";

        var pipeline = Pipeline.New("daemon-preview");
        _src = ElementFactory.Make("shmsrc", "daemon-src") ?? throw new Exception("Failed to create shmsrc");
        SetPropString(_src, "socket-path", socketPath);
        SetPropBool(_src, "is-live", true);
        SetPropBool(_src, "do-timestamp", true);

        var queue = ElementFactory.Make("queue", "daemon-queue") ?? throw new Exception("Failed to create queue");
        SetPropInt(queue, "max-size-buffers", 2);
        SetPropInt(queue, "leaky", 2);

        var convert = ElementFactory.Make("videoconvert", "daemon-convert") ?? throw new Exception("Failed to create videoconvert");

        _sink = ElementFactory.Make("gtk4paintablesink", "daemon-sink") ?? throw new Exception("gtk4paintablesink not available");
        SetPropBool(_sink, "sync", false);

        pipeline.Add(_src);
        pipeline.Add(queue);
        pipeline.Add(convert);
        pipeline.Add(_sink);

        if (!_src.Link(queue)) throw new Exception("Link src->queue failed");
        string capsString = _previewCaps ?? "video/x-raw,format=RGBA,width=1920,height=1080,framerate=24000/1000";
        using (var caps = Caps.FromString(capsString))
        {
            if (!queue.LinkFiltered(convert, caps)) throw new Exception("Link queue->convert (caps) failed");
        }

        if (!convert.Link(_sink)) throw new Exception("Link convert->sink failed");

        _pipeline = pipeline;

        UpdateZoomInfrastructureFlag(false);
        RebindPaintable();

        if (_pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
        {
            throw new Exception("Preview pipeline failed to start");
        }

        GLibFunctions.IdleAdd(0, () =>
        {
            RebindPaintable();
            return false;
        });

        EnsureStatusPolling();
    }

    private async Task CaptureStillViaDaemonAsync()
    {
        try
        {
            var result = await _daemonClient.CaptureStillAsync().ConfigureAwait(false);
            if (result != null && result.Count > 0)
            {
                Console.WriteLine($"Captured {result.Count} frame(s): {string.Join(", ", result.Frames)}");
                ProcessLastCapture(result);
                UpdateHudFromCachedSettings();
            }
            await RefreshDaemonStatusAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to trigger still capture: {ex.Message}");
        }
    }

    private void EnsureStatusPolling()
    {
        if (_statusPollId != 0) return;
        _statusPollId = GLibFunctions.TimeoutAdd(0, StatusPollIntervalMs, () =>
        {
            _ = RefreshDaemonStatusAsync();
            return true;
        });
    }

    private void ApplySettingsToState(DaemonSettings settings)
    {
        if (_state.AutoExposureEnabled != settings.AutoExposure)
        {
            _state.AutoExposureEnabled = settings.AutoExposure;
        }

        int approxIso = (int)Math.Round(Math.Max(settings.AnalogueGain, 1.0) * 100.0);
        int isoIndex = FindClosestIndex(CameraPresets.IsoSteps, approxIso);
        if (_state.IsoIndex != isoIndex)
        {
            _state.IsoIndex = isoIndex;
        }

        int shutterIndex = FindClosestIndex(CameraPresets.ShutterSteps, settings.ShutterUs / 1_000_000.0);
        if (_state.ShutterIndex != shutterIndex)
        {
            _state.ShutterIndex = shutterIndex;
        }

        if (!string.IsNullOrWhiteSpace(settings.Mode))
        {
            int modeIndex = FindModeIndex(settings.Mode);
            if (modeIndex >= 0 && _state.ResolutionIndex != modeIndex)
            {
                _state.ResolutionIndex = modeIndex;
            }
        }
    }

    private static int FindClosestIndex(int[] values, int target)
    {
        int best = 0;
        int bestDelta = int.MaxValue;
        for (int i = 0; i < values.Length; i++)
        {
            int delta = Math.Abs(values[i] - target);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = i;
            }
        }
        return best;
    }

    private static int FindClosestIndex(double[] values, double target)
    {
        int best = 0;
        double bestDelta = double.MaxValue;
        for (int i = 0; i < values.Length; i++)
        {
            double delta = Math.Abs(values[i] - target);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = i;
            }
        }
        return best;
    }

    private static int FindModeIndex(string mode)
    {
        for (int i = 0; i < CameraPresets.ResolutionOptions.Length; i++)
        {
            if (string.Equals(CameraPresets.ResolutionOptions[i].Mode, mode, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private static (string? SocketPath, string? Caps) ExtractPreviewInfo(string pipelineDescription)
    {
        string? socket = null;
        string? caps = null;

        var segments = pipelineDescription.Split('!');
        foreach (var rawSegment in segments)
        {
            string segment = rawSegment.Trim();
            if (segment.StartsWith("shmsrc", StringComparison.OrdinalIgnoreCase))
            {
                const string key = "socket-path";
                int keyIndex = segment.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                if (keyIndex >= 0)
                {
                    int quoteStart = segment.IndexOf('"', keyIndex);
                    if (quoteStart >= 0 && quoteStart + 1 < segment.Length)
                    {
                        int quoteEnd = segment.IndexOf('"', quoteStart + 1);
                        if (quoteEnd > quoteStart)
                        {
                            socket = segment.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                        }
                    }
                }
            }
            else if (segment.StartsWith("video/x-raw", StringComparison.OrdinalIgnoreCase))
            {
                caps = segment;
            }
        }

        return (socket, caps);
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

    private void UpdateZoomInfrastructureFlag(bool newValue)
    {
        if (_supportsZoomCropping == newValue) return;
        _supportsZoomCropping = newValue;
        ZoomInfrastructureChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateHudFromCachedSettings()
    {
        if (_hud == null) return;
        if (_lastSettings == null)
        {
            _hud.SetText("Live: —\nLast: —");
            return;
        }

        string live = FormatLiveHud(_lastSettings);
        string last = FormatLastHud();
        _hud.SetText($"{live}\n{last}");
    }

    private void ProcessLastCapture(DaemonCaptureResult capture)
    {
        string token = $"{capture.Count}:{string.Join(',', capture.Frames)}";
        if (token == _lastCaptureToken)
        {
            return;
        }

        _lastCaptureToken = token;

        if (_lastSettings == null)
        {
            return;
        }

        long? expUs = _lastSettings.AutoExposure ? null : (long?)Math.Round(_lastSettings.ShutterUs);
        int? iso = _lastSettings.AutoExposure ? null : (int?)Math.Round(Math.Max(_lastSettings.AnalogueGain, 1.0) * 100.0);
        double? ag = _lastSettings.AutoExposure ? null : _lastSettings.AnalogueGain;

        _state.UpdateLastCapture(expUs, iso, ag);
    }

    private string FormatLiveHud(DaemonSettings settings)
    {
        double shutterUs = settings.ShutterUs;
        string shutter = shutterUs > 0 ? FormatShutter(shutterUs / 1_000_000.0) : "auto";
        string gain = settings.AutoExposure ? "auto" : settings.AnalogueGain.ToString("0.##", CultureInfo.InvariantCulture);
        return $"Live: t={shutter}  AG={gain}  FPS={settings.Fps:0.#}";
    }

    private string FormatLastHud()
    {
        if (_state.LastExposureMicroseconds.HasValue || _state.LastIso.HasValue || _state.LastAnalogueGain.HasValue)
        {
            string t = _state.LastExposureMicroseconds.HasValue ? FormatShutter(_state.LastExposureMicroseconds.Value / 1_000_000.0) : "auto";
            string iso = _state.LastIso.HasValue ? _state.LastIso.Value.ToString(CultureInfo.InvariantCulture) : "auto";
            string ag = _state.LastAnalogueGain.HasValue ? _state.LastAnalogueGain.Value.ToString("0.##", CultureInfo.InvariantCulture) : "auto";
            return $"Last: t={t}  ISO={iso}  AG={ag}";
        }

        return "Last: —";
    }

    private static string FormatShutter(double seconds)
    {
        if (seconds <= 0) seconds = 0.0001;
        double denom = 1.0 / seconds;
        if (denom >= 1.0)
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

    private static void SetPropString(Element? element, string name, string value)
    {
        if (element is null) return;
        Value v = new();
        v.Init(GObject.Type.String);
        v.SetString(value);
        element.SetProperty(name, v);
        v.Unset();
    }

    private static void SetPropBool(Element? element, string name, bool value)
    {
        if (element is null) return;
        Value v = new();
        v.Init(GObject.Type.Boolean);
        v.SetBoolean(value);
        element.SetProperty(name, v);
        v.Unset();
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

    public static string ShutterLabel(double seconds)
    {
        return FormatShutter(seconds);
    }
}
