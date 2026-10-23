// Tests of the page runtime, src/Oriel/Bridge/bridge.js.
// Run with `node --test tests/bridge`.
const { test } = require("node:test");
const assert = require("node:assert/strict");
const { createRuntime } = require("../../src/Oriel/Bridge/bridge.js");

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
