import assert from "node:assert/strict";
import { test } from "node:test";
import { Color } from "three";
import { parseColor, place, squareOf } from "../src/board3d.ts";

const a8 = 0;
const h1 = 63;

test("puts a8 far left, and h1 far left once the board is turned", () => {
  assert.deepEqual(place(a8, false).toArray(), [-3.5, 0, -3.5]);
  assert.deepEqual(place(h1, false).toArray(), [3.5, 0, 3.5]);
  assert.deepEqual(place(h1, true).toArray(), [-3.5, 0, -3.5]);
});

test("finds each square where it is put, either way up", () => {
  for (const flipped of [false, true])
    for (let i = 0; i < 64; i++) {
      const centre = place(i, flipped);
      assert.equal(squareOf(centre.x, centre.z, flipped), i);
      assert.equal(squareOf(centre.x + 0.49, centre.z - 0.49, flipped), i);
    }
});

test("finds no square on the border or beyond", () => {
  assert.equal(squareOf(-4.1, 0, false), null);
  assert.equal(squareOf(0, 4.2, true), null);
  assert.equal(squareOf(8, 8, false), null);
});

test("reads the colors of the stylesheet, their opacity apart", () => {
  const arrow = parseColor(" rgba(21, 120, 27, 0.85)");
  assert.equal(arrow.alpha, 0.85);
  assert.equal(arrow.color.getHexString(), new Color("rgb(21, 120, 27)").getHexString());
  assert.equal(parseColor("#b58863").alpha, 1);
  assert.equal(parseColor("#b58863").color.getHexString(), "b58863");
});
