// Tests of the page runtime, src/Oriel/Bridge/bridge.js.
// Run with `node --test tests/bridge`.
const { test } = require("node:test");
const assert = require("node:assert/strict");
const { createRuntime, findTransport, start } = require("../../src/Oriel/Bridge/bridge.js");

/** A runtime whose posted messages are kept, parsed. */
function setup() {
  const sent = [];
  const page = createRuntime((m) => sent.push(JSON.parse(m)));
  return { ...page, sent };
}

test("says hello with its token first", () => {
  const { sent, token } = setup();
  assert.deepEqual(sent, [{ t: "hello", k: token }]);
  assert.match(token, /^[0-9a-f]{24}$/);
});

test("pages get tokens of their own", () => {
  assert.notEqual(setup().token, setup().token);
});

test("sends calls with increasing ids", () => {
  const { runtime, sent, token } = setup();
  runtime.call("Greeter.Greet", ["Ada"]);
  runtime.call("Greeter.Reset");
  assert.deepEqual(sent.slice(1), [
    { t: "call", id: 1, k: token, m: "Greeter.Greet", a: ["Ada"] },
    { t: "call", id: 2, k: token, m: "Greeter.Reset", a: [] },
  ]);
});

test("resolves a call with its result", async () => {
  const { runtime, receive, token } = setup();
  const result = runtime.call("Greeter.Greet", ["Ada"]);
  receive(JSON.stringify({ t: "result", id: 1, k: token, v: "Hello, Ada" }));
  assert.equal(await result, "Hello, Ada");
});

test("resolves a call returning nothing with undefined", async () => {
  const { runtime, receive, token } = setup();
  const result = runtime.call("Greeter.Reset");
  receive({ t: "result", id: 1, k: token });
  assert.equal(await result, undefined);
});

test("rejects a failed call with a CallError naming the method", async () => {
  const { runtime, receive, token } = setup();
  const result = runtime.call("Decks.Open", ["x"]);
  receive({ t: "error", id: 1, k: token, e: "not a deck" });
  await assert.rejects(result, (err) => {
    assert.equal(err.name, "CallError");
    assert.equal(err.method, "Decks.Open");
    assert.equal(err.message, "not a deck");
    assert.equal(err.code, null);
    assert.equal(err.data, null);
    return true;
  });
});

test("gives a CallError the code and data of a service error", async () => {
  const { runtime, receive, token } = setup();
  const result = runtime.call("Decks.Save", ["x"]);
  receive({ t: "error", id: 1, k: token, e: "x changed", c: "conflict", d: { version: 2 } });
  await assert.rejects(result, (err) => {
    assert.equal(err.code, "conflict");
    assert.deepEqual(err.data, { version: 2 });
    return true;
  });
});

test("rejects a failed call whose error has no message with one of its own", async () => {
  // Found by fuzzing: String({ toString: 0 }) threw, and the call stayed pending.
  const { runtime, receive, token } = setup();
  const result = runtime.call("Decks.Open", ["x"]);
  assert.doesNotThrow(() => receive(JSON.stringify({ t: "error", id: 1, k: token, e: { toString: 0 } })));
  await assert.rejects(result, { name: "CallError", message: "Decks.Open failed" });
});

test("ignores replies meant for another page", async () => {
  const { runtime, receive, token } = setup();
  const result = runtime.call("Greeter.Greet", ["Ada"]);
  receive({ t: "result", id: 1, k: "someone else", v: "wrong" });
  receive({ t: "result", id: 1, k: token, v: "right" });
  assert.equal(await result, "right");
});

test("ignores replies to no call and malformed messages", () => {
  const { receive, token } = setup();
  assert.doesNotThrow(() => {
    receive({ t: "result", id: 99, k: token, v: 1 });
    receive("{not json");
    receive(null);
    receive(42);
    receive({ t: "future" });
  });
});

test("rejects a call the transport refuses", async () => {
  let first = true;
  const { runtime } = createRuntime(() => {
    if (first) return void (first = false); // the hello
    throw new Error("gone");
  });
  await assert.rejects(runtime.call("A.B"), /gone/);
});

test("delivers events to their listeners until they stop", () => {
  const { runtime, receive } = setup();
  const got = [];
  const stop = runtime.on("Clock.Tick", (v) => got.push(v));
  runtime.on("Clock.Other", () => got.push("other"));
  receive({ t: "event", n: "Clock.Tick", v: 1 });
  stop();
  receive({ t: "event", n: "Clock.Tick", v: 2 });
  assert.deepEqual(got, [1]);
});

test("a throwing listener does not keep the others from an event", () => {
  const { runtime, receive } = setup();
  const got = [];
  runtime.on("E", () => {
    throw new Error("listener bug");
  });
  runtime.on("E", (v) => got.push(v));

  // The error is rethrown later, out of the runtime's way.
  const setTimeout = globalThis.setTimeout;
  let rethrow;
  globalThis.setTimeout = (fn) => void (rethrow = fn);
  try {
    receive({ t: "event", n: "E", v: "x" });
  } finally {
    globalThis.setTimeout = setTimeout;
  }

  assert.deepEqual(got, ["x"]);
  assert.throws(rethrow, /listener bug/);
});

test("acknowledges coalesced events once per frame, all at once", () => {
  const sent = [];
  const frames = [];
  const { runtime, receive, token } = createRuntime((m) => sent.push(JSON.parse(m)), null, (f) => frames.push(f));
  const got = [];
  runtime.on("Pointer.Moved", (v) => got.push(v));
  receive({ t: "event", n: "Pointer.Moved", v: 1, c: 1 });
  receive({ t: "event", n: "Clock.Tick", v: 2 }); // not coalesced: no acknowledgement
  receive({ t: "event", n: "Pointer.Moved", v: 3, c: 1 });
  receive({ t: "event", n: "Zoom.Changed", v: 4, c: 1 }); // acknowledged though no one listens
  assert.deepEqual(got, [1, 3]);
  assert.equal(frames.length, 1);
  assert.equal(sent.length, 1); // the hello

  frames.shift()();
  assert.deepEqual(sent.slice(1), [{ t: "ack", k: token, n: ["Pointer.Moved", "Zoom.Changed"] }]);

  receive({ t: "event", n: "Pointer.Moved", v: 5, c: 1 });
  assert.equal(frames.length, 1); // the next frame's
});

test("ignores coalesced events without a name", () => {
  const frames = [];
  const { receive } = createRuntime(() => {}, null, (f) => frames.push(f));
  receive({ t: "event", n: 7, v: 1, c: 1 });
  assert.equal(frames.length, 0);
});

test("the runtime cannot be changed by pages", () => {
  const { runtime } = setup();
  assert.ok(Object.isFrozen(runtime));
});

/** A keydown as the page's listeners leave it. */
function key(keyCode, mods = {}, prevented = false) {
  return {
    keyCode,
    ctrlKey: !!mods.ctrl,
    shiftKey: !!mods.shift,
    altKey: !!mods.alt,
    isComposing: false,
    getModifierState: (name) => name === "AltGraph" && !!mods.altGraph,
    defaultPrevented: prevented,
  };
}

const turn = () => new Promise((resolve) => setTimeout(resolve));

test("reports the shortcuts the page left alone, after its listeners", async () => {
  const { keyDown, sent, token } = setup();
  const e = key(83, { ctrl: true, shift: true });
  keyDown(e);
  assert.equal(sent.length, 1); // not before the page's listeners ran
  await turn();
  keyDown(key(116));
  keyDown(key(70, { alt: true }));
  await turn();
  assert.deepEqual(sent.slice(1), [
    { t: "key", k: token, c: 83, m: 3 },
    { t: "key", k: token, c: 116, m: 0 },
    { t: "key", k: token, c: 70, m: 4 },
  ]);
});

test("leaves the keys the page handled, plain keys and AltGr to the page", async () => {
  const { keyDown, sent } = setup();
  const handled = key(90, { ctrl: true });
  keyDown(handled);
  handled.defaultPrevented = true; // a listener after the runtime's
  keyDown(key(90));
  keyDown(key(48, { ctrl: true, alt: true, altGraph: true }));
  keyDown({ ...key(65, { ctrl: true }), isComposing: true });
  keyDown(key(17, { ctrl: true })); // Ctrl itself
  keyDown(key(18, { alt: true }));
  await turn();
  assert.equal(sent.length, 1);
});

/** A drag event carrying `types`, and `files` for a drop. */
function drag(types, files = []) {
  return {
    dataTransfer: { types, files, dropEffect: "none" },
    clientX: 12,
    clientY: 34,
    defaultPrevented: false,
    preventDefault() {
      this.defaultPrevented = true;
    },
  };
}

test("lets files be dropped anywhere, and leaves other drags to the page", () => {
  const { dragOver } = setup();
  const files = drag(["Files"]);
  dragOver(files);
  assert.equal(files.defaultPrevented, true);
  assert.equal(files.dataTransfer.dropEffect, "copy");
  const text = drag(["text/plain"]);
  dragOver(text);
  assert.equal(text.defaultPrevented, false);
  assert.equal(text.dataTransfer.dropEffect, "none");
});

test("sends dropped files with where they were dropped", () => {
  const withFiles = [];
  const page = createRuntime(() => {}, (message, files) => withFiles.push([JSON.parse(message), files]));
  const a = { name: "a.txt" };
  const b = { name: "b.png" };
  const e = drag(["Files"], [a, b]);
  page.drop(e);
  assert.equal(e.defaultPrevented, true); // the webview does not open the file instead of the page
  assert.deepEqual(withFiles, [[{ t: "drop", k: page.token, x: 12, y: 34 }, [a, b]]]);
});

test("ignores drops without files, and drops on a runtime that cannot send files", () => {
  const withFiles = [];
  const page = createRuntime(() => {}, (message, files) => withFiles.push(files));
  const text = drag(["text/plain"]);
  page.drop(text);
  assert.equal(text.defaultPrevented, false);
  page.drop(drag(["Files"], []));
  assert.equal(withFiles.length, 0);

  const old = setup(); // no postFiles: an older runtime
  const e = drag(["Files"], [{ name: "a.txt" }]);
  old.drop(e);
  assert.equal(e.defaultPrevented, true);
  assert.equal(old.sent.length, 1); // the hello only
});

test("gives the page the paths of dropped files, as an event", () => {
  const { runtime, receive } = setup();
  const drops = [];
  runtime.on("oriel:filesDropped", (drop) => drops.push(drop));
  receive(JSON.stringify({ t: "event", n: "oriel:filesDropped", v: { paths: ["C:\a.txt"], x: 1, y: 2 } }));
  assert.deepEqual(drops, [{ paths: ["C:\a.txt"], x: 1, y: 2 }]);
});

/** A page's global object, with the webview's channel `channel` adds. */
function fakePage(channel) {
  const page = { listeners: {}, addEventListener: (type, f, capture) => (page.listeners[type] = { f, capture }) };
  page.top = page;
  channel(page);
  return page;
}

/** A WebView2 page: chrome.webview, as Windows gives it. */
function webView2Page({ withFiles = true } = {}) {
  const posted = [];
  const page = fakePage((p) => {
    p.chrome = {
      webview: {
        postMessage: (m) => posted.push(JSON.parse(m)),
        addEventListener: (type, f) => (p.webviewListener = { type, f }),
      },
    };
    if (withFiles) p.chrome.webview.postMessageWithAdditionalObjects = (m, files) => posted.push({ ...JSON.parse(m), files });
  });
  return { page, posted };
}

/** A WebKit page: the script message handler "oriel", as macOS and Linux give it. */
function webKitPage() {
  const posted = [];
  const page = fakePage((p) => {
    p.webkit = { messageHandlers: { oriel: { postMessage: (m) => posted.push(JSON.parse(m)) } } };
  });
  return { page, posted };
}

test("starts in a WebView2 page: posts through chrome.webview, receives its messages", async () => {
  const { page, posted } = webView2Page();
  start(page);
  assert.equal(typeof page.oriel.call, "function");
  assert.equal(posted[0].t, "hello");
  assert.equal(page.webviewListener.type, "message");
  const result = page.oriel.call("Greeter.Greet", ["Ada"]);
  page.webviewListener.f({ data: JSON.stringify({ t: "result", id: 1, k: posted[0].k, v: "Hello" }) });
  assert.equal(await result, "Hello");
  assert.deepEqual(Object.keys(page.listeners).sort(), ["dragover", "drop", "keydown"]);
  assert.ok(Object.values(page.listeners).every((l) => l.capture));
});

test("starts in a WebKit page: posts through the oriel handler, receives through __orielReceive", async () => {
  const { page, posted } = webKitPage();
  start(page);
  assert.equal(posted[0].t, "hello");
  const result = page.oriel.call("Greeter.Greet", ["Ada"]);
  page.__orielReceive(JSON.stringify({ t: "result", id: 1, k: posted[0].k, v: "Hello" }));
  assert.equal(await result, "Hello");
  assert.equal(Object.keys(page).includes("__orielReceive"), false); // not enumerable
  assert.throws(() => {
    "use strict";
    page.__orielReceive = () => {};
  }); // nor replaceable
});

test("sends files only where the webview can", () => {
  assert.equal(typeof findTransport(webView2Page().page).postFiles, "function");
  assert.equal(findTransport(webView2Page({ withFiles: false }).page).postFiles, null);
  assert.equal(findTransport(webKitPage().page).postFiles, null);
});

test("does nothing outside an Oriel window, in frames, or twice", () => {
  const plain = fakePage(() => {});
  start(plain);
  assert.equal(plain.oriel, undefined);
  assert.equal(findTransport(plain), null);

  const { page: frame, posted } = webView2Page();
  frame.top = {};
  start(frame);
  assert.equal(frame.oriel, undefined);
  assert.equal(posted.length, 0);

  const { page, posted: twice } = webKitPage();
  start(page);
  start(page);
  assert.equal(twice.length, 1);
});
