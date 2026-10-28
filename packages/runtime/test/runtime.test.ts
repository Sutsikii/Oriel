import assert from "node:assert/strict";
import { beforeEach, test } from "node:test";
import { CallErrorBase, bridge, call, on, onFilesDropped, stream } from "oriel-runtime";

type Fake = { calls: { method: string; args: unknown[] }[]; reply: (method: string) => Promise<unknown>; listeners: Map<string, (p: unknown) => void> };
let fake: Fake;

beforeEach(() => {
  fake = { calls: [], reply: async () => undefined, listeners: new Map() };
  (globalThis as { oriel?: unknown }).oriel = {
    call: (method: string, args: unknown[]) => {
      fake.calls.push({ method, args });
      return fake.reply(method);
    },
    on: (event: string, listener: (p: unknown) => void) => {
      fake.listeners.set(event, listener);
      return () => fake.listeners.delete(event);
    },
    stream: () => ({
      async *[Symbol.asyncIterator]() {
        yield 1;
        yield 2;
      },
    }),
  };
});

test("calls with the arguments given, leaving out those left out", async () => {
  fake.reply = async () => "Hello, Ada";
  assert.equal(await call<string>("Greeter.Greet", "Ada", undefined), "Hello, Ada");
  assert.deepEqual(fake.calls, [{ method: "Greeter.Greet", args: ["Ada"] }]);
});

test("rejects with the one CallError of every client", async () => {
  fake.reply = async () => {
    const err = Object.assign(new Error("locked"), { name: "CallError", code: "locked", data: { by: "ada" } });
    throw err;
  };
  await assert.rejects(call("Notes.Lock"), (err: unknown) => {
    assert.ok(err instanceof CallErrorBase);
    assert.equal((globalThis as { [k: symbol]: unknown })[Symbol.for("oriel.CallError")], CallErrorBase);
    const e = err as CallErrorBase;
    assert.equal(e.method, "Notes.Lock");
    assert.equal(e.code, "locked");
    assert.deepEqual(e.data, { by: "ada" });
    return true;
  });
});

test("listens to events and streams", async () => {
  const got: unknown[] = [];
  const stop = on<number>("Clock.Tick", (n) => got.push(n));
  fake.listeners.get("Clock.Tick")!(7);
  stop();
  assert.equal(fake.listeners.has("Clock.Tick"), false);
  for await (const n of stream<number>("Counter.Count")) got.push(n);
  const drops: unknown[] = [];
  onFilesDropped((d) => drops.push(d));
  fake.listeners.get("oriel:filesDropped")!({ paths: ["/a"], x: 1, y: 2 });
  assert.deepEqual(got, [7, 1, 2]);
  assert.deepEqual(drops, [{ paths: ["/a"], x: 1, y: 2 }]);
});

test("says when the page is not in an Oriel window", () => {
  delete (globalThis as { oriel?: unknown }).oriel;
  assert.throws(() => bridge(), /window\.oriel is missing/);
});
