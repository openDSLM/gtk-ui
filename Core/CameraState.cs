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
    private bool _galleryColorEnabled = true;
    private int _galleryPageIndex;
    private int _galleryPageSize = 2;
    private CaptureMode _captureMode = CaptureMode.Photo;
    private double _videoFps = 24.0;
    private double _videoShutterAngle = 180.0;
    private bool _videoRecording;
    private string? _activeVideoSequencePath;
    private double _videoActualFps;
    private int _videoCapturedFrames;
    private int _videoDroppedFrames;
    private double _timelapseIntervalSeconds = 5.0;
    private int _timelapseFrameCount = 120;
    private bool _timelapseActive;
    private string? _activeTimelapsePath;
    private readonly UserPreferences _preferences;
    private CameraMetadataSnapshot _metadataSnapshot = CameraMetadataSnapshot.Empty;
    private int ItemsPerGalleryPage => Math.Max(1, _galleryPageSize * _galleryPageSize);

    public CameraState()
    {
        _recentCaptureView = _recentCaptures.AsReadOnly();
        _preferences = UserPreferences.Instance;
        _galleryColorEnabled = _preferences.GalleryColorEnabled;
        _galleryPageSize = _preferences.GalleryPageSize;
        _captureMode = _preferences.CaptureMode;
        _videoFps = _preferences.VideoFps;
        _videoShutterAngle = _preferences.VideoShutterAngle;
        _timelapseIntervalSeconds = _preferences.TimelapseIntervalSeconds;
        _timelapseFrameCount = _preferences.TimelapseFrameCount;
        _outputDirectory = _preferences.OutputDirectory;
        var storedOverrides = _preferences.MetadataOverrides;
        _metadataSnapshot = new CameraMetadataSnapshot(
            storedOverrides.Make,
            storedOverrides.Model,
            storedOverrides.UniqueModel,
            storedOverrides.Software,
            storedOverrides.Artist,
            storedOverrides.Copyright,
            storedOverrides.Make,
            storedOverrides.Model,
            storedOverrides.UniqueModel,
            storedOverrides.Software,
            storedOverrides.Artist,
            storedOverrides.Copyright);
        EnsureGalleryPageBounds();
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
    public event EventHandler<bool>? GalleryColorEnabledChanged;
    public event EventHandler<int>? GalleryPageIndexChanged;
    public event EventHandler<int>? GalleryPageSizeChanged;
    public event EventHandler<CaptureMode>? CaptureModeChanged;
    public event EventHandler? VideoSettingsChanged;
    public event EventHandler<bool>? VideoRecordingChanged;
    public event EventHandler<VideoRecordingMetrics>? VideoRecordingMetricsChanged;
    public event EventHandler? TimelapseSettingsChanged;
    public event EventHandler<bool>? TimelapseActiveChanged;
    public event EventHandler? MetadataChanged;

    public long? LastExposureMicroseconds { get; private set; }
    public int? LastIso { get; private set; }
    public double? LastAnalogueGain { get; private set; }
    public readonly record struct VideoRecordingMetrics(double TargetFps, double ActualFps, int CapturedFrames, int DroppedFrames);
    public CameraMetadataSnapshot Metadata => _metadataSnapshot;
    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            value ??= string.Empty;
            if (_outputDirectory == value) return;
            _outputDirectory = value;
            _preferences.OutputDirectory = value;
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
            EnsureGalleryPageBounds();
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

        EnsureGalleryPageBounds();
        RecentCapturesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearRecentCaptures()
    {
        if (_recentCaptures.Count == 0)
        {
            return;
        }

        _recentCaptures.Clear();
        EnsureGalleryPageBounds();
        RecentCapturesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateMetadata(CameraMetadataSnapshot snapshot)
    {
        snapshot ??= CameraMetadataSnapshot.Empty;
        if (_metadataSnapshot == snapshot)
        {
            return;
        }

        _metadataSnapshot = snapshot;
        var overrides = new MetadataOverrides(
            snapshot.MakeOverride,
            snapshot.ModelOverride,
            snapshot.UniqueModelOverride,
            snapshot.SoftwareOverride,
            snapshot.ArtistOverride,
            snapshot.CopyrightOverride);
        _preferences.MetadataOverrides = overrides;
        MetadataChanged?.Invoke(this, EventArgs.Empty);
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

    public bool GalleryColorEnabled
    {
        get => _galleryColorEnabled;
        set
        {
            if (_galleryColorEnabled == value) return;
            _galleryColorEnabled = value;
            _preferences.GalleryColorEnabled = value;
            GalleryColorEnabledChanged?.Invoke(this, value);
        }
    }

    public CaptureMode CaptureMode
    {
        get => _captureMode;
        set
        {
            if (_captureMode == value) return;
            _captureMode = value;
            _preferences.CaptureMode = value;
            EnsureGalleryPageBounds();
            CaptureModeChanged?.Invoke(this, value);
        }
    }

    public int GalleryPageIndex => _galleryPageIndex;

    public void SetGalleryPage(int page)
    {
        int clamped = ClampPageIndex(page);
        if (_galleryPageIndex == clamped) return;
        _galleryPageIndex = clamped;
        GalleryPageIndexChanged?.Invoke(this, clamped);
    }

    public int GetGalleryPageCount()
    {
        if (_recentCaptures.Count == 0) return 0;
        int perPage = ItemsPerGalleryPage;
        return (_recentCaptures.Count + perPage - 1) / perPage;
    }

    public IReadOnlyList<string> GetGalleryPageItems()
    {
        if (_recentCaptures.Count == 0)
        {
            if (_galleryPageIndex != 0)
            {
                _galleryPageIndex = 0;
                GalleryPageIndexChanged?.Invoke(this, 0);
            }
            return Array.Empty<string>();
        }

        int totalPages = GetGalleryPageCount();
        int clampedIndex = Math.Clamp(_galleryPageIndex, 0, Math.Max(0, totalPages - 1));
        if (clampedIndex != _galleryPageIndex)
        {
            _galleryPageIndex = clampedIndex;
            GalleryPageIndexChanged?.Invoke(this, clampedIndex);
        }

        int perPage = ItemsPerGalleryPage;
        int start = clampedIndex * perPage;
        if (start >= _recentCaptures.Count)
        {
            start = Math.Max(0, _recentCaptures.Count - perPage);
        }
        int count = Math.Clamp(_recentCaptures.Count - start, 0, perPage);
        if (count <= 0)
        {
            return Array.Empty<string>();
        }

        return _recentCaptures.GetRange(start, count);
    }

    public int GalleryPageSize
    {
        get => _galleryPageSize;
        set
        {
            int clamped = Math.Clamp(value, 2, 6);
            if (_galleryPageSize == clamped) return;
            _galleryPageSize = clamped;
            _preferences.GalleryPageSize = clamped;
            EnsureGalleryPageBounds();
            GalleryPageSizeChanged?.Invoke(this, clamped);
            RecentCapturesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double VideoFps
    {
        get => _videoFps;
        set
        {
            double clamped = Math.Clamp(value, 1.0, 240.0);
            if (Math.Abs(_videoFps - clamped) < 0.0001) return;
            _videoFps = clamped;
            _preferences.VideoFps = clamped;
            VideoSettingsChanged?.Invoke(this, EventArgs.Empty);
            NotifyVideoRecordingMetricsChanged();
        }
    }

    public double VideoShutterAngle
    {
        get => _videoShutterAngle;
        set
        {
            double clamped = Math.Clamp(value, 1.0, 360.0);
            if (Math.Abs(_videoShutterAngle - clamped) < 0.0001) return;
            _videoShutterAngle = clamped;
            _preferences.VideoShutterAngle = clamped;
            VideoSettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsVideoRecording => _videoRecording;
    public string? ActiveVideoSequencePath => _activeVideoSequencePath;
    public VideoRecordingMetrics CurrentVideoRecordingMetrics => new VideoRecordingMetrics(_videoFps, _videoActualFps, _videoCapturedFrames, _videoDroppedFrames);

    public void BeginVideoRecording(string sequencePath)
    {
        _videoRecording = true;
        _activeVideoSequencePath = sequencePath;
        ResetVideoRecordingMetrics();
        VideoRecordingChanged?.Invoke(this, true);
    }

    public void EndVideoRecording()
    {
        if (!_videoRecording) return;
        _videoRecording = false;
        _activeVideoSequencePath = null;
        VideoRecordingChanged?.Invoke(this, false);
    }

    public void ResetVideoRecordingMetrics()
    {
        SetVideoRecordingMetricsInternal(0.0, 0, 0, force: true);
    }

    public void UpdateVideoRecordingMetrics(double actualFps, int capturedFrames, int droppedFrames)
    {
        SetVideoRecordingMetricsInternal(actualFps, capturedFrames, droppedFrames, force: false);
    }

    public double TimelapseIntervalSeconds
    {
        get => _timelapseIntervalSeconds;
        set
        {
            double clamped = Math.Clamp(value, 0.5, 3600.0);
            if (Math.Abs(_timelapseIntervalSeconds - clamped) < 0.0001) return;
            _timelapseIntervalSeconds = clamped;
            _preferences.TimelapseIntervalSeconds = clamped;
            TimelapseSettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int TimelapseFrameCount
    {
        get => _timelapseFrameCount;
        set
        {
            int clamped = Math.Clamp(value, 1, 10000);
            if (_timelapseFrameCount == clamped) return;
            _timelapseFrameCount = clamped;
            _preferences.TimelapseFrameCount = clamped;
            TimelapseSettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool TimelapseActive => _timelapseActive;
    public string? ActiveTimelapsePath => _activeTimelapsePath;

    public void BeginTimelapse(string sequencePath)
    {
        _timelapseActive = true;
        _activeTimelapsePath = sequencePath;
        TimelapseActiveChanged?.Invoke(this, true);
    }

    public void EndTimelapse()
    {
        if (!_timelapseActive) return;
        _timelapseActive = false;
        _activeTimelapsePath = null;
        TimelapseActiveChanged?.Invoke(this, false);
    }

    private void SetVideoRecordingMetricsInternal(double actualFps, int capturedFrames, int droppedFrames, bool force)
    {
        if (double.IsNaN(actualFps) || double.IsInfinity(actualFps))
        {
            actualFps = 0.0;
        }

        actualFps = Math.Clamp(actualFps, 0.0, 480.0);
        capturedFrames = Math.Max(0, capturedFrames);
        droppedFrames = Math.Max(0, droppedFrames);

        bool changed = force
            || Math.Abs(_videoActualFps - actualFps) > 0.005
            || _videoCapturedFrames != capturedFrames
            || _videoDroppedFrames != droppedFrames;

        if (!changed)
        {
            return;
        }

        _videoActualFps = actualFps;
        _videoCapturedFrames = capturedFrames;
        _videoDroppedFrames = droppedFrames;
        NotifyVideoRecordingMetricsChanged();
    }

    private void NotifyVideoRecordingMetricsChanged()
    {
        VideoRecordingMetricsChanged?.Invoke(this, new VideoRecordingMetrics(_videoFps, _videoActualFps, _videoCapturedFrames, _videoDroppedFrames));
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

    private void EnsureGalleryPageBounds()
    {
        int clamped = ClampPageIndex(_galleryPageIndex);
        if (_galleryPageIndex != clamped)
        {
            _galleryPageIndex = clamped;
            GalleryPageIndexChanged?.Invoke(this, clamped);
        }
    }

    private int ClampPageIndex(int requested)
    {
        int totalPages = GetGalleryPageCount();
        if (totalPages == 0)
        {
            return 0;
        }

        return Math.Clamp(requested, 0, totalPages - 1);
    }
}
