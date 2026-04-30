using System.Text.Json;
using Clip.Core.App;

namespace Clip.Core.Settings;

public sealed class SettingsStore : IAppSettingsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public SettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? ClipPaths.SettingsPath;
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Current.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath) ?? ClipPaths.AppDataDirectory);
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json, cancellationToken);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            Current = new AppSettings();
            Current.Normalize();
            return;
        }

        await using var stream = File.OpenRead(_settingsPath);
        Current = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken) ?? new AppSettings();
        Current.Normalize();
    }

    private AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            var fresh = new AppSettings();
            fresh.Normalize();
            return fresh;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch
        {
            var fresh = new AppSettings();
            fresh.Normalize();
            return fresh;
        }
    }
}
