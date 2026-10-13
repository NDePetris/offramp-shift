// Real child processes/stdio/named pipe, synthetic caller. No browser validation.
import { spawn } from "node:child_process";
import { once } from "node:events";
import { fileURLToPath } from "node:url";
import assert from "node:assert/strict";
const root = fileURLToPath(new URL("../../..", import.meta.url));
const exe = fileURLToPath(new URL("../Probe/bin/Release/net10.0/BrowserProbe.exe", import.meta.url));
const env = { ...process.env, DOTNET_ROOT: process.env.DOTNET_ROOT || root + "\\.tools\\dotnet" };
const children = [];
function child(args) { const p = spawn(exe, args, { env, windowsHide: true, stdio: ["pipe","pipe","pipe"] }); p.stderr.on("data", b => process.stderr.write(b)); children.push(p); return p; }
function until(check, register, label) {
  return new Promise((resolve, reject) => {
    const deadline = setTimeout(() => reject(new Error("Timeout: " + label)), 5000);
    const callback = () => { if (check()) { clearTimeout(deadline); resolve(); } };
    register(callback); callback();
  });
}
function framed(p, m) {
  const data = Buffer.from(JSON.stringify(m)); const header = Buffer.alloc(4); header.writeUInt32LE(data.length); p.stdin.write(Buffer.concat([header,data]));
}
try {
  // Exercise README's dotnet-DLL monitor and the registered executable host.
  const monitor = spawn(env.DOTNET_ROOT + "\\dotnet.exe", [exe.replace(/\.exe$/, ".dll"), "monitor"], { env, windowsHide: true, stdio: ["pipe","pipe","pipe"] });
  monitor.stderr.on("data", b => process.stderr.write(b)); children.push(monitor);
  const observations = []; const watchers = []; let text = "";
  monitor.stdout.on("data", chunk => {
    text += chunk.toString(); let index;
    while ((index = text.indexOf("\n")) >= 0) { observations.push(JSON.parse(text.slice(0,index))); text = text.slice(index+1); }
    watchers.forEach(f => f());
  });
  await until(() => observations.length > 0, f => watchers.push(f), "monitor ready");
  const duplicate = child(["monitor"]); const duplicateExit = await once(duplicate, "exit"); assert.equal(duplicateExit[0], 1);
  const host = child(["chrome-extension://" + "a".repeat(32) + "/", "--parent-window=0"]);
  const messages = [], listeners = []; let bytes = Buffer.alloc(0);
  host.stdout.on("data", chunk => {
    bytes = Buffer.concat([bytes,chunk]);
    while (bytes.length >= 4 && bytes.length >= 4+bytes.readUInt32LE(0)) {
      const size = bytes.readUInt32LE(0); assert(size <= 4096); messages.push(JSON.parse(bytes.subarray(4,4+size))); bytes=bytes.subarray(4+size);
    }
    listeners.forEach(f => f());
  });
  const session = "a".repeat(32);
  framed(host, { v:1, type:"hello", session });
  await until(() => messages.some(m => m.type === "challenge"), f => listeners.push(f), "challenge bridged");
  const challenge = messages.find(m => m.type === "challenge");
  framed(host, { v:1, type:"sample", session, nonce:challenge.nonce, windowId:7, focused:true, stable:true, domain:"example.com" });
  const start = observations.length;
  await until(() => observations.length >= start+3, f => watchers.push(f), "classification after unbound sample");
  assert(observations.every(o => o.domain === null));
  const bad = Buffer.alloc(4); bad.writeInt32LE(4097); host.stdin.write(bad);
  const exit = await once(host,"exit"); assert.equal(exit[0],1);
  const after = observations.length;
  await until(() => observations.length >= after+2, f => watchers.push(f), "disconnect fallback");
  assert(observations.every(o => o.domain === null));
  // Native host restart creates a new stream, never restores a binding/domain.
  const restarted = child(["chrome-extension://"+"a".repeat(32)+"/", "--parent-window=0"]);
  restarted.stdout.once("data", () => framed(restarted, { v:1,type:"invalidate",session:"b".repeat(32) }));
  framed(restarted, { v:1,type:"hello",session:"b".repeat(32) });
  const end = observations.length;
  await until(() => observations.length >= end+3, f => watchers.push(f), "restart fallback");
  assert(observations.every(o => o.domain === null));
  console.log("PASS: native process/stdio/pipe round trip, singleton, unbound rejection, oversized-frame exit, disconnect and restart. Synthetic caller; no browser validation.");
} finally { for (const p of children) { if (p.exitCode === null) { p.kill(); await once(p,"exit"); } } }
