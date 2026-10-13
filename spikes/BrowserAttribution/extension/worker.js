import { sample } from "./domain.mjs";
const HOST = "com.offramp_shift.pr0b";
const token = () => crypto.randomUUID().replaceAll("-", "");
let session = token(), port = null, epoch = 0, busy = false;
function send(value) { try { port?.postMessage({ v: 1, session, ...value }); } catch { port = null; } }
function invalidate(windowId = -1) { epoch++; send({ type: "invalidate", windowId }); }
function connect() {
  if (port) return;
  session = token();
  const p = chrome.runtime.connectNative(HOST); port = p;
  p.onDisconnect.addListener(() => {
    void chrome.runtime.lastError;
    if (port === p) { port = null; epoch++; session = token(); }
    // Open the popup to reconnect and rebind; no retry storm.
  });
  p.onMessage.addListener(async m => {
    if (port !== p || m.v !== 1 || m.type !== "challenge" || m.session !== session ||
        !/^[a-f0-9]{32}$/.test(m.nonce) || busy) return;
    busy = true;
    try {
      const result = await sample(chrome, () => epoch);
      if (port === p && m.session === session) send({ type: "sample", nonce: m.nonce, ...result });
    } finally { busy = false; }
  });
  send({ type: "hello" });
}
chrome.windows.onFocusChanged.addListener(() => invalidate());
chrome.windows.onRemoved.addListener(id => invalidate(id));
chrome.tabs.onActivated.addListener(() => invalidate());
chrome.tabs.onUpdated.addListener(() => invalidate()); // Never inspect changeInfo or tab objects.
chrome.tabs.onRemoved.addListener(() => invalidate());
chrome.tabs.onAttached.addListener(() => invalidate());
chrome.tabs.onDetached.addListener(() => invalidate());
chrome.runtime.onMessage.addListener((m, sender, reply) => {
  if (sender.id !== chrome.runtime.id || sender.url !== chrome.runtime.getURL("bind.html") || m?.type !== "session") return;
  connect(); reply({ session });
});
connect();
