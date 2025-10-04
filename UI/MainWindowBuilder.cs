using System;
using System.IO;
using Gtk;

public sealed class MainWindowBuilder
{
    private const string LayoutFileName = "camera_main_window.ui";

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
        using var builder = Builder.NewFromFile(ResolveLayoutPath());

        var window = Require<ApplicationWindow>(builder, "main_window");
        window.SetApplication(app);

        var picture = Require<Picture>(builder, "live_picture");
        var hud = Require<Label>(builder, "hud_label");
        var autoToggle = Require<CheckButton>(builder, "auto_toggle");
        var isoBox = Require<ComboBoxText>(builder, "iso_combo");
        var shutterBox = Require<ComboBoxText>(builder, "shutter_combo");
        var resolutionBox = Require<ComboBoxText>(builder, "resolution_combo");
        var zoomScale = Require<Scale>(builder, "zoom_scale");
        var panXScale = Require<Scale>(builder, "pan_x_scale");
        var panYScale = Require<Scale>(builder, "pan_y_scale");
        var captureButton = Require<Button>(builder, "capture_button");

        ConfigureAutoToggle(autoToggle);
        ConfigureResolutionCombo(resolutionBox);
        ConfigureIsoCombo(isoBox);
        ConfigureShutterCombo(shutterBox);
        ConfigureZoomControls(zoomScale, panXScale, panYScale);
        ConfigureCaptureButton(captureButton);

        StyleInstaller.TryInstall();

        BindStateToControls(autoToggle, isoBox, shutterBox, resolutionBox, zoomScale, panXScale, panYScale);

        return new CameraWindow(
            window,
            picture,
            hud,
            isoBox,
            shutterBox,
            resolutionBox,
            autoToggle,
            zoomScale,
            panXScale,
            panYScale,
            captureButton);
    }

    private static T Require<T>(Builder builder, string id) where T : class
    {
        if (builder.GetObject(id) is T instance)
        {
            return instance;
        }

        throw new InvalidOperationException($"The UI template is missing an object with id '{id}' of type {typeof(T).Name}.");
    }

    private static string ResolveLayoutPath()
    {
        string baseDir = AppContext.BaseDirectory ?? string.Empty;
        string candidate = Path.Combine(baseDir, "Resources", "ui", LayoutFileName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        string fallback = Path.Combine(Environment.CurrentDirectory, "Resources", "ui", LayoutFileName);
        if (File.Exists(fallback))
        {
            return fallback;
        }

        throw new FileNotFoundException($"Unable to locate {LayoutFileName}. Ensure it is copied alongside the application binaries.");
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

    private void BindStateToControls(CheckButton autoToggle, ComboBoxText isoBox, ComboBoxText shutterBox, ComboBoxText resBox, Scale zoomScale, Scale panXScale, Scale panYScale)
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

    private void UpdatePanSensitivity(Scale panXScale, Scale panYScale)
    {
        bool enablePan = _controller.SupportsZoomCropping && _state.Zoom > 1.0001;
        panXScale.Sensitive = enablePan;
        panYScale.Sensitive = enablePan;
    }
}
