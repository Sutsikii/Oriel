import assert from "node:assert/strict";
import { test } from "node:test";
import { accuracy, finalLine, judge, positions, readPgn, whiteChances, type Ply } from "../src/review.ts";

// As chess.com exports them: tags, then moves with their clocks as comments.
const scholar = `[Event "Live Chess"]
[Site "Chess.com"]
[White "alice"]
[Black "bob"]
[Result "1-0"]
[WhiteElo "1200"]
[BlackElo "1180"]

1. e4 {[%clk 0:09:58.1]} 1... e5 {[%clk 0:09:57.3]} 2. Bc4 {[%clk 0:09:55]} 2... Nc6 {[%clk 0:09:51.2]}
3. Qh5 {[%clk 0:09:50]} 3... Nf6 {[%clk 0:09:40.8]} 4. Qxf7# {[%clk 0:09:47.6]} 1-0`;

const line = (cp: number | null, mate: number | null = null) => ({ rank: 1, cp, mate, moves: [] });

test("reads a game of chess.com, its tags and its moves", () => {
  const game = readPgn(scholar);
  assert.equal(game.headers.White, "alice");
  assert.equal(game.headers.WhiteElo, "1200");
  assert.equal(game.start, "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
  assert.deepEqual(
    game.plies.map((p) => p.san),
    ["e4", "e5", "Bc4", "Nc6", "Qh5", "Nf6", "Qxf7#"],
  );
  assert.deepEqual(game.plies.map((p) => p.uci).slice(0, 3), ["e2e4", "e7e5", "f1c4"]);
  assert.deepEqual(
    game.plies.map((p) => `${p.number}${p.color}`),
    ["1w", "1b", "2w", "2b", "3w", "3b", "4w"],
  );
  assert.equal(positions(game).length, 8);
  assert.equal(positions(game)[7], game.plies[6].fen);
});

test("reads a game from a set-up position, Black to move first", () => {
  const game = readPgn(`[SetUp "1"]
[FEN "4k3/8/8/8/8/8/4P3/4K3 b - - 0 40"]

40... Kd7 41. e4 *`);
  assert.equal(game.start, "4k3/8/8/8/8/8/4P3/4K3 b - - 0 40");
  assert.deepEqual(
    game.plies.map((p) => `${p.number}${p.color} ${p.san}`),
    ["40b Kd7", "41w e4"],
  );
});

test("writes castling and promotions as the engine does", () => {
  const game = readPgn("1. e4 e5 2. Nf3 Nc6 3. Bc4 Bc5 4. O-O *");
  assert.equal(game.plies[6].uci, "e1g1");
  const promotion = readPgn(`[SetUp "1"]
[FEN "8/4P3/8/8/8/8/k7/4K3 w - - 0 1"]

1. e8=Q *`);
  assert.equal(promotion.plies[0].uci, "e7e8q");
});

test("refuses what is not a game", () => {
  assert.throws(() => readPgn("1. e4 e5 2. Ke3 Ke6 3. Qxf7"));
});

test("gives White's chances from the scores", () => {
  assert.equal(whiteChances(line(0), "w"), 0.5);
  assert.ok(whiteChances(line(300), "w") > 0.7);
  assert.ok(whiteChances(line(-300), "b") < 0.3);
  assert.equal(whiteChances(line(null, 3), "b"), 1);
  assert.equal(whiteChances(line(null, -2), "w"), 0);
  // Mated: the side to move loses.
  assert.equal(whiteChances(line(null, 0), "b"), 1);
  assert.equal(whiteChances(line(null, 0), "w"), 0);
});

const white: Ply = { san: "e4", uci: "e2e4", color: "w", number: 1, fen: "" };
const black: Ply = { san: "e5", uci: "e7e5", color: "b", number: 1, fen: "" };

test("judges the engine's move best, whatever the score does", () => {
  assert.deepEqual(judge(white, line(30), line(-200), "e2e4"), { judgement: "best", loss: 0, better: null });
});

test("judges moves by the chances their side gave away", () => {
  assert.equal(judge(white, line(30), line(20), "d2d4").judgement, "good");
  assert.equal(judge(white, line(30), line(-40), "d2d4").judgement, "inaccuracy");
  assert.equal(judge(white, line(30), line(-100), "d2d4").judgement, "mistake");
  assert.equal(judge(white, line(30), line(-500), "d2d4").judgement, "blunder");
  // Black's moves, from Black's side: White's score going up is Black's loss.
  assert.equal(judge(black, line(0), line(450), "d7d5").judgement, "blunder");
  assert.equal(judge(black, line(0), line(-30), "d7d5").judgement, "good");
  assert.equal(judge(white, line(30), line(-500), "d2d4").better, "d2d4");
});

test("judges missing a mate, and walking into one", () => {
  assert.equal(judge(white, line(null, 2), line(150), "h5f7").judgement, "blunder");
  assert.equal(judge(black, line(-50), line(null, 1), "g8f6").judgement, "blunder");
  // Mating: the position after it is a mate of 0 for Black, to move.
  assert.equal(judge(white, line(null, 1), line(null, 0), "h5f7").judgement, "good");
});

test("gives 100 for perfect play and less for losses", () => {
  assert.equal(accuracy([]), null);
  const perfect = accuracy([{ judgement: "best", loss: 0, better: null }])!;
  assert.ok(perfect > 99.9 && perfect <= 100);
  const blunders = accuracy([{ judgement: "blunder", loss: 0.4, better: "e2e4" }])!;
  assert.ok(blunders < 20);
});

test("scores positions where the game is over", () => {
  const game = readPgn(scholar);
  assert.deepEqual(finalLine(game.plies[6].fen), line(null, 0));
  assert.deepEqual(finalLine("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1"), line(0)); // stalemate
  assert.equal(finalLine(game.start), null);
});
