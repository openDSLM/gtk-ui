using System;
using Gtk;

public sealed class MainWindowBuilder
{
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
        var window = ApplicationWindow.New(app);
        window.Title = "openDSLM – Live Preview + RAW (DNG)";
        window.SetDefaultSize(1280, 720);

        var overlay = Overlay.New();
        var picture = Picture.New();
        overlay.SetChild(picture);

        var hud = Label.New("");
        hud.Halign = Align.Start;
        hud.Valign = Align.Start;
        hud.MarginTop = 8;
        hud.MarginStart = 8;
        hud.AddCssClass("hud-readout");
        overlay.AddOverlay(hud);

        var panel = Box.New(Orientation.Vertical, 8);
        panel.Halign = Align.End;
        panel.Valign = Align.Start;
        panel.MarginTop = 12;
        panel.MarginEnd = 12;
        panel.AddCssClass("control-panel");

        var autoToggle = BuildAutoToggle(panel);
        var resolutionBox = BuildResolutionSection(panel);
        var isoBox = BuildIsoSection(panel);
        var shutterBox = BuildShutterSection(panel);
        (Scale zoomScale, Scale panXScale, Scale panYScale) = BuildZoomSection(panel);
        var captureButton = BuildCaptureButton(panel);

        overlay.AddOverlay(panel);

        StyleInstaller.TryInstall();
        window.SetChild(overlay);

        BindStateToControls(autoToggle, isoBox, shutterBox, resolutionBox, zoomScale, panXScale, panYScale);

        return new CameraWindow(window, picture, hud, isoBox, shutterBox, resolutionBox, autoToggle, zoomScale, panXScale, panYScale, captureButton);
    }

    private CheckButton BuildAutoToggle(Box panel)
    {
        var autoRow = Box.New(Orientation.Horizontal, 6);
        autoRow.AddCssClass("control-row");

        var autoLabel = Label.New("Auto AE/AGC");
        autoLabel.AddCssClass("control-inline-label");
        autoRow.Append(autoLabel);

        var autoChk = CheckButton.New();
        autoChk.Active = _state.AutoExposureEnabled;
        autoChk.AddCssClass("control-toggle");
        autoChk.OnToggled += (_, __) =>
        {
            if (_suppressAutoToggle) return;
            _dispatcher.FireAndForget(AppActionId.ToggleAutoExposure, new ToggleAutoExposurePayload(autoChk.Active));
        };

        autoRow.Append(autoChk);
        panel.Append(autoRow);
        return autoChk;
    }

    private ComboBoxText BuildResolutionSection(Box panel)
    {
        var resLabel = Label.New("Still Resolution");
        resLabel.AddCssClass("control-section-label");
        panel.Append(resLabel);

        var resBox = ComboBoxText.New();
        for (int i = 0; i < CameraPresets.ResolutionOptions.Length; i++)
        {
            resBox.AppendText(CameraPresets.ResolutionOptions[i].Label);
        }
        resBox.Active = _state.ResolutionIndex;
        resBox.AddCssClass("control-input");

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

        panel.Append(resBox);
        return resBox;
    }

    private ComboBoxText BuildIsoSection(Box panel)
    {
        var isoLabel = Label.New("ISO");
        isoLabel.AddCssClass("control-section-label");
        panel.Append(isoLabel);

        var isoBox = ComboBoxText.New();
        foreach (var iso in CameraPresets.IsoSteps)
        {
            isoBox.AppendText(iso.ToString());
        }
        isoBox.Active = _state.IsoIndex;
        isoBox.Sensitive = !_state.AutoExposureEnabled;
        isoBox.AddCssClass("control-input");

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

        panel.Append(isoBox);
        return isoBox;
    }

    private ComboBoxText BuildShutterSection(Box panel)
    {
        var shutLabel = Label.New("Shutter");
        shutLabel.AddCssClass("control-section-label");
        panel.Append(shutLabel);

        var shutBox = ComboBoxText.New();
        foreach (var sec in CameraPresets.ShutterSteps)
        {
            shutBox.AppendText(CameraController.ShutterLabel(sec));
        }
        shutBox.Active = _state.ShutterIndex;
        shutBox.Sensitive = !_state.AutoExposureEnabled;
        shutBox.AddCssClass("control-input");

        bool updating = false;
        shutBox.OnChanged += async (_, __) =>
        {
            if (updating || _suppressShutterChange) return;
            if (_state.AutoExposureEnabled) return;
            updating = true;
            try
            {
                await _dispatcher.DispatchAsync(AppActionId.SelectShutter, new SelectIndexPayload(shutBox.Active));
            }
            finally
            {
                updating = false;
            }
        };

        panel.Append(shutBox);
        return shutBox;
    }

    private (Scale ZoomScale, Scale PanXScale, Scale PanYScale) BuildZoomSection(Box panel)
    {
        var zoomLabel = Label.New("Zoom");
        zoomLabel.AddCssClass("control-section-label");
        panel.Append(zoomLabel);

        var zoomAdj = Adjustment.New(1.0, 1.0, 8.0, 0.1, 0.5, 0.0);
        var zoomScale = Scale.New(Orientation.Horizontal, zoomAdj);
        zoomScale.Digits = 2;
        zoomScale.AddCssClass("control-input");
        ((Gtk.Range)zoomScale).SetValue(_state.Zoom);

        zoomScale.OnValueChanged += (_, __) =>
        {
            if (_suppressZoomChange) return;
            double value = ((Gtk.Range)zoomScale).GetValue();
            _dispatcher.FireAndForget(AppActionId.AdjustZoom, new AdjustZoomPayload(value));
        };

        panel.Append(zoomScale);

        var panLabel = Label.New("Pan X / Pan Y");
        panLabel.AddCssClass("control-section-label");
        panel.Append(panLabel);

        var panXAdj = Adjustment.New(_state.PanX, 0.0, 1.0, 0.01, 0.1, 0.0);
        var panYAdj = Adjustment.New(_state.PanY, 0.0, 1.0, 0.01, 0.1, 0.0);
        var panXScale = Scale.New(Orientation.Horizontal, panXAdj);
        var panYScale = Scale.New(Orientation.Horizontal, panYAdj);
        panXScale.Digits = 2;
        panYScale.Digits = 2;
        panXScale.AddCssClass("control-input");
        panYScale.AddCssClass("control-input");

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

        panel.Append(panXScale);
        panel.Append(panYScale);
        return (zoomScale, panXScale, panYScale);
    }

    private Button BuildCaptureButton(Box panel)
    {
        var btn = Button.NewWithLabel("● Capture DNG");
        btn.AddCssClass("control-button");
        btn.OnClicked += async (_, __) =>
        {
            btn.Sensitive = false;
            try
            {
                await _dispatcher.DispatchAsync(AppActionId.CaptureStill);
            }
            finally
            {
                btn.Sensitive = true;
            }
        };

        panel.Append(btn);
        return btn;
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
