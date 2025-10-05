using System;
using System.Globalization;
using System.IO;
using Gtk;

public sealed class MainWindowBuilder
{
    private const string MainLayoutFileName = "camera_main_window.ui";
    private const string LiveLayoutFileName = "camera_live_view.ui";
    private const string SettingsLayoutFileName = "camera_settings_page.ui";
    private const string GalleryLayoutFileName = "camera_gallery_page.ui";
    private const string PhotoControlsLayoutFileName = "mode_photo_controls.ui";
    private const string VideoControlsLayoutFileName = "mode_video_controls.ui";
    private const string TimelapseControlsLayoutFileName = "mode_timelapse_controls.ui";

    private readonly CameraState _state;
    private readonly ActionDispatcher _dispatcher;
    private readonly CameraController _controller;

    private bool _suppressAutoToggle;
    private bool _suppressIsoChange;
    private bool _suppressShutterChange;
    private bool _suppressResolutionChange;
    private bool _suppressZoomChange;
    private bool _suppressPanChange;

    public MainWindowBuilder(CameraState state, ActionDispatcher dispatcher, CameraController controller)
    {
        _state = state;
        _dispatcher = dispatcher;
        _controller = controller;
    }

    public CameraWindow Build(Gtk.Application app)
    {
        using var mainBuilder = Builder.NewFromFile(ResolveLayoutPath(MainLayoutFileName));

        var window = Require<ApplicationWindow>(mainBuilder, "main_window");
        window.SetApplication(app);
        var stack = Require<Stack>(mainBuilder, "page_stack");

        using var liveBuilder = Builder.NewFromFile(ResolveLayoutPath(LiveLayoutFileName));
        var liveOverlay = Require<Overlay>(liveBuilder, "live_overlay");
        var picture = Require<Picture>(liveBuilder, "live_picture");
        var hud = Require<Label>(liveBuilder, "hud_label");
        var settingsButton = Require<Button>(liveBuilder, "settings_button");
        var galleryButton = Require<Button>(liveBuilder, "gallery_button");
        var modeMenuToggle = Require<ToggleButton>(liveBuilder, "mode_menu_toggle");
        var modeRevealer = Require<Revealer>(liveBuilder, "mode_revealer");
        var modePhotoToggle = Require<ToggleButton>(liveBuilder, "mode_photo_toggle");
        var modeVideoToggle = Require<ToggleButton>(liveBuilder, "mode_video_toggle");
        var modeTimelapseToggle = Require<ToggleButton>(liveBuilder, "mode_timelapse_toggle");
        var modeStack = Require<Stack>(liveBuilder, "mode_stack");

        using var photoBuilder = Builder.NewFromFile(ResolveLayoutPath(PhotoControlsLayoutFileName));
        var photoRoot = Require<Box>(photoBuilder, "photo_controls_box");
        var autoToggle = Require<CheckButton>(photoBuilder, "auto_toggle");
        var isoBox = Require<ComboBoxText>(photoBuilder, "iso_combo");
        var shutterBox = Require<ComboBoxText>(photoBuilder, "shutter_combo");
        var zoomScale = Require<Scale>(photoBuilder, "zoom_scale");
        var panXScale = Require<Scale>(photoBuilder, "pan_x_scale");
        var panYScale = Require<Scale>(photoBuilder, "pan_y_scale");
        var captureButton = Require<Button>(photoBuilder, "capture_button");

        using var videoBuilder = Builder.NewFromFile(ResolveLayoutPath(VideoControlsLayoutFileName));
        var videoRoot = Require<Box>(videoBuilder, "video_controls_box");
        var videoFpsCombo = Require<ComboBoxText>(videoBuilder, "video_fps_combo");
        var videoShutterAngleSpin = Require<SpinButton>(videoBuilder, "video_shutter_angle_spin");
        var videoRecordButton = Require<Button>(videoBuilder, "video_record_button");
        var videoStatusLabel = Require<Label>(videoBuilder, "video_status_label");

        using var timelapseBuilder = Builder.NewFromFile(ResolveLayoutPath(TimelapseControlsLayoutFileName));
        var timelapseRoot = Require<Box>(timelapseBuilder, "timelapse_controls_box");
        var timelapseIntervalSpin = Require<SpinButton>(timelapseBuilder, "timelapse_interval_spin");
        var timelapseFrameCountSpin = Require<SpinButton>(timelapseBuilder, "timelapse_frame_count_spin");
        var timelapseStartButton = Require<Button>(timelapseBuilder, "timelapse_start_button");
        var timelapseStatusLabel = Require<Label>(timelapseBuilder, "timelapse_status_label");

        modeStack.AddNamed(photoRoot, "photo");
        modeStack.AddNamed(videoRoot, "video");
        modeStack.AddNamed(timelapseRoot, "timelapse");

        var photoView = new PhotoControlsView(photoRoot, autoToggle, isoBox, shutterBox, zoomScale, panXScale, panYScale, captureButton);
        var videoView = new VideoControlsView(videoRoot, videoFpsCombo, videoShutterAngleSpin, videoRecordButton, videoStatusLabel);
        var timelapseView = new TimelapseControlsView(timelapseRoot, timelapseIntervalSpin, timelapseFrameCountSpin, timelapseStartButton, timelapseStatusLabel);

        using var settingsBuilder = Builder.NewFromFile(ResolveLayoutPath(SettingsLayoutFileName));
        var settingsRoot = Require<Box>(settingsBuilder, "settings_root");
        var settingsCloseButton = Require<Button>(settingsBuilder, "settings_close_button");
        var settingsResolutionCombo = Require<ComboBoxText>(settingsBuilder, "settings_resolution_combo");
        var outputDirEntry = Require<Entry>(settingsBuilder, "output_dir_entry");
        var outputDirApplyButton = Require<Button>(settingsBuilder, "output_dir_apply_button");
        var galleryColorToggle = Require<CheckButton>(settingsBuilder, "gallery_color_toggle");
        var galleryPageSizeSpin = Require<SpinButton>(settingsBuilder, "gallery_page_size_spin");

        var settingsView = new CameraSettingsView(
            settingsRoot,
            settingsCloseButton,
            settingsResolutionCombo,
            outputDirEntry,
            outputDirApplyButton,
            galleryColorToggle,
            galleryPageSizeSpin);

        using var galleryBuilder = Builder.NewFromFile(ResolveLayoutPath(GalleryLayoutFileName));
        var galleryRoot = Require<Box>(galleryBuilder, "gallery_root");
        var galleryBackButton = Require<Button>(galleryBuilder, "gallery_back_button");
        var galleryStack = Require<Stack>(galleryBuilder, "gallery_stack");
        var galleryEmptyBox = Require<Box>(galleryBuilder, "gallery_empty_box");
        var galleryScroller = Require<ScrolledWindow>(galleryBuilder, "gallery_scroller");
        var galleryFlow = Require<FlowBox>(galleryBuilder, "gallery_flow");
        var galleryViewerBox = Require<Box>(galleryBuilder, "gallery_viewer_box");
        var galleryViewerBackButton = Require<Button>(galleryBuilder, "gallery_viewer_back_button");
        var galleryPrevButton = Require<Button>(galleryBuilder, "gallery_prev_button");
        var galleryNextButton = Require<Button>(galleryBuilder, "gallery_next_button");
        var galleryPageLabel = Require<Label>(galleryBuilder, "gallery_page_label");
        var galleryFullPicture = Require<Picture>(galleryBuilder, "gallery_full_picture");
        var galleryFullLabel = Require<Label>(galleryBuilder, "gallery_full_label");

        var galleryView = new GalleryView(
            galleryRoot,
            galleryBackButton,
            galleryStack,
            galleryEmptyBox,
            galleryScroller,
            galleryFlow,
            galleryViewerBox,
            galleryViewerBackButton,
            galleryPrevButton,
            galleryNextButton,
            galleryPageLabel,
            galleryFullPicture,
            galleryFullLabel);

        ConfigureAutoToggle(photoView.AutoToggle);
        ConfigureIsoCombo(photoView.IsoBox);
        ConfigureShutterCombo(photoView.ShutterBox);
        ConfigureZoomControls(photoView.ZoomScale, photoView.PanXScale, photoView.PanYScale);
        ConfigureCaptureButton(photoView.CaptureButton);
        ConfigureResolutionCombo(settingsView.ResolutionCombo);
        ConfigureSettingsPanel(settingsView);
        ConfigureGallerySettings(settingsView);
        ConfigureModeControls(modeMenuToggle, modeRevealer, modePhotoToggle, modeVideoToggle, modeTimelapseToggle, modeStack, photoView);
        ConfigureVideoControls(videoView);
        ConfigureTimelapseControls(timelapseView);
        ConfigureNavigation(stack, liveOverlay, settingsView.Root, galleryView.Root, settingsButton, settingsView.CloseButton, galleryButton, galleryView);

        StyleInstaller.TryInstall();

        BindStateToControls(photoView, settingsView.ResolutionCombo, settingsView);
        BindGallery(galleryView);

        return new CameraWindow(
            window,
            picture,
            hud,
            settingsButton,
            galleryButton,
            modeMenuToggle,
            modePhotoToggle,
            modeVideoToggle,
            modeTimelapseToggle,
            modeRevealer,
            modeStack,
            photoView,
            videoView,
            timelapseView,
            stack,
            liveOverlay,
            settingsView,
            galleryView.Root,
            galleryView);
    }

    private static T Require<T>(Builder builder, string id) where T : class
    {
        if (builder.GetObject(id) is T instance)
        {
            return instance;
        }

        throw new InvalidOperationException($"The UI template is missing an object with id '{id}' of type {typeof(T).Name}.");
    }

    private static string ResolveLayoutPath(string fileName)
    {
        string baseDir = AppContext.BaseDirectory ?? string.Empty;
        string candidate = Path.Combine(baseDir, "Resources", "ui", fileName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        string fallback = Path.Combine(Environment.CurrentDirectory, "Resources", "ui", fileName);
        if (File.Exists(fallback))
        {
            return fallback;
        }

        throw new FileNotFoundException($"Unable to locate {fileName}. Ensure it is copied alongside the application binaries.");
    }

    private void ConfigureAutoToggle(CheckButton autoToggle)
    {
        autoToggle.Active = _state.AutoExposureEnabled;
        autoToggle.OnToggled += (_, __) =>
        {
            if (_suppressAutoToggle) return;
            _dispatcher.FireAndForget(AppActionId.ToggleAutoExposure, new ToggleAutoExposurePayload(autoToggle.Active));
        };
    }

    private void ConfigureResolutionCombo(ComboBoxText resBox)
    {
        resBox.RemoveAll();
        for (int i = 0; i < CameraPresets.ResolutionOptions.Length; i++)
        {
            resBox.AppendText(CameraPresets.ResolutionOptions[i].Label);
        }
        resBox.Active = _state.ResolutionIndex;

        bool updating = false;
        resBox.OnChanged += async (_, __) =>
        {
            if (updating || _suppressResolutionChange) return;
            updating = true;
            try
            {
                await _dispatcher.DispatchAsync(AppActionId.SelectResolution, new SelectIndexPayload(resBox.Active));
            }
            finally
            {
                updating = false;
            }
        };
    }

    private void ConfigureIsoCombo(ComboBoxText isoBox)
    {
        isoBox.RemoveAll();
        foreach (var iso in CameraPresets.IsoSteps)
        {
            isoBox.AppendText(iso.ToString());
        }
        isoBox.Active = _state.IsoIndex;
        isoBox.Sensitive = !_state.AutoExposureEnabled;

        bool updating = false;
        isoBox.OnChanged += async (_, __) =>
        {
            if (updating || _suppressIsoChange) return;
            if (_state.AutoExposureEnabled) return;
            updating = true;
            try
            {
                await _dispatcher.DispatchAsync(AppActionId.SelectIso, new SelectIndexPayload(isoBox.Active));
            }
            finally
            {
                updating = false;
            }
        };
    }

    private void ConfigureShutterCombo(ComboBoxText shutterBox)
    {
        shutterBox.RemoveAll();
        foreach (var sec in CameraPresets.ShutterSteps)
        {
            shutterBox.AppendText(CameraController.ShutterLabel(sec));
        }
        shutterBox.Active = _state.ShutterIndex;
        shutterBox.Sensitive = !_state.AutoExposureEnabled;

        bool updating = false;
        shutterBox.OnChanged += async (_, __) =>
        {
            if (updating || _suppressShutterChange) return;
            if (_state.AutoExposureEnabled) return;
            updating = true;
            try
            {
                await _dispatcher.DispatchAsync(AppActionId.SelectShutter, new SelectIndexPayload(shutterBox.Active));
            }
            finally
            {
                updating = false;
            }
        };
    }

    private void ConfigureZoomControls(Scale zoomScale, Scale panXScale, Scale panYScale)
    {
        ((Gtk.Range)zoomScale).SetValue(_state.Zoom);
        ((Gtk.Range)panXScale).SetValue(_state.PanX);
        ((Gtk.Range)panYScale).SetValue(_state.PanY);

        zoomScale.OnValueChanged += (_, __) =>
        {
            if (_suppressZoomChange) return;
            double value = ((Gtk.Range)zoomScale).GetValue();
            _dispatcher.FireAndForget(AppActionId.AdjustZoom, new AdjustZoomPayload(value));
        };

        panXScale.OnValueChanged += (_, __) =>
        {
            if (_suppressPanChange) return;
            double x = ((Gtk.Range)panXScale).GetValue();
            double y = ((Gtk.Range)panYScale).GetValue();
            _dispatcher.FireAndForget(AppActionId.AdjustPan, new AdjustPanPayload(x, y));
        };

        panYScale.OnValueChanged += (_, __) =>
        {
            if (_suppressPanChange) return;
            double x = ((Gtk.Range)panXScale).GetValue();
            double y = ((Gtk.Range)panYScale).GetValue();
            _dispatcher.FireAndForget(AppActionId.AdjustPan, new AdjustPanPayload(x, y));
        };
    }

    private void ConfigureCaptureButton(Button captureButton)
    {
        captureButton.OnClicked += async (_, __) =>
        {
            captureButton.Sensitive = false;
            try
            {
                await _dispatcher.DispatchAsync(AppActionId.CaptureStill);
            }
            finally
            {
                captureButton.Sensitive = true;
            }
        };
    }

    private void ConfigureSettingsPanel(CameraSettingsView settingsView)
    {
        SetEntryText(settingsView.OutputDirectoryEntry, _state.OutputDirectory);

        async System.Threading.Tasks.Task CommitAsync()
        {
            string path = GetEntryText(settingsView.OutputDirectoryEntry).Trim();
            if (string.Equals(path, _state.OutputDirectory, StringComparison.Ordinal))
                return;

            try
            {
                await _dispatcher.DispatchAsync(AppActionId.UpdateOutputDirectory,
                    new UpdateOutputDirectoryPayload(path));
            }
            finally
            {
                // Refresh entry in case the daemon adjusted the value.
                SetEntryText(settingsView.OutputDirectoryEntry, _state.OutputDirectory);
            }
        }

        settingsView.OutputDirectoryEntry.OnActivate += async (_, __) => await CommitAsync();
        settingsView.OutputDirectoryApplyButton.OnClicked += async (_, __) => await CommitAsync();
    }

    private void ConfigureGallerySettings(CameraSettingsView settingsView)
    {
        bool suppressToggle = false;
        bool suppressPageSize = false;

        settingsView.GalleryColorToggle.Active = _state.GalleryColorEnabled;
        settingsView.GalleryPageSizeSpin.Adjustment.Lower = 1;
        settingsView.GalleryPageSizeSpin.Adjustment.Upper = 48;
        settingsView.GalleryPageSizeSpin.Value = _state.GalleryPageSize;

        settingsView.GalleryColorToggle.OnToggled += (_, __) =>
        {
            if (suppressToggle) return;
            bool enabled = settingsView.GalleryColorToggle.Active;
            _dispatcher.FireAndForget(AppActionId.SetGalleryColorEnabled, new SetGalleryColorEnabledPayload(enabled));
        };

        settingsView.GalleryPageSizeSpin.OnValueChanged += (_, __) =>
        {
            if (suppressPageSize) return;
            int requested = (int)Math.Max(1, Math.Round(settingsView.GalleryPageSizeSpin.Value));
            _dispatcher.FireAndForget(AppActionId.SetGalleryPageSize, new SetGalleryPageSizePayload(requested));
        };

        _state.GalleryColorEnabledChanged += (_, enabled) =>
        {
            suppressToggle = true;
            settingsView.GalleryColorToggle.Active = enabled;
            suppressToggle = false;
        };

        _state.GalleryPageSizeChanged += (_, size) =>
        {
            suppressPageSize = true;
            settingsView.GalleryPageSizeSpin.Value = size;
            suppressPageSize = false;
        };
    }

    private void ConfigureModeControls(ToggleButton modeMenuToggle, Revealer modeRevealer, ToggleButton photoToggle, ToggleButton videoToggle, ToggleButton timelapseToggle, Stack modeStack, PhotoControlsView photoControls)
    {
        if (modeMenuToggle is null || modeRevealer is null || photoToggle is null || videoToggle is null || timelapseToggle is null || modeStack is null || photoControls is null)
        {
            return;
        }

        bool suppress = false;
        bool menuSuppress = false;

        modeMenuToggle.Active = false;
        modeRevealer.SetRevealChild(false);

        modeMenuToggle.OnToggled += (_, __) =>
        {
            if (menuSuppress) return;
            modeRevealer.SetRevealChild(modeMenuToggle.Active);
        };

        void CloseModeMenu()
        {
            menuSuppress = true;
            modeMenuToggle.Active = false;
            menuSuppress = false;
            modeRevealer.SetRevealChild(false);
        }

        void UpdateVisualState(CaptureMode mode)
        {
            suppress = true;
            photoToggle.Active = mode == CaptureMode.Photo;
            videoToggle.Active = mode == CaptureMode.Video;
            timelapseToggle.Active = mode == CaptureMode.Timelapse;
            suppress = false;

            modeStack.SetVisibleChildName(mode switch
            {
                CaptureMode.Photo => "photo",
                CaptureMode.Video => "video",
                CaptureMode.Timelapse => "timelapse",
                _ => "photo"
            });

            if (photoControls.CaptureButton is not null)
            {
                photoControls.CaptureButton.Sensitive = mode == CaptureMode.Photo;
            }

            CloseModeMenu();
        }

        photoToggle.OnToggled += (_, __) =>
        {
            if (suppress) return;
            if (photoToggle.Active)
            {
                _dispatcher.FireAndForget(AppActionId.SetCaptureMode, new SetCaptureModePayload(CaptureMode.Photo));
            }
        };

        videoToggle.OnToggled += (_, __) =>
        {
            if (suppress) return;
            if (videoToggle.Active)
            {
                _dispatcher.FireAndForget(AppActionId.SetCaptureMode, new SetCaptureModePayload(CaptureMode.Video));
            }
        };

        timelapseToggle.OnToggled += (_, __) =>
        {
            if (suppress) return;
            if (timelapseToggle.Active)
            {
                _dispatcher.FireAndForget(AppActionId.SetCaptureMode, new SetCaptureModePayload(CaptureMode.Timelapse));
            }
        };

        _state.CaptureModeChanged += (_, mode) => UpdateVisualState(mode);
        UpdateVisualState(_state.CaptureMode);
    }

    private void ConfigureVideoControls(VideoControlsView view)
    {
        if (view is null)
        {
            return;
        }

        var fpsCombo = view.FpsCombo;
        var shutterAngleSpin = view.ShutterAngleSpin;
        var recordButton = view.RecordButton;
        var statusLabel = view.StatusLabel;

        if (fpsCombo is null || shutterAngleSpin is null || recordButton is null || statusLabel is null)
        {
            return;
        }

        fpsCombo.RemoveAll();
        for (int i = 0; i < CameraPresets.VideoFpsOptions.Length; i++)
        {
            double fps = CameraPresets.VideoFpsOptions[i];
            string label = fps >= 100 ? fps.ToString("0.##", CultureInfo.InvariantCulture) : fps.ToString("0.###", CultureInfo.InvariantCulture);
            fpsCombo.AppendText(label);
        }

        bool suppress = false;

        void UpdateFpsSelection()
        {
            suppress = true;
            string targetId = _state.VideoFps.ToString("0.###", CultureInfo.InvariantCulture);
            int match = -1;
            for (int i = 0; i < CameraPresets.VideoFpsOptions.Length; i++)
            {
                if (Math.Abs(CameraPresets.VideoFpsOptions[i] - _state.VideoFps) < 0.001)
                {
                    match = i;
                    break;
                }
            }
            fpsCombo.Active = match >= 0 ? match : 0;
            suppress = false;
        }

        shutterAngleSpin.Value = _state.VideoShutterAngle;

        fpsCombo.OnChanged += (_, __) =>
        {
            if (suppress) return;
            double fps = _state.VideoFps;
            int index = fpsCombo.Active;
            if (index >= 0 && index < CameraPresets.VideoFpsOptions.Length)
            {
                fps = CameraPresets.VideoFpsOptions[index];
            }
            else if (!string.IsNullOrEmpty(fpsCombo.GetActiveText()) && double.TryParse(fpsCombo.GetActiveText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var textVal))
            {
                fps = textVal;
            }

            _dispatcher.FireAndForget(AppActionId.UpdateVideoSettings, new UpdateVideoSettingsPayload(fps, _state.VideoShutterAngle));
        };

        shutterAngleSpin.OnValueChanged += (_, __) =>
        {
            if (suppress) return;
            double fps = _state.VideoFps;
            double angle = shutterAngleSpin.Value;
            _dispatcher.FireAndForget(AppActionId.UpdateVideoSettings, new UpdateVideoSettingsPayload(fps, angle));
        };

        void UpdateRecordingUi()
        {
            recordButton.Label = _state.IsVideoRecording ? "■ Stop Recording" : "● Start Recording";
            bool isVideoMode = _state.CaptureMode == CaptureMode.Video;
            recordButton.Sensitive = isVideoMode;
            bool settingsEnabled = isVideoMode && !_state.IsVideoRecording;
            fpsCombo.Sensitive = settingsEnabled;
            shutterAngleSpin.Sensitive = settingsEnabled;
            if (_state.IsVideoRecording)
            {
                var metrics = _state.CurrentVideoRecordingMetrics;
                string sequencePath = _state.ActiveVideoSequencePath ?? string.Empty;
                string sequenceName = string.IsNullOrEmpty(sequencePath) ? string.Empty : Path.GetFileName(sequencePath);
                string header = string.IsNullOrEmpty(sequenceName) ? "Recording…" : $"Recording… {sequenceName}";
                string metricsText;
                if (metrics.CapturedFrames > 0)
                {
                    metricsText = $"{metrics.CapturedFrames} frames · {metrics.ActualFps:0.0} fps actual / {metrics.TargetFps:0.0} fps target";
                    if (metrics.DroppedFrames > 0)
                    {
                        metricsText += $" · dropped {metrics.DroppedFrames}";
                    }
                }
                else
                {
                    metricsText = $"Target {metrics.TargetFps:0.0} fps";
                }

                statusLabel.SetText($"{header}\n{metricsText}");
            }
            else
            {
                statusLabel.SetText("Idle");
            }
        }

        recordButton.OnClicked += (_, __) =>
        {
            if (_state.IsVideoRecording)
            {
                _dispatcher.FireAndForget(AppActionId.StopVideoRecording);
            }
            else
            {
                _dispatcher.FireAndForget(AppActionId.StartVideoRecording);
            }
        };

        _state.VideoSettingsChanged += (_, __) =>
        {
            suppress = true;
            UpdateFpsSelection();
            shutterAngleSpin.Value = _state.VideoShutterAngle;
            suppress = false;
        };

        _state.VideoRecordingChanged += (_, __) => UpdateRecordingUi();
        _state.VideoRecordingMetricsChanged += (_, __) => UpdateRecordingUi();
        _state.CaptureModeChanged += (_, __) => UpdateRecordingUi();

        UpdateFpsSelection();
        UpdateRecordingUi();
    }

    private void ConfigureTimelapseControls(TimelapseControlsView view)
    {
        if (view is null)
        {
            return;
        }

        var intervalSpin = view.IntervalSpin;
        var frameCountSpin = view.FrameCountSpin;
        var startButton = view.StartButton;
        var statusLabel = view.StatusLabel;

        if (intervalSpin is null || frameCountSpin is null || startButton is null || statusLabel is null)
        {
            return;
        }

        bool suppress = false;

        intervalSpin.Value = _state.TimelapseIntervalSeconds;
        frameCountSpin.Value = _state.TimelapseFrameCount;

        intervalSpin.OnValueChanged += (_, __) =>
        {
            if (suppress) return;
            double interval = Math.Max(0.5, intervalSpin.Value);
            _dispatcher.FireAndForget(AppActionId.UpdateTimelapseSettings, new UpdateTimelapseSettingsPayload(interval, _state.TimelapseFrameCount));
        };

        frameCountSpin.OnValueChanged += (_, __) =>
        {
            if (suppress) return;
            int frames = Math.Max(1, (int)Math.Round(frameCountSpin.Value));
            _dispatcher.FireAndForget(AppActionId.UpdateTimelapseSettings, new UpdateTimelapseSettingsPayload(_state.TimelapseIntervalSeconds, frames));
        };

        startButton.OnClicked += (_, __) =>
        {
            if (_state.TimelapseActive)
            {
                _dispatcher.FireAndForget(AppActionId.StopTimelapse);
            }
            else
            {
                _dispatcher.FireAndForget(AppActionId.StartTimelapse);
            }
        };

        void UpdateTimelapseUi()
        {
            bool isTimelapseMode = _state.CaptureMode == CaptureMode.Timelapse;
            startButton.Label = _state.TimelapseActive ? "■ Stop Timelapse" : "▶ Start Timelapse";
            startButton.Sensitive = isTimelapseMode;
            bool settingsEnabled = isTimelapseMode && !_state.TimelapseActive;
            intervalSpin.Sensitive = settingsEnabled;
            frameCountSpin.Sensitive = settingsEnabled;
            statusLabel.SetText(_state.TimelapseActive
                ? ($"Capturing… {_state.ActiveTimelapsePath ?? string.Empty}")
                : "Idle");
        }

        _state.TimelapseSettingsChanged += (_, __) =>
        {
            suppress = true;
            intervalSpin.Value = _state.TimelapseIntervalSeconds;
            frameCountSpin.Value = _state.TimelapseFrameCount;
            suppress = false;
        };

        _state.TimelapseActiveChanged += (_, __) => UpdateTimelapseUi();
        _state.CaptureModeChanged += (_, __) => UpdateTimelapseUi();

        UpdateTimelapseUi();
    }

    private void ConfigureNavigation(
        Stack stack,
        Widget livePage,
        Widget settingsPage,
        Widget galleryPage,
        Button settingsButton,
        Button settingsCloseButton,
        Button galleryButton,
        GalleryView galleryView)
    {
        livePage.SetName("live-view");
        settingsPage.SetName("settings-view");
        galleryPage.SetName("gallery-view");

        stack.AddChild(livePage);
        stack.AddChild(settingsPage);
        stack.AddChild(galleryPage);

        void ShowLive()
        {
            stack.SetVisibleChild(livePage);
            settingsButton.Sensitive = true;
            galleryButton.Sensitive = true;
            galleryView.EnsureGridVisible();
        }

        ShowLive();

        settingsButton.OnClicked += (_, __) =>
        {
            if (!settingsButton.Sensitive) return;
            stack.SetVisibleChild(settingsPage);
            settingsButton.Sensitive = false;
            galleryButton.Sensitive = true;
        };

        settingsCloseButton.OnClicked += (_, __) =>
        {
            ShowLive();
        };

        galleryButton.OnClicked += (_, __) =>
        {
            if (!galleryButton.Sensitive) return;
            _dispatcher.FireAndForget(AppActionId.SetGalleryPage, new SetGalleryPagePayload(0));
            _dispatcher.FireAndForget(AppActionId.LoadGallery);
            stack.SetVisibleChild(galleryPage);
            galleryButton.Sensitive = false;
            settingsButton.Sensitive = true;
            galleryView.EnsureGridVisible();
        };

        galleryView.BackRequested += (_, __) =>
        {
            ShowLive();
        };
    }

    private void BindStateToControls(PhotoControlsView photoControls, ComboBoxText resBox, CameraSettingsView settingsView)
    {
        if (photoControls is null)
        {
            return;
        }

        var autoToggle = photoControls.AutoToggle;
        var isoBox = photoControls.IsoBox;
        var shutterBox = photoControls.ShutterBox;
        var zoomScale = photoControls.ZoomScale;
        var panXScale = photoControls.PanXScale;
        var panYScale = photoControls.PanYScale;

        _state.AutoExposureChanged += (_, enabled) =>
        {
            _suppressAutoToggle = true;
            try
            {
                if (autoToggle.Active != enabled)
                {
                    autoToggle.Active = enabled;
                }
            }
            finally
            {
                _suppressAutoToggle = false;
            }
            isoBox.Sensitive = !enabled;
            shutterBox.Sensitive = !enabled;
        };

        _state.IsoIndexChanged += (_, index) =>
        {
            _suppressIsoChange = true;
            try
            {
                if (isoBox.Active != index)
                {
                    isoBox.Active = index;
                }
            }
            finally
            {
                _suppressIsoChange = false;
            }
        };

        _state.ShutterIndexChanged += (_, index) =>
        {
            _suppressShutterChange = true;
            try
            {
                if (shutterBox.Active != index)
                {
                    shutterBox.Active = index;
                }
            }
            finally
            {
                _suppressShutterChange = false;
            }
        };

        _state.ResolutionIndexChanged += (_, index) =>
        {
            _suppressResolutionChange = true;
            try
            {
                if (resBox.Active != index)
                {
                    resBox.Active = index;
                }
            }
            finally
            {
                _suppressResolutionChange = false;
            }
        };

        _state.OutputDirectoryChanged += (_, path) =>
        {
            SetEntryText(settingsView.OutputDirectoryEntry, path ?? string.Empty);
        };

        _state.ZoomChanged += (_, value) =>
        {
            _suppressZoomChange = true;
            try
            {
                if (Math.Abs(((Gtk.Range)zoomScale).GetValue() - value) > 0.0001)
                {
                    ((Gtk.Range)zoomScale).SetValue(value);
                }
            }
            finally
            {
                _suppressZoomChange = false;
            }
            UpdatePanSensitivity(panXScale, panYScale);
        };

        _state.PanChanged += (_, coords) =>
        {
            _suppressPanChange = true;
            try
            {
                if (Math.Abs(((Gtk.Range)panXScale).GetValue() - coords.X) > 0.0001)
                {
                    ((Gtk.Range)panXScale).SetValue(coords.X);
                }
                if (Math.Abs(((Gtk.Range)panYScale).GetValue() - coords.Y) > 0.0001)
                {
                    ((Gtk.Range)panYScale).SetValue(coords.Y);
                }
            }
            finally
            {
                _suppressPanChange = false;
            }
        };

        _controller.ZoomInfrastructureChanged += (_, __) => UpdatePanSensitivity(panXScale, panYScale);
        UpdatePanSensitivity(panXScale, panYScale);
    }

    private void BindGallery(GalleryView galleryView)
    {
        if (galleryView is null)
        {
            throw new ArgumentNullException(nameof(galleryView));
        }

        bool galleryUpdating = false;

        void RefreshGallery()
        {
            if (galleryUpdating) return;
            galleryUpdating = true;
            try
            {
                var items = _state.GetGalleryPageItems();
                galleryView.UpdateItems(items, _state.GalleryColorEnabled);
                int totalPages = _state.GetGalleryPageCount();
                int currentPage = totalPages == 0 ? 0 : Math.Clamp(_state.GalleryPageIndex, 0, Math.Max(0, totalPages - 1));
                galleryView.UpdatePagination(currentPage, totalPages);
            }
            finally
            {
                galleryUpdating = false;
            }
        }

        RefreshGallery();

        _state.RecentCapturesChanged += (_, __) => RefreshGallery();
        _state.GalleryPageIndexChanged += (_, __) => RefreshGallery();
        _state.GalleryColorEnabledChanged += (_, __) => RefreshGallery();
        _state.GalleryPageSizeChanged += (_, __) => RefreshGallery();

        galleryView.PageRequested += (_, delta) =>
        {
            int requested = _state.GalleryPageIndex + delta;
            _dispatcher.FireAndForget(AppActionId.SetGalleryPage, new SetGalleryPagePayload(requested));
        };

        _state.OutputDirectoryChanged += (_, __) =>
        {
            _dispatcher.FireAndForget(AppActionId.LoadGallery);
        };

        _dispatcher.FireAndForget(AppActionId.LoadGallery);
    }

    private void UpdatePanSensitivity(Scale panXScale, Scale panYScale)
    {
        bool enablePan = _controller.SupportsZoomCropping && _state.Zoom > 1.0001;
        panXScale.Sensitive = enablePan;
        panYScale.Sensitive = enablePan;
    }

    private static string GetEntryText(Entry entry)
    {
        return entry.GetText() ?? string.Empty;
    }

    private static void SetEntryText(Entry entry, string text)
    {
        entry.SetText(text ?? string.Empty);
    }
}
