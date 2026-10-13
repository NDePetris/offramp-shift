using System.Buffers.Binary;
using System.Text;
using BrowserAttribution;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; }
var session = new string('a', 32); var nonce = new string('b', 32);
var w = new Window(100, 42, 1000, 0, "chrome", true);
var b = new Binding(session, 7, w);
var q = new Challenge(nonce, 1000, 1, w);
var m = new Message { Type = "sample", Session = session, Nonce = nonce, WindowId = 7, Focused = true, Stable = true, Domain = "example.com" };
var c = new Candidate(b, q, m, 1100);
Check(Policy.Accept(b, q, m, w, 1, 1100), "positive attribution");
foreach (var bad in new[] { m with { Private = true }, m with { Focused = false }, m with { Stable = false }, m with { WindowId = 8 }, m with { Session = new string('c',32) }, m with { Nonce = new string('c',32) }, m with { Domain = null }, m with { Domain = "https://example.com/private" }, m with { Type = "hello" } })
    Check(!Policy.Accept(b,q,bad,w,1,1100), "sample rejection");
foreach (var wrong in new[] { w with { Hwnd = 101 }, w with { Pid = 43 }, w with { ProcessBirth = 1001 }, w with { Generation = 1 }, w with { Application = "msedge" }, w with { Eligible = false } })
    Check(!Policy.Accept(b,q,m,wrong,1,1100), "wrong foreground/profile/window/process/destruction");
Check(!Policy.Accept(b,q,m,w,2,1100), "foreground ABA epoch");
Check(!Policy.Accept(b,q,m,w,1,1401), "stale reply");
Check(!Policy.Accept(b,q,m,w,1,999), "time reversal");
Check(Policy.Classify(w,1,1200,[c]).Domain == "example.com", "visibility");
Check(Policy.Classify(w,1,1601,[c]).Domain == null, "lease expiry");
Check(Policy.Classify(w,1,1200,[]).Domain == null, "disconnect/uninstrumented");
Check(Policy.Classify(w,1,1200,[c,c]).Domain == null, "ambiguity");
Check(Policy.Classify(w,2,1200,[c]).Domain == null, "focus change invalidation");
Check(Policy.Classify(w with { Eligible=false },1,1200,[c]).Domain == null, "unsupported/private foreground");
Check(Policy.Classify(w with { Hwnd=200 },1,1200,[c]).Domain == null, "background window/other desktop");

// Separate logical browser labels: these are simulations, never browser measurements.
foreach (string browser in new[] { "chrome", "msedge" })
{
    int visible = 0;
    for (int i = 0; i < 250; i++)
    {
        var target = w with { Application = browser, Hwnd = 100 + i % 3 };
        var binding = b with { Window = target, WindowId = i % 3 };
        var challenge = q with { Window = target, Issued = i * 1000, FocusEpoch = i };
        var sample = m with { WindowId = i % 3, Domain = i % 2 == 0 ? "example.com" : "example.org" };
        var candidate = new Candidate(binding, challenge, sample, i * 1000 + 100);
        Check(Policy.Classify(target,i,i*1000+150,[candidate]).Domain == sample.Domain, "controlled simulated positive"); visible++;
        Check(Policy.Classify(target with { Hwnd=999 },i,i*1000+150,[candidate]).Domain == null, "simulated background");
        Check(Policy.Classify(target,i+1,i*1000+150,[candidate]).Domain == null, "simulated rapid focus");
        Check(Policy.Classify(target,i,i*1000+601,[candidate]).Domain == null, "simulated stale");
    }
    Console.WriteLine($"SIMULATED {browser}: 250 transitions, {visible} positive, zero wrong domains; synthetic 150 ms classification, not measured latency.");
}
foreach (var domain in new[] { "example.com", "a.b.example.com", "xn--bcher-kva.example" }) Check(Wire.Domain(domain), "valid domain");
foreach (var domain in new[] { "https://example.com", "example.com/path", "example.com?secret", "user@example.com", "localhost", "127.0.0.1", "EXAMPLE.COM", "example..com", "-a.example", "a_.example", "example.com\n" }) Check(!Wire.Domain(domain), "domain-only rejection");
Check(!Wire.Token(session + "\n"), "strict token ending");
using (var stream = new MemoryStream())
{
    await Wire.Write(stream,m,CancellationToken.None); stream.Position=0;
    Check(await Wire.Read(stream,CancellationToken.None) == m, "frame roundtrip");
    Check(await Wire.Read(stream,CancellationToken.None) == null, "EOF");
}
async Task Reject(byte[] bytes)
{
    try { using var s = new MemoryStream(bytes); await Wire.Read(s,CancellationToken.None); throw new Exception("accepted bad frame"); }
    catch (Exception e) when (e is InvalidDataException or EndOfStreamException or System.Text.Json.JsonException) { passed++; }
}
foreach (int length in new[] { -1, 0, Wire.Limit+1, int.MaxValue })
{ byte[] bytes=new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes,length); await Reject(bytes); }
await Reject([1,0]); await Reject([10,0,0,0,123]);
foreach (string json in new[] { "{}", "{\"type\":\"hello\",\"session\":\""+session+"\",\"url\":\"https://example.com/secret\"}", "{\"v\":2,\"type\":\"hello\",\"session\":\""+session+"\"}", "null" })
{ byte[] data = Encoding.UTF8.GetBytes(json); byte[] bytes=new byte[data.Length+4]; BinaryPrimitives.WriteInt32LittleEndian(bytes,data.Length); data.CopyTo(bytes,4); await Reject(bytes); }
Console.WriteLine($"PASS: {passed} policy/protocol assertions. No actual Chrome/Edge validation.");
