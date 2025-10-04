using System;
using System.IO;
using Gtk;

public sealed class MainWindowBuilder
{
    private const string MainLayoutFileName = "camera_main_window.ui";
    private const string LiveLayoutFileName = "camera_live_view.ui";
    private const string SettingsLayoutFileName = "camera_settings_page.ui";
    private const string GalleryLayoutFileName = "camera_gallery_page.ui";

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
        var autoToggle = Require<CheckButton>(liveBuilder, "auto_toggle");
        var isoBox = Require<ComboBoxText>(liveBuilder, "iso_combo");
        var shutterBox = Require<ComboBoxText>(liveBuilder, "shutter_combo");
        var zoomScale = Require<Scale>(liveBuilder, "zoom_scale");
        var panXScale = Require<Scale>(liveBuilder, "pan_x_scale");
        var panYScale = Require<Scale>(liveBuilder, "pan_y_scale");
        var captureButton = Require<Button>(liveBuilder, "capture_button");
        var settingsButton = Require<Button>(liveBuilder, "settings_button");
        var galleryButton = Require<Button>(liveBuilder, "gallery_button");

        using var settingsBuilder = Builder.NewFromFile(ResolveLayoutPath(SettingsLayoutFileName));
        var settingsRoot = Require<Box>(settingsBuilder, "settings_root");
        var settingsCloseButton = Require<Button>(settingsBuilder, "settings_close_button");
        var settingsResolutionCombo = Require<ComboBoxText>(settingsBuilder, "settings_resolution_combo");
        var outputDirEntry = Require<Entry>(settingsBuilder, "output_dir_entry");
        var outputDirApplyButton = Require<Button>(settingsBuilder, "output_dir_apply_button");

        var settingsView = new CameraSettingsView(
            settingsRoot,
            settingsCloseButton,
            settingsResolutionCombo,
            outputDirEntry,
            outputDirApplyButton);

        using var galleryBuilder = Builder.NewFromFile(ResolveLayoutPath(GalleryLayoutFileName));
        var galleryRoot = Require<Box>(galleryBuilder, "gallery_root");
        var galleryBackButton = Require<Button>(galleryBuilder, "gallery_back_button");
        var galleryStack = Require<Stack>(galleryBuilder, "gallery_stack");
        var galleryEmptyBox = Require<Box>(galleryBuilder, "gallery_empty_box");
        var galleryScroller = Require<ScrolledWindow>(galleryBuilder, "gallery_scroller");
        var galleryFlow = Require<FlowBox>(galleryBuilder, "gallery_flow");
        var galleryViewerBox = Require<Box>(galleryBuilder, "gallery_viewer_box");
        var galleryViewerBackButton = Require<Button>(galleryBuilder, "gallery_viewer_back_button");
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
            galleryFullPicture,
            galleryFullLabel);

        ConfigureAutoToggle(autoToggle);
        ConfigureIsoCombo(isoBox);
        ConfigureShutterCombo(shutterBox);
        ConfigureZoomControls(zoomScale, panXScale, panYScale);
        ConfigureCaptureButton(captureButton);
        ConfigureResolutionCombo(settingsView.ResolutionCombo);
        ConfigureSettingsPanel(settingsView);
        ConfigureNavigation(stack, liveOverlay, settingsView.Root, galleryView.Root, settingsButton, settingsView.CloseButton, galleryButton, galleryView);

        StyleInstaller.TryInstall();

        BindStateToControls(autoToggle, isoBox, shutterBox, settingsView.ResolutionCombo, zoomScale, panXScale, panYScale, settingsView);
        BindGallery(galleryView);

        return new CameraWindow(
            window,
            picture,
            hud,
            isoBox,
            shutterBox,
            autoToggle,
            zoomScale,
            panXScale,
            panYScale,
            captureButton,
            settingsButton,
            galleryButton,
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

    private void BindStateToControls(CheckButton autoToggle, ComboBoxText isoBox, ComboBoxText shutterBox, ComboBoxText resBox, Scale zoomScale, Scale panXScale, Scale panYScale, CameraSettingsView settingsView)
    {
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

        void Update()
        {
            galleryView.UpdateItems(_state.RecentCaptures);
        }

        Update();

        _state.RecentCapturesChanged += (_, __) =>
        {
            Update();
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
