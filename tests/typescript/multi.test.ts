// The clients of two assemblies against the real page runtime. tsc checks
// the index (its imports end in .js, as bundlers take them); Node runs the
// modules it re-exports.
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { beforeEach, test } from "node:test";
import type * as Index from "./generated/multi/oriel.ts";
import { CallError, Notes } from "./generated/multi/oriel/Notes.App.ts";
import { CallError as SyncCallError, Sync, type State } from "./generated/multi/oriel/Sync.ts";

// The index exports what the modules do.
const _index: typeof Index = { CallError, Notes, Sync, onFilesDropped: (() => () => {}) as typeof Index.onFilesDropped };
void _index;

type Message = { t: string; id?: number; m?: string };
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

test("calls the services of both assemblies", async () => {
  const state = Sync.now();
  await Promise.resolve();
  const call = sent.at(-1)!;
  assert.equal(call.m, "Sync.Now");
  page.receive({ t: "result", id: call.id, k: page.token, v: "busy" });
  const now: State = await state;
  assert.equal(now, "busy");
});

test("one CallError for the errors of every assembly", async () => {
  assert.equal(CallError, SyncCallError);
  const locked = Notes.lock();
  await Promise.resolve();
  page.receive({ t: "error", id: sent.at(-1)!.id, k: page.token, e: "locked", c: "locked", d: { by: "ada" } });
  await assert.rejects(locked, (err: unknown) => {
    assert.ok(err instanceof CallError);
    assert.ok(err instanceof SyncCallError);
    assert.equal(err.code, "locked");
    assert.deepEqual(err.data, { by: "ada" });
    return true;
  });
});
