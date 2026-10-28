import assert from "node:assert/strict";
import { test } from "node:test";
import { invalidateOn, invalidateOnPayload } from "oriel-tanstack-query";

test("an event invalidates the queries of its key", () => {
  const invalidated: unknown[] = [];
  const client = { invalidateQueries: (filters: { queryKey: readonly unknown[] }) => invalidated.push(filters.queryKey) };
  let fire: ((deck: { id: number }) => void) | undefined;
  const subscribe = (listener: (deck: { id: number }) => void) => {
    fire = listener;
    return () => (fire = undefined);
  };
  const stop = invalidateOn(client, subscribe, ["decks"]);
  fire!({ id: 4 });
  stop();
  assert.equal(fire, undefined);
  invalidateOnPayload(client, subscribe, (deck) => ["deck", deck.id]);
  fire!({ id: 7 });
  assert.deepEqual(invalidated, [["decks"], ["deck", 7]]);
});
