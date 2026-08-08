using System;
using System.IO;
using System.Text.Json;

namespace DanmuFree.App.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON under
/// <c>%AppData%/DanmuFree/settings.json</c>. A missing or malformed file
/// yields a default <see cref="AppSettings"/> instance rather than throwing.
/// </summary>
public sealed class SettingsService
{
    private static string BaseDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DanmuFree");

    private static string SettingsPath => Path.Combine(BaseDir, "settings.json");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath))!
                : new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(BaseDir);
        File.WriteAllText(
            SettingsPath,
            JsonSerializer.Serialize(settings, SerializerOptions));
    }
}
