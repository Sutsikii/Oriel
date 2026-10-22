// The position of the board editor, read from and written to FEN. Unlike
// chess.js, it holds any position, legal or not: the board is being set up.

export type Color = "w" | "b";
export type Piece = `${Color}${"K" | "Q" | "R" | "B" | "N" | "P"}`;

export interface Position {
  /** 64 squares, a8 first and h1 last, as FEN lists them. */
  board: (Piece | null)[];
  turn: Color;
  /** "KQkq", or a part of it; empty for none. */
  castling: string;
  /** The en passant square, or "-". */
  ep: string;
  halfmove: number;
  fullmove: number;
}

export const START = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
export const KINGS = "4k3/8/8/8/8/8/8/4K3 w - - 0 1";
export const EMPTY = "8/8/8/8/8/8/8/8 w - - 0 1";

export const PIECES: Piece[] = ["wK", "wQ", "wR", "wB", "wN", "wP", "bK", "bQ", "bR", "bB", "bN", "bP"];

/** Index 0 is a8, 63 is h1. */
export const squareName = (i: number) => "abcdefgh"[i % 8] + (8 - Math.floor(i / 8));
export const squareIndex = (name: string) => (8 - Number(name[1])) * 8 + "abcdefgh".indexOf(name[0]);

const toPiece = (c: string): Piece | null => {
  const kind = c.toUpperCase();
  if (!"KQRBNP".includes(kind)) return null;
  return `${c === kind ? "w" : "b"}${kind}` as Piece;
};
const toChar = (p: Piece) => (p[0] === "w" ? p[1] : p[1].toLowerCase());

/** Reads a FEN; the fields after the board may be left out. Null when it is not one. */
export function parse(fen: string): Position | null {
  const [placement, turn = "w", castling = "-", ep = "-", half = "0", full = "1"] = fen.trim().split(/\s+/);
  const ranks = placement?.split("/") ?? [];
  if (ranks.length !== 8) return null;
  const board: (Piece | null)[] = [];
  for (const rank of ranks) {
    let files = 0;
    for (const c of rank) {
      if (/[1-8]/.test(c)) {
        for (let n = 0; n < Number(c); n++) board.push(null);
        files += Number(c);
      } else {
        const piece = toPiece(c);
        if (!piece) return null;
        board.push(piece);
        files++;
      }
    }
    if (files !== 8) return null;
  }
  if (turn !== "w" && turn !== "b") return null;
  const position: Position = {
    board,
    turn,
    castling: castling === "-" ? "" : castling.replace(/[^KQkq]/g, ""),
    ep: /^[a-h][36]$/.test(ep) ? ep : "-",
    halfmove: Number(half) || 0,
    fullmove: Math.max(1, Number(full) || 1),
  };
  position.castling = allowedCastling(position);
  return position;
}

export function format(p: Position): string {
  const ranks: string[] = [];
  for (let r = 0; r < 8; r++) {
    let rank = "";
    let empty = 0;
    for (let f = 0; f < 8; f++) {
      const piece = p.board[r * 8 + f];
      if (!piece) {
        empty++;
        continue;
      }
      if (empty) rank += empty;
      empty = 0;
      rank += toChar(piece);
    }
    ranks.push(empty ? rank + empty : rank);
  }
  return `${ranks.join("/")} ${p.turn} ${allowedCastling(p) || "-"} ${p.ep} ${p.halfmove} ${p.fullmove}`;
}

/** Which castling rights the pieces allow: a king and its rook on their first squares. */
export function possibleCastling(board: (Piece | null)[]): string {
  const at = (name: string) => board[squareIndex(name)];
  let rights = "";
  if (at("e1") === "wK") {
    if (at("h1") === "wR") rights += "K";
    if (at("a1") === "wR") rights += "Q";
  }
  if (at("e8") === "bK") {
    if (at("h8") === "bR") rights += "k";
    if (at("a8") === "bR") rights += "q";
  }
  return rights;
}

function allowedCastling(p: Position): string {
  const possible = possibleCastling(p.board);
  return [..."KQkq"].filter((c) => p.castling.includes(c) && possible.includes(c)).join("");
}
