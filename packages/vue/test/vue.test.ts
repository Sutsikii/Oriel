import assert from "node:assert/strict";
import { test } from "node:test";
import { effectScope, nextTick, ref } from "vue";
import { useOrielEvent, useOrielQuery } from "oriel-vue";

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

const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

test("listens to an event until its scope goes", () => {
  const tick = event<number>();
  const seen: number[] = [];
  const scope = effectScope();
  scope.run(() => useOrielEvent(tick.subscribe, (n) => seen.push(n)));
  tick.fire(1);
  scope.stop();
  tick.fire(2);
  assert.deepEqual(seen, [1]);
  assert.equal(tick.count, 0);
});

test("queries again when its source or an event changes", async () => {
  const changed = event<string>();
  const id = ref(1);
  const scope = effectScope();
  const query = scope.run(() => useOrielQuery(async () => `deck ${id.value}`, { watch: id, refreshOn: [changed.subscribe] }))!;
  await settle();
  assert.equal(query.data.value, "deck 1");
  id.value = 2;
  await nextTick();
  await settle();
  assert.equal(query.data.value, "deck 2");
  changed.fire("changed");
  assert.equal(query.loading.value, true);
  await settle();
  assert.equal(query.loading.value, false);
  scope.stop();
  assert.equal(changed.count, 0);
});
