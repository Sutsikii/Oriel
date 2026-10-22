// Fuzz tests of the page runtime's receive, src/Oriel/Bridge/bridge.js:
// whatever arrives, nothing throws, and only the reply to a call settles it,
// once. Each run uses a seed of its own, printed when it fails;
// BRIDGE_FUZZ_SEED replays one, and BRIDGE_FUZZ_RUNS sets how many.
const { test } = require("node:test");
const assert = require("node:assert/strict");
const { createRuntime } = require("../../src/Oriel/Bridge/bridge.js");

const runs = Number(process.env.BRIDGE_FUZZ_RUNS ?? 3000);
const firstSeed = Number(process.env.BRIDGE_FUZZ_SEED ?? Date.now() % 2 ** 31);

/** mulberry32: a small generator of numbers in [0, 1), the same for a seed. */
function random(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 2 ** 32;
  };
}

function generators(next) {
  const int = (n) => Math.floor(next() * n);
  const pick = (items) => items[int(items.length)];

  function value(depth) {
    switch (int(depth > 0 ? 9 : 6)) {
      case 0: return null;
      case 1: return next() < 0.5;
      case 2: return pick([0, -0, 1, -1, 1.5, 2 ** 53, -(2 ** 60), 1e308]);
      case 3: return pick(["", "x", "\ud800", " ", "__proto__", "constructor", "toString"]);
      case 4: return pick([undefined, NaN, Infinity]);
      case 5: return int(10);
      case 6: return Array.from({ length: int(4) }, () => value(depth - 1));
      default: {
        const o = {};
        for (let i = int(4); i > 0; i--)
          o[pick(["a", "then", "toString", "valueOf", "__proto__", "constructor", "length", "\ud800"])] = value(depth - 1);
        return o;
      }
    }
  }

  /** A message with the fields the runtime reads, each right or wrong. */
  function message(token, ids) {
    const m = {};
    const field = (name, choices) => {
      if (next() < 0.85) m[name] = pick(choices);
    };
    field("t", ["result", "error", "event", "call", "hello", "RESULT", 1, null, value(1)]);
    field("k", [token, token, token, "other", token + " ", 0, null, value(1)]);
    field("id", [...ids, ...ids, 0, -1, 1e9, "1", String(ids[0]), 1.5, null, value(1)]);
    field("v", [value(2)]);
    field("e", ["failed", "", value(2)]);
    field("c", ["conflict", "", null, value(1)]);
    field("d", [value(2)]);
    field("n", ["E.Tick", "E.Other", "", value(1)]);
    return m;
  }

  /** What arrives: a message, its JSON, broken JSON, or anything else. */
  function received(token, ids) {
    const m = message(token, ids);
    switch (int(5)) {
      case 0: return m;
      case 1:
      case 2: return JSON.stringify(m) ?? "null";
      case 3: {
        const text = JSON.stringify(m) ?? "";
        const at = int(text.length + 1);
        return text.slice(0, at) + pick(["", "{", "\"", "\\", "\ud800", ","]) + text.slice(at + int(3));
      }
      default: return value(2);
    }
  }

  return { int, received };
}

/** Lets settled promises run their handlers. */
const settle = () => new Promise((resolve) => setImmediate(resolve));

test("receive never throws, and only a call's reply settles it, once", async () => {
  for (let run = 0; run < runs; run++) {
    const seed = firstSeed + run;
    const { int, received } = generators(random(seed));
    const page = createRuntime(() => {});
    const events = [];
    page.runtime.on("E.Tick", (v) => events.push(v));

    // Calls, each with how it settled: times, and the last outcome.
    const calls = new Map();
    for (let i = 0, n = 1 + int(5); i < n; i++) {
      const call = { times: 0, outcome: undefined };
      page.runtime.call(`S.M${i}`, [i]).then(
        (v) => { call.times++; call.outcome = { result: v }; },
        (err) => { call.times++; call.outcome = { error: err }; },
      );
      calls.set(i + 1, call);
    }
    const ids = [...calls.keys()];
    const replied = new Set();

    for (let i = 0, n = 1 + int(30); i < n; i++) {
      const raw = received(page.token, ids);
      assert.doesNotThrow(() => page.receive(raw), `seed ${seed}: ${String(JSON.stringify(raw))}`);

      // The model: a reply of this page to a pending call settles it.
      let m;
      try {
        m = typeof raw === "string" ? JSON.parse(raw) : raw;
      } catch {
        m = undefined;
      }
      const isReply = m && typeof m === "object" && (m.t === "result" || m.t === "error") && m.k === page.token;
      const call = isReply && calls.get(m.id);
      if (call && !replied.has(m.id)) {
        replied.add(m.id);
        await settle();
        assert.equal(call.times, 1, `seed ${seed}: call ${m.id} not settled by ${String(JSON.stringify(raw))}`);
        if (m.t === "result") {
          assert.deepEqual(call.outcome, { result: m.v }, `seed ${seed}`);
        } else {
          const err = call.outcome.error;
          assert.ok(err instanceof Error && err.name === "CallError", `seed ${seed}`);
          assert.equal(typeof err.message, "string", `seed ${seed}`);
          assert.equal(err.method, `S.M${m.id - 1}`, `seed ${seed}`);
        }
      }
    }

    await settle();
    for (const [id, call] of calls)
      assert.equal(call.times, replied.has(id) ? 1 : 0, `seed ${seed}: call ${id} settled ${call.times} times`);
  }
});
