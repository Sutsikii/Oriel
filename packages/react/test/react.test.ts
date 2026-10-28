import assert from "node:assert/strict";
import { test } from "node:test";
import { JSDOM } from "jsdom";

const dom = new JSDOM("<!doctype html><div id=root></div>");
Object.assign(globalThis, { window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true });
const { act, createElement } = await import("react");
const { createRoot } = await import("react-dom/client");
const { useOrielEvent, useOrielQuery } = await import("oriel-react");

/** An event of a generated client, fired by the test. */
function event<T>() {
  const listeners = new Set<(p: T) => void>();
  return {
    subscribe: (l: (p: T) => void) => {
      listeners.add(l);
      return () => listeners.delete(l);
    },
    fire: (p: T) => listeners.forEach((l) => l(p)),
    get count() {
      return listeners.size;
    },
  };
}

test("listens to an event while mounted", async () => {
  const tick = event<number>();
  const seen: number[] = [];
  function Clock() {
    useOrielEvent(tick.subscribe, (n) => seen.push(n));
    return null;
  }
  const root = createRoot(document.getElementById("root")!);
  await act(async () => root.render(createElement(Clock)));
  await act(async () => tick.fire(1));
  assert.equal(tick.count, 1);
  await act(async () => root.unmount());
  tick.fire(2);
  assert.deepEqual(seen, [1]);
  assert.equal(tick.count, 0);
});

test("queries, and queries again when an event says so", async () => {
  const changed = event<string>();
  let calls = 0;
  const seen: (number | undefined)[] = [];
  function Count() {
    const { data, loading } = useOrielQuery(async () => ++calls, [], { refreshOn: [changed.subscribe] });
    if (!loading) seen.push(data);
    return null;
  }
  const root = createRoot(document.getElementById("root")!);
  await act(async () => root.render(createElement(Count)));
  await act(async () => changed.fire("changed"));
  await act(async () => root.unmount());
  assert.deepEqual(seen.filter((v, i) => seen.indexOf(v) === i), [1, 2]);
  assert.equal(changed.count, 0);
});

test("keeps the error of a call that failed", async () => {
  let state: { error: unknown; loading: boolean } | undefined;
  function Failing() {
    state = useOrielQuery(async () => {
      throw new Error("nope");
    }, []);
    return null;
  }
  const root = createRoot(document.getElementById("root")!);
  await act(async () => root.render(createElement(Failing)));
  assert.equal((state!.error as Error).message, "nope");
  assert.equal(state!.loading, false);
  await act(async () => root.unmount());
});
