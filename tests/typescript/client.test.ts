// Runs the generated client against the real page runtime, bridge.js, with
// the app's side played by the test.
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { beforeEach, test } from "node:test";
import { CallError, Decks, Greeter } from "./generated/client.ts";

type Message = { t: string; id?: number; k?: string; m?: string; a?: unknown[] };
type Page = { runtime: unknown; receive(message: unknown): void; token: string };

const require = createRequire(import.meta.url);
const { createRuntime } = require("../../src/Oriel/Bridge/bridge.js") as {
  createRuntime(post: (message: string) => void): Page;
};

let sent: Message[] = [];
let page: Page;

beforeEach(() => {
  sent = [];
  page = createRuntime((m) => sent.push(JSON.parse(m)));
  (globalThis as { oriel?: unknown }).oriel = page.runtime;
});

/** The call the page sent last, answered with `reply`. */
function answer(reply: { v?: unknown; e?: string; c?: string; d?: unknown }): Message {
  const call = sent.at(-1)!;
  page.receive({ t: reply.e === undefined ? "result" : "error", id: call.id, k: page.token, ...reply });
  return call;
}

test("calls the method with its arguments and resolves with the result", async () => {
  const greeting = Greeter.greet("Ada");
  await Promise.resolve();
  const call = answer({ v: "Hello, Ada" });
  assert.equal(await greeting, "Hello, Ada");
  assert.equal(call.m, "Greeter.Greet");
  assert.deepEqual(call.a, ["Ada"]);
});

test("leaves out the optional arguments a caller left out", async () => {
  const results = Decks.search("q", undefined, undefined);
  await Promise.resolve();
  assert.deepEqual(answer({ v: [] }).a, ["q"]);
  await results;

  // JSON has no undefined: one between arguments is sent as null, which C#
  // reads as "left out" for parameters that cannot be null.
  const given = Decks.search("q", undefined, "dark");
  await Promise.resolve();
  assert.deepEqual(answer({ v: [] }).a, ["q", null, "dark"]);
  await given;
});

test("rejects with a CallError when the method fails", async () => {
  const opened = Decks.open("/missing");
  await Promise.resolve();
  answer({ e: "no such deck" });
  await assert.rejects(opened, (err: unknown) => {
    assert.ok(err instanceof CallError);
    assert.equal(err.method, "Decks.Open");
    assert.equal(err.message, "no such deck");
    assert.equal(err.code, null);
    assert.equal(err.data, null);
    return true;
  });
});

test("rejects with the code and data of a service error", async () => {
  const saved = Decks.save("/d.json", { title: "D" } as never);
  await Promise.resolve();
  answer({ e: "/d.json changed", c: "conflict", d: { path: "/d.json", version: 4 } });
  await assert.rejects(saved, (err: unknown) => {
    assert.ok(err instanceof CallError);
    assert.equal(err.code, "conflict");
    assert.deepEqual(err.data, { path: "/d.json", version: 4 });
    return true;
  });
});

test("listens to an event until told to stop", () => {
  const titles: string[] = [];
  const stop = Decks.onChanged((deck) => titles.push(deck.title));
  page.receive({ t: "event", n: "Decks.Changed", v: { title: "A" } });
  stop();
  page.receive({ t: "event", n: "Decks.Changed", v: { title: "B" } });
  assert.deepEqual(titles, ["A"]);
});

test("iterates a stream, pulling items as it takes them", async () => {
  const titles: string[] = [];
  const done = (async () => {
    for await (const slide of Decks.slides("/d.json")) titles.push(slide.title);
  })();
  await Promise.resolve();
  const call = sent.at(-1)!;
  assert.deepEqual(call, { t: "call", id: call.id, k: page.token, m: "Decks.Slides", a: ["/d.json"], w: 16 });
  for (let i = 0; i < 10; i++) page.receive({ t: "item", id: call.id, k: page.token, v: { title: `S${i}` } });
  await new Promise((resolve) => setImmediate(resolve)); // the loop takes them
  assert.equal(titles.length, 10);
  assert.deepEqual(sent.slice(-1), [{ t: "pull", id: call.id, k: page.token, n: 8 }]);
  page.receive({ t: "result", id: call.id, k: page.token });
  await done;
});

test("cancels a stream the page stops iterating, and fails one that fails", async () => {
  const first = (async () => {
    for await (const slide of Decks.slides("/a.json")) return slide.title;
  })();
  await Promise.resolve();
  const call = sent.at(-1)!;
  page.receive({ t: "item", id: call.id, k: page.token, v: { title: "A" } });
  assert.equal(await first, "A");
  assert.deepEqual(sent.at(-1), { t: "cancel", id: call.id, k: page.token });

  const failed = (async () => {
    for await (const _ of Decks.slides("/b.json"));
  })();
  await Promise.resolve();
  page.receive({ t: "error", id: sent.at(-1)!.id, k: page.token, e: "gone", c: "conflict", d: { path: "/b.json", version: 1 } });
  await assert.rejects(failed, (err: unknown) => err instanceof CallError && err.method === "Decks.Slides" && err.code === "conflict");
});

test("resolves binary results with typed arrays", async () => {
  const levels = Decks.levels();
  await Promise.resolve();
  const call = sent.at(-1)!;
  (page.receive as (message: unknown, buffer?: ArrayBuffer) => void)(
    { t: "result", id: call.id, k: page.token, b: "Float32Array", n: 4 },
    new Float32Array([0.25]).buffer,
  );
  const result: Float32Array = await levels;
  assert.deepEqual([...result], [0.25]);
});

test("says when the page is not in an Oriel window", async () => {
  delete (globalThis as { oriel?: unknown }).oriel;
  await assert.rejects(Greeter.greet("Ada"), /window\.oriel is missing/);
});
