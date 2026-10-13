using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BrowserAttribution;

// Fixed DTO: never retain arbitrary JSON or browser tab objects in state/logs.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Message
{
    public int V { get; init; } = 1;
    public string Type { get; init; } = "";
    public string Session { get; init; } = "";
    public string Nonce { get; init; } = "";
    public int WindowId { get; init; } = -1;
    public bool Private { get; init; }
    public bool Focused { get; init; }
    public bool Stable { get; init; }
    public string? Domain { get; init; }
    public long Parent { get; init; }
}

public static class Wire
{
    public const int Limit = 4096;
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static bool Token(string value) => Regex.IsMatch(value, @"\A[a-f0-9]{32}\z", RegexOptions.CultureInvariant);
    public static bool Domain(string? value) => value is { Length: <= 253 } &&
        Regex.IsMatch(value, @"\A(?=.{1,253}\z)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z](?:[a-z0-9-]{0,61}[a-z0-9])?\z", RegexOptions.CultureInvariant);
    public static Message Parse(byte[] data)
    {
        var m = JsonSerializer.Deserialize<Message>(data, Json) ?? throw new InvalidDataException();
        if (m.V != 1 || !Token(m.Session) || (m.Nonce != "" && !Token(m.Nonce)) ||
            (m.Domain != null && !Domain(m.Domain)) || m.Type is not ("hello" or "bind" or "invalidate" or "sample" or "challenge" or "bound"))
            throw new InvalidDataException("Invalid protocol");
        return m;
    }
    public static async Task<Message?> Read(Stream stream, CancellationToken ct)
    {
        byte[] header = new byte[4];
        int first = await stream.ReadAsync(header.AsMemory(0, 1), ct);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), ct);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > Limit) throw new InvalidDataException("Frame size");
        byte[] data = new byte[length];
        await stream.ReadExactlyAsync(data, ct);
        return Parse(data);
    }
    public static async Task Write(Stream stream, Message m, CancellationToken ct)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(m, Json);
        if (data.Length > Limit) throw new InvalidDataException("Frame size");
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(data, ct);
        await stream.FlushAsync(ct);
    }
}
