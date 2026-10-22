using System.IO;
using System.Text.Json;

namespace OfframpShift.App;

// App-owned UI preference only; never part of WindowsSettings or accounting.
internal sealed class UiPreferences(string directory)
{
    private string PathName => Path.Combine(directory, "ui-preferences.json");
    internal bool ShowDiagnostics { get; private set; }
    internal string Error { get; private set; } = "";
    internal Action? BeforeReplace { get; set; }
    private byte[]? confirmedFile;
    private bool writable;

    internal void Load()
    {
        ShowDiagnostics = false; writable = false; Error = "";
        try
        {
            confirmedFile = File.ReadAllBytes(PathName);
            if (confirmedFile.Length > 4096) throw new JsonException();
            using var document = JsonDocument.Parse(confirmedFile);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
            var fields = root.EnumerateObject().ToArray();
            if (fields.Length != 2 || fields.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != 2 ||
                !root.TryGetProperty("Version", out var version) || !version.TryGetInt32(out var number) || number != 1 ||
                !root.TryGetProperty("ShowDiagnostics", out var enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new JsonException();
            ShowDiagnostics = enabled.GetBoolean(); writable = true;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        { confirmedFile = null; writable = true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { Error = "UI preferences are unreadable or unsupported. The original file is preserved; Diagnostics is off."; }
    }

    internal bool Save(bool enabled)
    {
        if (!writable) { Error = "UI preferences cannot be changed while the existing file is unreadable or unsupported. The original file is preserved."; return false; }
        var temporary = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            // Refuse to overwrite an externally changed file, including corruption.
            var current = File.Exists(PathName) ? File.ReadAllBytes(PathName) : null;
            if (current is null != (confirmedFile is null) || current is not null && !current.SequenceEqual(confirmedFile!))
                throw new IOException("UI preferences changed externally.");
            Directory.CreateDirectory(directory);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, ShowDiagnostics = enabled });
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(true); }
            BeforeReplace?.Invoke();
            if (confirmedFile is null) File.Move(temporary, PathName);
            else File.Replace(temporary, PathName, null);
            confirmedFile = bytes; ShowDiagnostics = enabled; Error = ""; return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { Error = "UI preferences could not be saved. The confirmed preference and Diagnostics window are unchanged. Check local storage access or restart after reviewing the file."; return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
