// A game read from PGN, and what the engine thinks of each of its moves.
// Nothing here touches the page, so that it can be tested on its own.

import { Chess } from "chess.js";
import type { Line } from "./oriel";

/** A move of a game. */
export interface Ply {
  san: string;
  /** In UCI notation, as the engine writes moves: e2e4, e7e8q. */
  uci: string;
  color: "w" | "b";
  /** Its number, as PGN writes it: 1 for 1. e4 and 1… e5. */
  number: number;
  /** The position after it. */
  fen: string;
}

export interface Game {
  /** The PGN's tags: White, Black, Result, Date… */
  headers: Record<string, string>;
  /** The position before the first move. */
  start: string;
  plies: Ply[];
}

/** Reads a game. Throws when the text is not one. */
export function readPgn(pgn: string): Game {
  const chess = new Chess();
  chess.loadPgn(pgn.trim());
  const moves = chess.history({ verbose: true });
  const headers = chess.getHeaders();
  const start = moves[0]?.before ?? headers.FEN ?? chess.fen();
  const plies = moves.map((move): Ply => {
    const fields = move.before.split(" ");
    return { san: move.san, uci: move.lan, color: move.color, number: Number(fields[5]) || 1, fen: move.after };
  });
  return { headers, start, plies };
}

/** The positions of the game, its start first: one more than its moves. */
export const positions = (game: Game) => [game.start, ...game.plies.map((p) => p.fen)];

/**
 * White's chances, from 0 to 1, from the best line of a position. `turn` is the side to move,
 * which a mate of 0 needs: that side is mated. The curve is the one lichess fits to its games.
 */
export function whiteChances(line: Pick<Line, "cp" | "mate"> | undefined, turn: "w" | "b"): number {
  if (!line) return 0.5;
  if (line.mate !== null) return line.mate > 0 || (line.mate === 0 && turn === "b") ? 1 : 0;
  return 1 / (1 + Math.exp(-0.00368208 * (line.cp ?? 0)));
}

export type Judgement = "best" | "good" | "inaccuracy" | "mistake" | "blunder";

/** The NAG people write after a move of each judgement. */
export const symbols: Record<Judgement, string> = { best: "", good: "", inaccuracy: "?!", mistake: "?", blunder: "??" };

export interface Verdict {
  judgement: Judgement;
  /** The chances the mover gave away, from 0 to 1. */
  loss: number;
  /** The move the engine preferred, in UCI; null when the mover played it. */
  better: string | null;
}

/**
 * Judges a move by the chances its side gave away, as lichess does: from 5 points out of 100 it
 * is an inaccuracy, from 10 a mistake, from 15 a blunder.
 */
export function judge(ply: Ply, before: Line, after: Line, best: string | null): Verdict {
  const opponent = ply.color === "w" ? "b" : "w";
  const forMover = (chances: number) => (ply.color === "w" ? chances : 1 - chances);
  const loss = Math.max(0, forMover(whiteChances(before, ply.color)) - forMover(whiteChances(after, opponent)));
  const played = best === null || best === ply.uci;
  const judgement: Judgement = played
    ? "best"
    : loss >= 0.15
      ? "blunder"
      : loss >= 0.1
        ? "mistake"
        : loss >= 0.05
          ? "inaccuracy"
          : "good";
  return { judgement, loss: played ? 0 : loss, better: played ? null : best };
}

/** How well a side played, from 0 to 100: lichess's accuracy of each move, averaged. */
export function accuracy(verdicts: Verdict[]): number | null {
  if (verdicts.length === 0) return null;
  const each = verdicts.map((v) => Math.min(100, Math.max(0, 103.1668 * Math.exp(-0.04354 * v.loss * 100) - 3.1669)));
  return each.reduce((a, b) => a + b, 0) / each.length;
}

/**
 * What the engine would say of a position where the game is over, which it does not search: a
 * mate of 0 for the side mated, an even score for a draw. Null while the game goes on.
 */
export function finalLine(fen: string): Line | null {
  const chess = new Chess(fen);
  if (chess.isCheckmate()) return { rank: 1, cp: null, mate: 0, moves: [] };
  if (chess.isGameOver()) return { rank: 1, cp: 0, mate: null, moves: [] };
  return null;
}
