using System.Text.Json;

namespace Clip.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private bool _monitorClipboard = true;
    private bool _autoAnalyzeClipboard;
    private bool _hideToTrayOnClose = true;
    private bool _startMinimized;
    private bool _checkForYtDlpUpdates = true;
    private bool _suppressSave;

    public bool MonitorClipboard
    {
        get => _monitorClipboard;
        set
        {
            if (SetProperty(ref _monitorClipboard, value))
            {
                Save();
            }
        }
    }

    public bool AutoAnalyzeClipboard
    {
        get => _autoAnalyzeClipboard;
        set
        {
            if (SetProperty(ref _autoAnalyzeClipboard, value))
            {
                Save();
            }
        }
    }

    public bool HideToTrayOnClose
    {
        get => _hideToTrayOnClose;
        set
        {
            if (SetProperty(ref _hideToTrayOnClose, value))
            {
                Save();
            }
        }
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set
        {
            if (SetProperty(ref _startMinimized, value))
            {
                Save();
            }
        }
    }

    public bool CheckForYtDlpUpdates
    {
        get => _checkForYtDlpUpdates;
        set
        {
            if (SetProperty(ref _checkForYtDlpUpdates, value))
            {
                Save();
            }
        }
    }

    public static SettingsViewModel Load()
    {
        var settings = new SettingsViewModel { _suppressSave = true };
        try
        {
            if (File.Exists(ClipConstants.SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<SettingsSnapshot>(
                    File.ReadAllText(ClipConstants.SettingsPath),
                    JsonOptions);

                if (loaded is not null)
                {
                    settings.MonitorClipboard = loaded.MonitorClipboard;
                    settings.AutoAnalyzeClipboard = loaded.AutoAnalyzeClipboard;
                    settings.HideToTrayOnClose = loaded.HideToTrayOnClose;
                    settings.StartMinimized = loaded.StartMinimized;
                    settings.CheckForYtDlpUpdates = loaded.CheckForYtDlpUpdates;
                }
            }
        }
        catch
        {
            // Defaults are safe and keep the app usable.
        }
        finally
        {
            settings._suppressSave = false;
        }

        return settings;
    }

    private void Save()
    {
        if (_suppressSave)
        {
            return;
        }

        Directory.CreateDirectory(ClipConstants.AppDataDirectory);
        var snapshot = new SettingsSnapshot
        {
            MonitorClipboard = MonitorClipboard,
            AutoAnalyzeClipboard = AutoAnalyzeClipboard,
            HideToTrayOnClose = HideToTrayOnClose,
            StartMinimized = StartMinimized,
            CheckForYtDlpUpdates = CheckForYtDlpUpdates
        };
        File.WriteAllText(ClipConstants.SettingsPath, JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    private sealed class SettingsSnapshot
    {
        public bool MonitorClipboard { get; set; } = true;
        public bool AutoAnalyzeClipboard { get; set; }
        public bool HideToTrayOnClose { get; set; } = true;
        public bool StartMinimized { get; set; }
        public bool CheckForYtDlpUpdates { get; set; } = true;
    }
}
