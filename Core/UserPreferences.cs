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
        if (model.GalleryPageSize < 1 || model.GalleryPageSize > 48)
        {
            model.GalleryPageSize = 4;
        }
        return model;
    }

    private class PreferencesModel
    {
        public bool GalleryColorEnabled { get; set; } = true;
        public int GalleryPageSize { get; set; } = 4;
    }
}
