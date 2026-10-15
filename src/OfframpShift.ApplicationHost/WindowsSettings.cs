using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace OfframpShift.ApplicationHost;

public enum AppTheme { System, Light, Dark }
public enum SettingsStatus { Ready, Missing, Invalid, Incompatible, Unavailable }

public sealed record WindowsSettings
{
    [JsonRequired] public int Version { get; init; } = 2;
    [JsonRequired] public Guid? Personal { get; init; }
    [JsonRequired] public Guid? Work { get; init; }
    [JsonRequired] public string Provider { get; init; } = "shell-com-53f5ca0b-readonly-v1";
    [JsonRequired] public string Build { get; init; } = "";
    [JsonRequired] public bool Consent { get; init; }
    [JsonRequired] public bool MonitoringEnabled { get; init; }
    [JsonRequired] public AppTheme Theme { get; init; }
    [JsonRequired] public bool? CloseToTray { get; init; }
    [JsonRequired] public bool BreakRemindersEnabled { get; init; }
    [JsonIgnore] public bool Complete => Personal is not null && Work is not null && Consent;

    public void Validate()
    {
        if (Version != 2 || Provider != "shell-com-53f5ca0b-readonly-v1" || Build is null ||
            Build.Length > 40 || !Enum.IsDefined(Theme) || Personal == Guid.Empty || Work == Guid.Empty ||
            Personal is not null && Personal == Work || MonitoringEnabled && !Complete)
            throw new ArgumentException("Invalid Windows settings.");
    }
}

public sealed class WindowsSettingsStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true };
    private string PathName => Path.Combine(directory, "windows-settings.json");
    public SettingsStatus Status { get; private set; } = SettingsStatus.Missing;
    internal Action? BeforeReplace { get; set; }

    public WindowsSettings Load()
    {
        try
        {
            using var stream = new FileStream(PathName, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 16_384) throw new JsonException();
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in document.RootElement.EnumerateObject())
                if (!names.Add(item.Name)) throw new JsonException();
            if (document.RootElement.TryGetProperty("Version", out var version) && version.ValueKind == JsonValueKind.Number &&
                (!version.TryGetInt32(out var number) || number is not (1 or 2)))
            { Status = SettingsStatus.Incompatible; return new(); }
            WindowsSettings settings;
            if (version.ValueKind == JsonValueKind.Number && version.GetInt32() == 1)
            {
                // Validate the exact v1 shape before adding the new required v2 field.
                // Migration is in memory only; the next explicit atomic save writes v2.
                if (!names.SetEquals(["Version", "Personal", "Work", "Provider", "Build", "Consent",
                    "MonitoringEnabled", "Theme", "CloseToTray"])) throw new JsonException();
                var migrated = JsonNode.Parse(document.RootElement.GetRawText())!;
                migrated["Version"] = 2;
                migrated["BreakRemindersEnabled"] = false;
                settings = migrated.Deserialize<WindowsSettings>(Options) ?? throw new JsonException();
            }
            else settings = document.RootElement.Deserialize<WindowsSettings>(Options) ?? throw new JsonException();
            settings.Validate(); Status = SettingsStatus.Ready; return settings;
        }
        catch (FileNotFoundException) { Status = SettingsStatus.Missing; }
        catch (DirectoryNotFoundException) { Status = SettingsStatus.Missing; }
        catch (Exception e) when (e is JsonException or ArgumentException) { Status = SettingsStatus.Invalid; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = SettingsStatus.Unavailable; }
        return new();
    }

    public void Save(WindowsSettings settings)
    {
        settings.Validate();
        if (Status is SettingsStatus.Invalid or SettingsStatus.Incompatible or SettingsStatus.Unavailable)
            throw new InvalidOperationException("Windows settings require explicit repair or a compatible app.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, "settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(file, settings, Options); file.Flush(true); }
            BeforeReplace?.Invoke();
            if (File.Exists(PathName)) File.Replace(temporary, PathName, null);
            else File.Move(temporary, PathName);
            Status = SettingsStatus.Ready;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Repair(bool confirmed)
    {
        if (!confirmed || Status != SettingsStatus.Invalid) throw new InvalidOperationException("Repair unavailable.");
        using (var original = new FileStream(PathName, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var archive = new FileStream(PathName + ".preserved-" + Guid.NewGuid().ToString("N"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { original.CopyTo(archive); archive.Flush(true); }
        Status = SettingsStatus.Ready;
        try { Save(new()); }
        catch { Status = SettingsStatus.Invalid; throw; }
    }
}
