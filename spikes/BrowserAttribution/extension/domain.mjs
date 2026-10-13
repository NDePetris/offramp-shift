export function domainOf(raw) {
  try {
    const url = new URL(raw);
    if (url.protocol !== "https:" && url.protocol !== "http:") return null;
    const host = url.hostname.toLowerCase();
    return host.length <= 253 && /^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z](?:[a-z0-9-]{0,61}[a-z0-9])?$/.test(host) ? host : null;
  } catch { return null; }
}
export async function sample(api, epoch) {
  const start = epoch();
  const empty = { windowId: -1, private: false, focused: false, stable: false, domain: null };
  try {
    const w = await api.windows.getLastFocused({ windowTypes: ["normal"] });
    if (w.incognito || !w.focused) return empty;
    const [a] = await api.tabs.query({ active: true, windowId: w.id });
    if (!a || a.incognito || a.pendingUrl || a.status !== "complete") return empty;
    const domain = domainOf(a.url);
    const [b] = await api.tabs.query({ active: true, windowId: w.id });
    const end = await api.windows.get(w.id);
    if (start !== epoch() || !end.focused || end.incognito || !b || b.id !== a.id ||
        b.incognito || b.pendingUrl || b.status !== "complete" || !domain || domainOf(b.url) !== domain) return empty;
    return { windowId: w.id, private: false, focused: true, stable: true, domain };
  } catch { return empty; }
}
