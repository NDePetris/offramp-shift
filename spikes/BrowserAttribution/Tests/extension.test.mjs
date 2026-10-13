import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { domainOf, sample } from "../extension/domain.mjs";

test("domain-only extraction and unsupported schemes", () => {
  assert.equal(domainOf("https://name:secret@Example.COM:8443/private?q=secret#secret"), "example.com");
  assert.equal(domainOf("https://bücher.example/path"), "xn--bcher-kva.example");
  for (const raw of [undefined, "chrome://settings/", "edge://settings/", "file:///secret", "about:blank", "chrome-extension://abc/", "https://127.0.0.1/", "https://localhost/", "bad"]) assert.equal(domainOf(raw), null);
});
const tab = { id: 2, url: "https://example.com/secret", status: "complete", incognito: false };
function api(first = tab, second = tab, window = { id: 7, focused: true, incognito: false }) {
  let count = 0;
  return { windows: { getLastFocused: async () => window, get: async () => window }, tabs: { query: async () => [count++ ? second : first] } };
}
test("positive and privacy schema", async () => {
  const result = await sample(api(), () => 0);
  assert.deepEqual(result, { windowId: 7, private: false, focused: true, stable: true, domain: "example.com" });
  assert(!JSON.stringify(result).includes("secret"));
});
test("private and background windows do not query tab data", async () => {
  for (const window of [{ id: 7, focused: false }, { id: 7, focused: true, incognito: true }]) {
    const fake = api(tab, tab, window); fake.tabs.query = () => { throw new Error("must not query"); };
    assert.equal((await sample(fake, () => 0)).domain, null);
  }
});
test("navigation, permission and tab races reject", async () => {
  for (const first of [{ ...tab, url: undefined }, { ...tab, pendingUrl: "https://example.org/" }, { ...tab, status: "loading" }, { ...tab, incognito: true }]) assert.equal((await sample(api(first), () => 0)).domain, null);
  for (const second of [{ ...tab, id: 3 }, { ...tab, url: "https://example.org/" }, { ...tab, pendingUrl: "https://example.org/" }, { ...tab, incognito: true }]) assert.equal((await sample(api(tab, second), () => 0)).domain, null);
  let epoch = 0;
  const changing = api(); changing.windows.get = async () => { epoch++; return { focused: true }; };
  assert.equal((await sample(changing, () => epoch)).domain, null);
  const denied = api(); denied.tabs.query = async () => { throw new Error("permission denied"); };
  assert.equal((await sample(denied, () => 0)).domain, null);
});
test("minimal manifest", () => {
  const m = JSON.parse(fs.readFileSync(new URL("../extension/manifest.json", import.meta.url)));
  assert.equal(m.manifest_version, 3); assert.equal(m.incognito, "not_allowed");
  assert.deepEqual(m.permissions, ["tabs", "nativeMessaging"]);
  assert.equal(m.host_permissions, undefined); assert.equal(m.content_scripts, undefined);
});
