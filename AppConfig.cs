using System.Text.Json;
using System.Text.Json.Serialization;

namespace NXVideoMaker;

public class AppConfig
{
    // ── User-visible fields ──────────────────────────────────────────────────
    public string Title          { get; set; } = "";
    public string Author         { get; set; } = "";
    public string DisplayVersion { get; set; } = "1.0.0";
    public string TitleId        { get; set; } = "";
    public string IconPath       { get; set; } = "";
    public string VideoFolder    { get; set; } = "";
    public string KeysPath       { get; set; } = "";

    // ── Internal defaults (not shown in UI) ──────────────────────────────────
    public int    KeyGen      { get; set; } = 19;
    public string SdkVersion  { get; set; } = "13030000";
    public string SysVersion  { get; set; } = "19.0.0";

    // ────────────────────────────────────────────────────────────────────────

    [JsonIgnore]
    public static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "NXVideoMaker.config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
        }
        catch { /* corrupt config — start fresh */ }

        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, options));
        }
        catch { /* non-fatal */ }
    }
}
