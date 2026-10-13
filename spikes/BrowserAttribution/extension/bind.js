const status = document.getElementById("status");
try {
  const { session } = await chrome.runtime.sendMessage({ type: "session" });
  const window = await chrome.windows.getCurrent();
  if (window.incognito) throw new Error();
  const port = chrome.runtime.connectNative("com.offramp_shift.pr0b");
  const timeout = setTimeout(() => { status.textContent = "Binding timed out. Check monitor and registration."; port.disconnect(); }, 3000);
  port.onDisconnect.addListener(() => { void chrome.runtime.lastError; clearTimeout(timeout); if (status.textContent.startsWith("Binding this")) status.textContent = "Disconnected. Start monitor, check registration, then reopen."; });
  port.onMessage.addListener(m => {
    clearTimeout(timeout);
    status.textContent = m.type === "bound" && m.session === session && m.windowId === window.id ?
      "Bound. Close this popup and use this window." : "Binding rejected. Attribution remains application-only.";
    port.disconnect();
  });
  port.postMessage({ v: 1, type: "bind", session, windowId: window.id, private: false });
} catch { status.textContent = "Unavailable. Attribution remains application-only."; }
