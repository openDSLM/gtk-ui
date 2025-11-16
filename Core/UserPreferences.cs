using System;
using System.IO;
using System.Text.Json;

public sealed class UserPreferences
{
    private const string PreferencesFileName = "preferences.json";

    private static readonly Lazy<UserPreferences> LazyInstance = new(() => new UserPreferences());

    public static UserPreferences Instance => LazyInstance.Value;

    private readonly object _syncRoot = new();
    private PreferencesModel _model;
    private readonly string _filePath;

    private UserPreferences()
    {
        _filePath = ResolvePreferencesPath();
        _model = LoadPreferences();
    }

    public bool GalleryColorEnabled
    {
        get => _model.GalleryColorEnabled;
        set
        {
            if (_model.GalleryColorEnabled == value) return;
            _model.GalleryColorEnabled = value;
            SavePreferences();
        }
    }

    public int GalleryPageSize
    {
        get => _model.GalleryPageSize;
        set
        {
            value = Math.Clamp(value, 1, 48);
            if (_model.GalleryPageSize == value) return;
            _model.GalleryPageSize = value;
            SavePreferences();
        }
    }

    public CaptureMode CaptureMode
    {
        get => _model.CaptureMode;
        set
        {
            if (_model.CaptureMode == value) return;
            _model.CaptureMode = value;
            SavePreferences();
        }
    }

    public double VideoFps
    {
        get => _model.VideoFps;
        set
        {
            double clamped = Math.Clamp(value, 1.0, 240.0);
            if (Math.Abs(_model.VideoFps - clamped) < 0.0001) return;
            _model.VideoFps = clamped;
            SavePreferences();
        }
    }

    public double VideoShutterAngle
    {
        get => _model.VideoShutterAngle;
        set
        {
            double clamped = Math.Clamp(value, 1.0, 360.0);
            if (Math.Abs(_model.VideoShutterAngle - clamped) < 0.0001) return;
            _model.VideoShutterAngle = clamped;
            SavePreferences();
        }
    }

    public double TimelapseIntervalSeconds
    {
        get => _model.TimelapseIntervalSeconds;
        set
        {
            double clamped = Math.Clamp(value, 0.5, 3600.0);
            if (Math.Abs(_model.TimelapseIntervalSeconds - clamped) < 0.0001) return;
            _model.TimelapseIntervalSeconds = clamped;
            SavePreferences();
        }
    }

    public int TimelapseFrameCount
    {
        get => _model.TimelapseFrameCount;
        set
        {
            int clamped = Math.Clamp(value, 1, 10000);
            if (_model.TimelapseFrameCount == clamped) return;
            _model.TimelapseFrameCount = clamped;
            SavePreferences();
        }
    }

    public string OutputDirectory
    {
        get => string.IsNullOrWhiteSpace(_model.OutputDirectory) ? "/ssd/RAW" : _model.OutputDirectory;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_model.OutputDirectory, value, StringComparison.Ordinal))
                return;
            _model.OutputDirectory = value;
            SavePreferences();
        }
    }

    public MetadataOverrides MetadataOverrides
    {
        get => new(
            _model.MetadataMake,
            _model.MetadataModel,
            _model.MetadataUniqueModel,
            _model.MetadataSoftware,
            _model.MetadataArtist,
            _model.MetadataCopyright);
        set
        {
            value ??= MetadataOverrides.Empty;
            bool changed =
                !string.Equals(_model.MetadataMake, value.Make, StringComparison.Ordinal) ||
                !string.Equals(_model.MetadataModel, value.Model, StringComparison.Ordinal) ||
                !string.Equals(_model.MetadataUniqueModel, value.UniqueModel, StringComparison.Ordinal) ||
                !string.Equals(_model.MetadataSoftware, value.Software, StringComparison.Ordinal) ||
                !string.Equals(_model.MetadataArtist, value.Artist, StringComparison.Ordinal) ||
                !string.Equals(_model.MetadataCopyright, value.Copyright, StringComparison.Ordinal);
            if (!changed)
                return;
            _model.MetadataMake = value.Make;
            _model.MetadataModel = value.Model;
            _model.MetadataUniqueModel = value.UniqueModel;
            _model.MetadataSoftware = value.Software;
            _model.MetadataArtist = value.Artist;
            _model.MetadataCopyright = value.Copyright;
            SavePreferences();
        }
    }

    private PreferencesModel LoadPreferences()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                var existing = JsonSerializer.Deserialize<PreferencesModel>(json);
                if (existing != null)
                {
                    return Normalize(existing);
                }
            }
        }
        catch
        {
        }

        return new PreferencesModel();
    }

    private void SavePreferences()
    {
        lock (_syncRoot)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                string json = JsonSerializer.Serialize(_model, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_filePath, json);
            }
            catch
            {
            }
        }
    }

    private static string ResolvePreferencesPath()
    {
        string? baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(baseDir))
        {
            baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        string preferencesDir = Path.Combine(baseDir, "opendslm-ui");
        return Path.Combine(preferencesDir, PreferencesFileName);
    }

    private static PreferencesModel Normalize(PreferencesModel model)
    {
        if (model.GalleryPageSize < 2 || model.GalleryPageSize > 6)
        {
            if (model.GalleryPageSize > 6)
            {
                int approxRows = (int)Math.Round(Math.Sqrt(model.GalleryPageSize));
                model.GalleryPageSize = Math.Clamp(approxRows, 2, 6);
            }
            else
            {
                model.GalleryPageSize = 2;
            }
        }
        if (!Enum.IsDefined(typeof(CaptureMode), model.CaptureMode))
        {
            model.CaptureMode = CaptureMode.Photo;
        }
        if (model.VideoFps < 1.0 || model.VideoFps > 240.0)
        {
            model.VideoFps = 24.0;
        }
        if (model.VideoShutterAngle < 1.0 || model.VideoShutterAngle > 360.0)
        {
            model.VideoShutterAngle = 180.0;
        }
        if (model.TimelapseIntervalSeconds < 0.5 || model.TimelapseIntervalSeconds > 3600.0)
        {
            model.TimelapseIntervalSeconds = 5.0;
        }
        if (model.TimelapseFrameCount < 1 || model.TimelapseFrameCount > 10000)
        {
            model.TimelapseFrameCount = 120;
        }
        model.OutputDirectory ??= "/ssd/RAW";
        return model;
    }

    private class PreferencesModel
    {
        public bool GalleryColorEnabled { get; set; } = true;
        public int GalleryPageSize { get; set; } = 2;
        public CaptureMode CaptureMode { get; set; } = CaptureMode.Photo;
        public double VideoFps { get; set; } = 24.0;
        public double VideoShutterAngle { get; set; } = 180.0;
        public double TimelapseIntervalSeconds { get; set; } = 5.0;
        public int TimelapseFrameCount { get; set; } = 120;
        public string OutputDirectory { get; set; } = "/ssd/RAW";
        public string? MetadataMake { get; set; }
        public string? MetadataModel { get; set; }
        public string? MetadataUniqueModel { get; set; }
        public string? MetadataSoftware { get; set; }
        public string? MetadataArtist { get; set; }
        public string? MetadataCopyright { get; set; }
    }
}
