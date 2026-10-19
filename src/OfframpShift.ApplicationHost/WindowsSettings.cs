using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using DesktopFeasibility;

namespace OfframpShift.ApplicationHost;

public enum AppTheme { System, Light, Dark }
public enum ReminderStyle { Off, WindowsNotification, InteractiveWindow }
public enum SettingsStatus { Ready, Missing, Invalid, Incompatible, Unavailable }

public sealed record WindowsSettings
{
    [JsonRequired] public int Version { get; init; } = 5;
    [JsonRequired] public Guid? Personal { get; init; }
    [JsonRequired] public Guid? Work { get; init; }
    [JsonRequired] public string Provider { get; init; } = "shell-com-53f5ca0b-readonly-v1";
    [JsonRequired] public string Build { get; init; } = "";
    [JsonRequired] public bool Consent { get; init; }
    [JsonRequired] public bool MonitoringEnabled { get; init; }
    [JsonRequired] public AppTheme Theme { get; init; }
    [JsonRequired] public bool? CloseToTray { get; init; }
    [JsonRequired] public ReminderStyle ReminderStyle { get; init; }
    [JsonRequired] public bool SwitchingConsent { get; init; }
    [JsonRequired] public string SwitchingConsentBuild { get; init; } = "";
    [JsonRequired] public string SwitchingConsentFamily { get; init; } = "";
    [JsonIgnore] public bool Complete => Personal is not null && Work is not null && Consent;
    public bool HasSwitchingConsent(string currentBuild) => SwitchingConsent && Complete &&
        (SwitchingConsentFamily == "26300"
            ? SwitchingConsentBuild == "" && SwitchCompatibility.Is26300Family(Build) && SwitchCompatibility.Is26300Family(currentBuild)
            : SwitchingConsentFamily == "" && !SwitchCompatibility.Is26300Family(Build) && SwitchingConsentBuild == currentBuild && Build == currentBuild);

    public void Validate()
    {
        if (Version != 5 || SwitchingConsentBuild is null || SwitchingConsentBuild.Length > 40 || SwitchingConsentFamily is null ||
            SwitchingConsent && (!Complete || !(SwitchingConsentFamily == "26300" && SwitchingConsentBuild == "" && SwitchCompatibility.Is26300Family(Build) ||
                SwitchingConsentFamily == "" && SwitchingConsentBuild.Length > 0 && SwitchingConsentBuild == Build && !SwitchCompatibility.Is26300Family(Build))) ||
            !SwitchingConsent && (SwitchingConsentBuild.Length != 0 || SwitchingConsentFamily.Length != 0) ||
            !Enum.IsDefined(ReminderStyle) || Provider != "shell-com-53f5ca0b-readonly-v1" || Build is null ||
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
                (!version.TryGetInt32(out var number) || number is not (1 or 2 or 3 or 4 or 5)))
            { Status = SettingsStatus.Incompatible; return new(); }
            WindowsSettings settings;
            if (version.ValueKind == JsonValueKind.Number && version.GetInt32() is 1 or 2 or 3 or 4)
            {
                // Validate each exact legacy shape; migration never opts into switching.
                string[] legacyFields = ["Version", "Personal", "Work", "Provider", "Build", "Consent",
                    "MonitoringEnabled", "Theme", "CloseToTray"];
                var oldVersion = version.GetInt32();
                var expected = oldVersion == 1 ? legacyFields : oldVersion == 4
                    ? [.. legacyFields, "ReminderStyle", "SwitchingConsent", "SwitchingConsentBuild"]
                    : [.. legacyFields, oldVersion == 2 ? "BreakRemindersEnabled" : "ReminderStyle"];
                if (!names.SetEquals(expected)) throw new JsonException();
                if (oldVersion == 4) ValidateV4Consent(document.RootElement);
                var migrated = JsonNode.Parse(document.RootElement.GetRawText())!;
                var enabled = oldVersion == 2 && document.RootElement.GetProperty("BreakRemindersEnabled").GetBoolean();
                migrated.AsObject().Remove("BreakRemindersEnabled");
                migrated["Version"] = 5;
                if (oldVersion < 3) migrated["ReminderStyle"] = (int)(enabled ? ReminderStyle.WindowsNotification : ReminderStyle.Off);
                migrated["SwitchingConsent"] = false;
                migrated["SwitchingConsentBuild"] = "";
                migrated["SwitchingConsentFamily"] = "";
                settings = migrated.Deserialize<WindowsSettings>(Options) ?? throw new JsonException();
            }
            else settings = document.RootElement.Deserialize<WindowsSettings>(Options) ?? throw new JsonException();
            settings.Validate(); Status = SettingsStatus.Ready; return settings;
        }
        catch (FileNotFoundException) { Status = SettingsStatus.Missing; }
        catch (DirectoryNotFoundException) { Status = SettingsStatus.Missing; }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { Status = SettingsStatus.Invalid; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = SettingsStatus.Unavailable; }
        return new();
    }

    private static void ValidateV4Consent(JsonElement legacy)
    {
        var consent = legacy.GetProperty("SwitchingConsent").GetBoolean();
        var scope = legacy.GetProperty("SwitchingConsentBuild").GetString();
        if (scope is null || scope.Length > 40 ||
            consent && (scope.Length == 0 || scope != legacy.GetProperty("Build").GetString() ||
                !legacy.GetProperty("Consent").GetBoolean() || legacy.GetProperty("Personal").ValueKind == JsonValueKind.Null ||
                legacy.GetProperty("Work").ValueKind == JsonValueKind.Null) || !consent && scope.Length != 0)
            throw new JsonException();
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
