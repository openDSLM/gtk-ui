using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

public sealed class CameraState
{
    private const int MaxRecentCaptures = 120;

    private bool _autoExposureEnabled = true;
    private int _isoIndex = Array.IndexOf(CameraPresets.IsoSteps, 400);
    private int _shutterIndex = Array.IndexOf(CameraPresets.ShutterSteps, 1.0 / 60);
    private int _resolutionIndex = 0;
    private double _zoom = 1.0;
    private double _panX = 0.5;
    private double _panY = 0.5;
    private string _outputDirectory = "/ssd/RAW";
    private readonly List<string> _recentCaptures = new();
    private readonly ReadOnlyCollection<string> _recentCaptureView;

    public CameraState()
    {
        _recentCaptureView = _recentCaptures.AsReadOnly();
    }

    public event EventHandler<bool>? AutoExposureChanged;
    public event EventHandler<int>? IsoIndexChanged;
    public event EventHandler<int>? ShutterIndexChanged;
    public event EventHandler<int>? ResolutionIndexChanged;
    public event EventHandler<double>? ZoomChanged;
    public event EventHandler<(double X, double Y)>? PanChanged;
    public event EventHandler<string>? OutputDirectoryChanged;

    public event EventHandler? LastCaptureChanged;
    public event EventHandler? RecentCapturesChanged;

    public long? LastExposureMicroseconds { get; private set; }
    public int? LastIso { get; private set; }
    public double? LastAnalogueGain { get; private set; }
    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            value ??= string.Empty;
            if (_outputDirectory == value) return;
            _outputDirectory = value;
            OutputDirectoryChanged?.Invoke(this, value);
        }
    }

    public IReadOnlyList<string> RecentCaptures => _recentCaptureView;

    public void AppendRecentCaptures(IEnumerable<string> paths)
    {
        if (paths == null)
        {
            return;
        }

        bool changed = false;
        foreach (var path in paths)
        {
            string? normalized = NormalizeCapturePath(path);
            if (string.IsNullOrEmpty(normalized))
            {
                continue;
            }

            int existingIndex = _recentCaptures.FindIndex(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                if (existingIndex == 0)
                {
                    continue;
                }

                _recentCaptures.RemoveAt(existingIndex);
            }

            _recentCaptures.Insert(0, normalized);
            changed = true;
        }

        if (_recentCaptures.Count > MaxRecentCaptures)
        {
            _recentCaptures.RemoveRange(MaxRecentCaptures, _recentCaptures.Count - MaxRecentCaptures);
            changed = true;
        }

        if (changed)
        {
            RecentCapturesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ReplaceRecentCaptures(IEnumerable<string>? paths)
    {
        _recentCaptures.Clear();

        if (paths != null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in paths)
            {
                string? normalized = NormalizeCapturePath(candidate);
                if (string.IsNullOrEmpty(normalized))
                {
                    continue;
                }

                if (!seen.Add(normalized))
                {
                    continue;
                }

                _recentCaptures.Add(normalized);
                if (_recentCaptures.Count >= MaxRecentCaptures)
                {
                    break;
                }
            }
        }

        RecentCapturesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearRecentCaptures()
    {
        if (_recentCaptures.Count == 0)
        {
            return;
        }

        _recentCaptures.Clear();
        RecentCapturesChanged?.Invoke(this, EventArgs.Empty);
    }

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

    public string GetSelectedSensorMode()
    {
        var option = CameraPresets.ResolutionOptions[Math.Clamp(_resolutionIndex, 0, CameraPresets.ResolutionOptions.Length - 1)];
        return option.Mode;
    }

    public void UpdateLastCapture(long? expUs, int? iso, double? analogueGain)
    {
        LastExposureMicroseconds = expUs;
        LastIso = iso;
        LastAnalogueGain = analogueGain;
        LastCaptureChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string? NormalizeCapturePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string trimmed = path.Trim();

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }
}
