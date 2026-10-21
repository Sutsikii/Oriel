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
function answer(reply: { v?: unknown; e?: string }): Message {
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
    return true;
  });
});

test("says when the page is not in an Oriel window", async () => {
  delete (globalThis as { oriel?: unknown }).oriel;
  await assert.rejects(Greeter.greet("Ada"), /window\.oriel is missing/);
});
