using System;

public sealed class CameraState
{
    private bool _autoExposureEnabled = true;
    private int _isoIndex = Array.IndexOf(CameraPresets.IsoSteps, 400);
    private int _shutterIndex = Array.IndexOf(CameraPresets.ShutterSteps, 1.0 / 60);
    private int _resolutionIndex = 0;
    private double _zoom = 1.0;
    private double _panX = 0.5;
    private double _panY = 0.5;

    public event EventHandler<bool>? AutoExposureChanged;
    public event EventHandler<int>? IsoIndexChanged;
    public event EventHandler<int>? ShutterIndexChanged;
    public event EventHandler<int>? ResolutionIndexChanged;
    public event EventHandler<double>? ZoomChanged;
    public event EventHandler<(double X, double Y)>? PanChanged;

    public event EventHandler? LastCaptureChanged;

    public long? LastExposureMicroseconds { get; private set; }
    public int? LastIso { get; private set; }
    public double? LastAnalogueGain { get; private set; }

    public bool AutoExposureEnabled
    {
        get => _autoExposureEnabled;
        set
        {
            if (_autoExposureEnabled == value) return;
            _autoExposureEnabled = value;
            AutoExposureChanged?.Invoke(this, value);
        }
    }

    public int IsoIndex
    {
        get => _isoIndex;
        set
        {
            value = Math.Clamp(value, 0, CameraPresets.IsoSteps.Length - 1);
            if (_isoIndex == value) return;
            _isoIndex = value;
            IsoIndexChanged?.Invoke(this, value);
        }
    }

    public int ShutterIndex
    {
        get => _shutterIndex;
        set
        {
            value = Math.Clamp(value, 0, CameraPresets.ShutterSteps.Length - 1);
            if (_shutterIndex == value) return;
            _shutterIndex = value;
            ShutterIndexChanged?.Invoke(this, value);
        }
    }

    public int ResolutionIndex
    {
        get => _resolutionIndex;
        set
        {
            value = Math.Clamp(value, 0, CameraPresets.ResolutionOptions.Length - 1);
            if (_resolutionIndex == value) return;
            _resolutionIndex = value;
            ResolutionIndexChanged?.Invoke(this, value);
        }
    }

    public double Zoom
    {
        get => _zoom;
        set
        {
            value = Math.Clamp(value, 1.0, 8.0);
            if (Math.Abs(_zoom - value) < 0.0001) return;
            _zoom = value;
            ZoomChanged?.Invoke(this, value);
        }
    }

    public double PanX
    {
        get => _panX;
        set
        {
            value = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_panX - value) < 0.0001) return;
            _panX = value;
            PanChanged?.Invoke(this, (value, _panY));
        }
    }

    public double PanY
    {
        get => _panY;
        set
        {
            value = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_panY - value) < 0.0001) return;
            _panY = value;
            PanChanged?.Invoke(this, (_panX, value));
        }
    }

    public (int Iso, long ShutterUs) GetManualRequest()
    {
        int iso = CameraPresets.IsoSteps[Math.Clamp(_isoIndex, 0, CameraPresets.IsoSteps.Length - 1)];
        double seconds = CameraPresets.ShutterSteps[Math.Clamp(_shutterIndex, 0, CameraPresets.ShutterSteps.Length - 1)];
        long microseconds = (long)Math.Round(seconds * 1_000_000.0);
        if (microseconds <= 0) microseconds = 1;
        return (iso, microseconds);
    }

    public (int Width, int Height) GetSelectedStillResolution()
    {
        var option = CameraPresets.ResolutionOptions[Math.Clamp(_resolutionIndex, 0, CameraPresets.ResolutionOptions.Length - 1)];
        return (option.Width, option.Height);
    }

    public void UpdateLastCapture(long? expUs, int? iso, double? analogueGain)
    {
        LastExposureMicroseconds = expUs;
        LastIso = iso;
        LastAnalogueGain = analogueGain;
        LastCaptureChanged?.Invoke(this, EventArgs.Empty);
    }
}
