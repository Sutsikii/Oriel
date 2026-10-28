// A client generated with OrielTypeScriptRuntime = oriel-runtime (client.ts,
// written by the generator's tests), against the package.
import assert from "node:assert/strict";
import { test } from "node:test";
import { CallErrorBase } from "oriel-runtime";
import { CallError, Decks, Greeter } from "./client.ts";

test("a generated client calls through the package, and its CallError is the package's", async () => {
  (globalThis as { oriel?: unknown }).oriel = {
    call: async (method: string, args: unknown[]) => {
      if (method === "Decks.Save") throw Object.assign(new Error("changed"), { name: "CallError", code: "conflict", data: { path: "/d", version: 2 } });
      return `${method}(${JSON.stringify(args)})`;
    },
    on: () => () => {},
    stream: () => ({ async *[Symbol.asyncIterator]() {} }),
  };
  assert.equal(await Greeter.greet("Ada"), 'Greeter.Greet(["Ada"])');
  await assert.rejects(Decks.save("/d", { title: "D" } as never), (err: unknown) => {
    assert.ok(err instanceof CallError);
    assert.ok(err instanceof CallErrorBase);
    assert.equal(err.code === "conflict" && err.data.version, 2);
    return true;
  });
});
