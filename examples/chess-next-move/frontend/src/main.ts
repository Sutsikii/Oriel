import "./style.css";
import { Chess, validateFen } from "chess.js";
import { CallError, Engine, type Analysis, type Line } from "./oriel";
import { EMPTY, KINGS, PIECES, START, format, parse, possibleCastling, squareIndex, squareName, type Piece, type Position } from "./position";
import { recentGames, result, type ArchivedGame } from "./chesscom";
import { accuracy, finalLine, judge, positions, readPgn, symbols, whiteChances, type Game, type Verdict } from "./review";
import type { Board3D, Squares } from "./board3d";

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;

// Settings, kept between runs.

interface Settings {
  depth: string;
  customDepth: number;
  time: number;
  lines: number;
  pieces: string;
  theme: "system" | "light" | "dark";
  coords: boolean;
  /** Whether each new position is searched at once, without pressing Play. */
  auto: boolean;
  flipped: boolean;
  /** The chess.com player whose games are listed. */
  username: string;
  view: "2d" | "3d";
}

const defaults: Settings = {
  depth: "18",
  customDepth: 30,
  time: 5000,
  lines: 3,
  pieces: "cburnett",
  theme: "system",
  coords: true,
  auto: true,
  flipped: false,
  username: "",
  view: "2d",
};

function loadSettings(): Settings {
  try {
    return { ...defaults, ...JSON.parse(localStorage.getItem("settings") ?? "{}") };
  } catch {
    return { ...defaults };
  }
}

const settings = loadSettings();
const saveSettings = () => {
  try {
    localStorage.setItem("settings", JSON.stringify(settings));
  } catch {
    // Settings then last only as long as the page.
  }
};
const searchDepth = () => (settings.depth === "custom" ? settings.customDepth : Number(settings.depth));

// The positions the board went through, for Back and Forward.

const initial = parse(new URLSearchParams(location.search).get("fen") ?? "") ?? parse(START)!;
const history: string[] = [format(initial)];
let cursor = 0;

const current = (): Position => parse(history[cursor])!;
const currentFen = () => history[cursor];

/** Makes `next` the position, after the one shown: what was ahead of it is dropped. */
function push(next: Position | string) {
  const fen = typeof next === "string" ? next : format(next);
  if (fen === currentFen()) return;
  history.splice(cursor + 1, Infinity, fen);
  cursor = history.length - 1;
  changed();
}

function edit(change: (p: Position) => void) {
  const p = current();
  change(p);
  p.ep = "-";
  push(p);
}

/** What keeps the engine from searching the position, or null when nothing does. */
function problem(fen: string): string | null {
  const validation = validateFen(fen);
  if (!validation.ok) return validation.error ?? "The position is not valid.";
  const other = fen.split(" ");
  other[1] = other[1] === "w" ? "b" : "w";
  other[3] = "-";
  if (new Chess(other.join(" "), { skipValidation: true }).inCheck()) return "the side not to move is in check.";
  return null;
}

// The board.

const boardEl = $("board");
const arrowsEl = document.getElementById("arrows") as unknown as SVGSVGElement;
const squares: HTMLDivElement[] = [];
let tool: Piece | "erase" | null = null;

for (let d = 0; d < 64; d++) {
  const square = document.createElement("div");
  square.className = "square";
  square.innerHTML = `<span class="coord file"></span><span class="coord rank"></span>`;
  square.addEventListener("pointerdown", (e) => startDrag(e, "board", squareAt(e.clientX, e.clientY)));
  square.addEventListener("contextmenu", (e) => {
    e.preventDefault();
    const i = squareAt(e.clientX, e.clientY);
    if (i !== null) edit((p) => (p.board[i] = null));
  });
  boardEl.append(square);
  squares.push(square);
}

/** The square shown at place `d`, 0 being the top left. */
const shown = (d: number) => (settings.flipped ? 63 - d : d);
const pieceUrl = (piece: Piece) => `pieces/${settings.pieces}/${piece}.svg`;

function squareAt(x: number, y: number): number | null {
  if (board3d) return board3d.squareAt(x, y);
  const r = boardEl.getBoundingClientRect();
  if (x < r.left || x >= r.right || y < r.top || y >= r.bottom) return null;
  const col = Math.floor(((x - r.left) / r.width) * 8);
  const row = Math.floor(((y - r.top) / r.height) * 8);
  return shown(row * 8 + col);
}

function renderBoard() {
  const p = current();
  const best = bestMove();
  // The move of the game that led to the position shown.
  const at = gameIndex();
  const last = at > 0 ? game!.plies[at - 1].uci : null;
  squares.forEach((square, d) => {
    const i = shown(d);
    const file = i % 8;
    const rank = 8 - Math.floor(i / 8);
    square.classList.toggle("dark", (file + rank) % 2 === 1); // a1 is dark
    square.classList.toggle("from", best?.slice(0, 2) === squareName(i));
    square.classList.toggle("to", best?.slice(2, 4) === squareName(i));
    square.classList.toggle("last", last?.slice(0, 2) === squareName(i) || last?.slice(2, 4) === squareName(i));
    square.querySelector(".file")!.textContent = settings.coords && d >= 56 ? "abcdefgh"[file] : "";
    square.querySelector(".rank")!.textContent = settings.coords && d % 8 === 0 ? String(rank) : "";
    const piece = p.board[i];
    let img = square.querySelector("img");
    if (!piece) {
      img?.remove();
      return;
    }
    if (!img) {
      img = document.createElement("img");
      img.draggable = false;
      img.alt = "";
      square.append(img);
    }
    if (img.getAttribute("src") !== pieceUrl(piece)) img.src = pieceUrl(piece);
    img.dataset.piece = piece;
  });
  if (board3d) {
    board3d.setPosition(p.board, settings.flipped, settings.coords);
    // The arrows in the 2D board's colors: --arrow1… for the lines, --reply for the reply.
    const css = getComputedStyle(document.documentElement);
    const squaresOf = (uci: string): Squares => [squareIndex(uci.slice(0, 2)), squareIndex(uci.slice(2, 4))];
    const arrows = arrowList(shownAnalysis()).map(({ move, style }) => ({
      squares: squaresOf(move),
      color: css.getPropertyValue(style === "reply" ? "--reply" : `--arrow${style.slice(4)}`),
    }));
    board3d.setMarks(arrows, { best: best ? squaresOf(best) : null, last: last ? squaresOf(last) : null });
  }
}

// Dragging pieces, from the board or from the palettes.

let drag: { piece: Piece; from: number | null; ghost: HTMLImageElement; x: number; y: number; moved: boolean } | null = null;
let pressed: { from: number | null; source: "board" | "palette" } | null = null;

function startDrag(e: PointerEvent, source: "board" | "palette", from: number | null, piece?: Piece) {
  if (e.button !== 0) return;
  e.preventDefault();
  pressed = { from, source };
  piece ??= from === null ? undefined : (current().board[from] ?? undefined);
  if (!piece) return; // an empty square: a click, which the tool acts on
  const ghost = document.createElement("img");
  ghost.src = pieceUrl(piece);
  ghost.className = "ghost";
  const size = (board3d ? $("board3d") : boardEl).getBoundingClientRect().width / 8;
  ghost.style.width = ghost.style.height = `${size}px`;
  drag = { piece, from: source === "board" ? from : null, ghost, x: e.clientX, y: e.clientY, moved: false };
  moveGhost(e.clientX, e.clientY);
}

function moveGhost(x: number, y: number) {
  if (!drag) return;
  // On the 3D board, the piece itself follows the pointer.
  if (board3d && drag.from !== null) return board3d.drag(x, y);
  const half = parseFloat(drag.ghost.style.width) / 2;
  drag.ghost.style.transform = `translate(${x - half}px, ${y - half}px)`;
}

document.addEventListener("pointermove", (e) => {
  if (!drag) return;
  if (!drag.moved && Math.hypot(e.clientX - drag.x, e.clientY - drag.y) > 4) {
    drag.moved = true;
    if (board3d && drag.from !== null) board3d.lift(drag.from);
    else document.body.append(drag.ghost);
    if (drag.from !== null) squares[settings.flipped ? 63 - drag.from : drag.from].classList.add("lifted");
  }
  moveGhost(e.clientX, e.clientY);
});

document.addEventListener("pointerup", (e) => {
  const was = drag;
  const press = pressed;
  drag = null;
  pressed = null;
  was?.ghost.remove();
  squares.forEach((s) => s.classList.remove("lifted"));
  if (!press) return;
  // A piece lifted on the 3D board and dropped where nothing changes goes back to its square.
  if (board3d && was?.moved) queueMicrotask(renderBoard);
  const target = squareAt(e.clientX, e.clientY);

  if (!was?.moved) {
    // A click: the selected tool acts on the square.
    if (press.source === "board" && target !== null && tool)
      edit((p) => (p.board[target] = tool === "erase" ? null : (tool as Piece)));
    return;
  }
  if (was.from !== null) {
    const from = was.from;
    if (target === from) return;
    if (target !== null && playDrag(from, target)) return;
    edit((p) => {
      p.board[from] = null;
      if (target !== null) p.board[target] = was.piece; // dropped off the board: removed
    });
  } else if (target !== null) {
    edit((p) => (p.board[target] = was.piece));
  }
});

/**
 * Plays a drag on the board as a move when it is a legal one: the turn passes, and castling
 * rights and en passant follow. Any other drag edits the board, and the side to move stays.
 */
function playDrag(from: number, to: number): boolean {
  const fen = currentFen();
  if (problem(fen)) return false;
  const game = new Chess(fen);
  try {
    game.move({ from: squareName(from), to: squareName(to), promotion: "q" });
  } catch {
    return false;
  }
  push(game.fen());
  return true;
}

function renderPalettes() {
  const top = settings.flipped ? "w" : "b";
  for (const [id, color] of [["palette-top", top], ["palette-bottom", top === "w" ? "b" : "w"]] as const) {
    const palette = $(id);
    palette.replaceChildren();
    for (const piece of PIECES.filter((p) => p[0] === color)) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "spare";
      button.classList.toggle("selected", tool === piece);
      button.title = `Drag to add, or click to select`;
      const img = document.createElement("img");
      img.src = pieceUrl(piece);
      img.draggable = false;
      img.alt = piece;
      button.append(img);
      button.addEventListener("pointerdown", (e) => startDrag(e, "palette", null, piece));
      button.addEventListener("click", () => selectTool(piece));
      palette.append(button);
    }
    if (id === "palette-bottom") {
      const eraser = document.createElement("button");
      eraser.type = "button";
      eraser.className = "spare eraser";
      eraser.classList.toggle("selected", tool === "erase");
      eraser.title = "Eraser: click squares to empty them";
      eraser.textContent = "×";
      eraser.addEventListener("click", () => selectTool("erase"));
      palette.append(eraser);
    }
  }
}

function selectTool(next: Piece | "erase") {
  tool = tool === next ? null : next;
  boardEl.classList.toggle("tool", tool !== null);
  renderPalettes();
}

// The engine.

let search = 0;
let running = false;
let analysis: Analysis | null = null;
/** The finished searches, by position: going back to one shows what was found at once. */
const analyses = new Map<string, Analysis>();

/** The analysis of the position shown, when there is one: the search going on, else the last one done. */
const shownAnalysis = () => (analysis && analysis.fen === currentFen() ? analysis : (analyses.get(currentFen()) ?? null));
const bestMove = () => {
  const a = shownAnalysis();
  return a?.bestMove ?? a?.lines[0]?.moves[0] ?? null;
};

Engine.onProgress((a) => {
  if (a.search !== search) return;
  analysis = a;
  renderAnalysis();
});

async function analyse() {
  const fen = currentFen();
  const id = ++search;
  analysis = null;
  const trouble = problem(fen);
  if (trouble) {
    running = false;
    renderAnalysis(`Invalid position · ${trouble}`);
    return;
  }
  const game = new Chess(fen);
  if (game.isGameOver()) {
    running = false;
    renderAnalysis(
      game.isCheckmate() ? "Checkmate" : game.isStalemate() ? "Stalemate" : game.isInsufficientMaterial() ? "Draw · insufficient material" : "Draw",
    );
    return;
  }
  running = true;
  renderAnalysis();
  try {
    const result = await Engine.analyse(id, fen, searchDepth(), settings.time, settings.lines);
    if (id !== search) return;
    analysis = result;
    analyses.set(fen, result);
    running = false;
    renderAnalysis();
  } catch (err) {
    if (id !== search) return;
    running = false;
    renderAnalysis(err instanceof CallError ? err.message : String(err));
  }
}

function stop() {
  search++;
  if (running) Engine.stop();
  running = false;
}

/** A score as people read it: +0.4, -1.2, M3, -M2. */
function score(line: Line | undefined, forWhite = true): string {
  if (!line) return "0.0";
  if (line.mate !== null) {
    if (line.mate === 0) return "#";
    const mate = forWhite ? line.mate : -line.mate;
    return `${mate < 0 ? "-" : ""}M${Math.abs(mate)}`;
  }
  const pawns = ((forWhite ? 1 : -1) * (line.cp ?? 0)) / 100;
  return `${pawns > 0 ? "+" : ""}${pawns.toFixed(1)}`;
}

/** How much of the bar is White's, from 0 to 1. */
function whiteShare(line: Line | undefined): number {
  if (!line) return 0.5;
  if (line.mate !== null) return line.mate > 0 || (line.mate === 0 && current().turn === "b") ? 1 : 0;
  return 1 / (1 + Math.exp(-0.004 * (line.cp ?? 0)));
}

/** The moves of a line in SAN, numbered: "12. Nf3 Nc6 13. Bb5". */
function san(fen: string, moves: string[], limit = 12): string {
  const game = new Chess(fen);
  const parts: string[] = [];
  for (const uci of moves.slice(0, limit)) {
    const white = game.turn() === "w";
    const number = game.moveNumber();
    try {
      const move = game.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci[4] });
      if (white) parts.push(`${number}. ${move.san}`);
      else parts.push(parts.length === 0 ? `${number}… ${move.san}` : move.san);
    } catch {
      break;
    }
  }
  return parts.join(" ");
}

const compact = (n: number) => Intl.NumberFormat("en", { notation: "compact", maximumFractionDigits: 1 }).format(n);

function renderAnalysis(message?: string) {
  const a = shownAnalysis();
  const best = a?.lines[0];

  // The evaluation: White's bar, and each side's number at its edge of the board.
  const share = whiteShare(best);
  $("eval-fill").style.height = `${share * 100}%`;
  $("eval-bar").classList.toggle("flipped", settings.flipped);
  const bottomWhite = !settings.flipped;
  $("eval-bottom").textContent = score(best, bottomWhite);
  $("eval-top").textContent = score(best, !bottomWhite);

  const status = $("status");
  status.classList.toggle("error", !!message);
  if (message) status.textContent = message;
  else if (reviewing) status.textContent = `Reviewing the game · ${reviewed.filter(Boolean).length}/${gamePositions.length} positions`;
  else if (running)
    status.textContent = a ? `Analysing · depth ${a.depth}/${searchDepth()} · ${compact(a.nps)} nodes/s` : "Analysing…";
  else if (a) {
    const line = a.lines[0]?.moves ?? [];
    const best = san(a.fen, line.slice(0, 1), 1) || "none";
    const expected = line.length > 1 ? ` · then ${san(a.fen, line.slice(0, 2), 2).split(" ").slice(-1)[0]}` : "";
    status.textContent = `Done · depth ${a.depth} · ${compact(a.nodes)} nodes · best ${best}${expected}`;
  }
  else status.textContent = "Ready to analyse · Press Play";

  const list = $("lines");
  list.replaceChildren();
  if (!a || a.lines.length === 0) {
    const li = document.createElement("li");
    li.className = "empty";
    li.textContent = running ? "Thinking…" : "No analysis yet.";
    list.append(li);
  }
  for (const line of a?.lines ?? []) {
    const li = document.createElement("li");
    li.title = "Click to play this line's move";
    const badge = document.createElement("span");
    badge.className = "score";
    badge.textContent = score(line);
    const moves = document.createElement("span");
    moves.className = "moves";
    moves.textContent = san(a!.fen, line.moves);
    li.append(badge, moves);
    li.addEventListener("click", () => line.moves[0] && play(line.moves[0]));
    list.append(li);
  }

  $<HTMLButtonElement>("analyse").textContent = running ? "■ Stop" : "▶ Play";
  $<HTMLButtonElement>("move").disabled = !bestMove();
  renderArrows(a);
  renderBoard();
}

/**
 * The arrows of the lines: the opponent's expected reply to the best move first, under the lines;
 * then the worst line, so that the best one is drawn on top.
 */
function arrowList(a: Analysis | null): { move: string; style: string; rank: number }[] {
  const reply = a?.lines[0]?.moves[1];
  const arrows = reply ? [{ move: reply, style: "reply", rank: 0 }] : [];
  for (const line of (a?.lines ?? []).slice(0, settings.lines).reverse())
    if (line.moves[0]) arrows.push({ move: line.moves[0], style: `rank${line.rank}`, rank: line.rank });
  return arrows;
}

function renderArrows(a: Analysis | null) {
  arrowsEl.replaceChildren();
  const ns = "http://www.w3.org/2000/svg";
  const place = (name: string) => {
    const i = squareIndex(name);
    const d = settings.flipped ? 63 - i : i;
    return { x: (d % 8) + 0.5, y: Math.floor(d / 8) + 0.5 };
  };
  for (const { move, style, rank } of arrowList(a)) {
    const from = place(move.slice(0, 2));
    const to = place(move.slice(2, 4));
    const length = Math.hypot(to.x - from.x, to.y - from.y);
    const ux = (to.x - from.x) / length;
    const uy = (to.y - from.y) / length;
    const head = 0.32;
    const end = { x: to.x - ux * head, y: to.y - uy * head };
    const g = document.createElementNS(ns, "g");
    g.setAttribute("class", `arrow ${style}`);
    const shaft = document.createElementNS(ns, "line");
    shaft.setAttribute("x1", String(from.x));
    shaft.setAttribute("y1", String(from.y));
    shaft.setAttribute("x2", String(end.x));
    shaft.setAttribute("y2", String(end.y));
    const tip = document.createElementNS(ns, "polygon");
    const w = 0.24;
    tip.setAttribute("points", `${to.x},${to.y} ${end.x - uy * w},${end.y + ux * w} ${end.x + uy * w},${end.y - ux * w}`);
    g.append(shaft, tip);
    if (settings.lines > 1 && rank > 0) {
      const badge = document.createElementNS(ns, "circle");
      badge.setAttribute("cx", String(from.x));
      badge.setAttribute("cy", String(from.y));
      badge.setAttribute("r", "0.18");
      const label = document.createElementNS(ns, "text");
      label.setAttribute("x", String(from.x));
      label.setAttribute("y", String(from.y));
      label.textContent = String(rank);
      g.append(badge, label);
    }
    arrowsEl.append(g);
  }
}

/** Plays `uci` in the position shown. */
function play(uci: string) {
  const game = new Chess(currentFen());
  try {
    game.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci[4] });
  } catch {
    return;
  }
  push(game.fen());
}

// The panel.

function renderPosition() {
  const p = current();
  document.querySelectorAll<HTMLButtonElement>("#turn button").forEach((b) => b.classList.toggle("selected", b.dataset.turn === p.turn));
  const possible = possibleCastling(p.board);
  document.querySelectorAll<HTMLInputElement>("#castling input").forEach((input) => {
    const right = input.dataset.castle!;
    input.checked = p.castling.includes(right);
    input.disabled = !possible.includes(right);
  });
  const fen = $<HTMLInputElement>("fen");
  if (document.activeElement !== fen) fen.value = currentFen();
  fen.classList.remove("invalid");
  $<HTMLButtonElement>("back").disabled = cursor === 0;
  $<HTMLButtonElement>("forward").disabled = cursor === history.length - 1;
  window.history.replaceState(null, "", `?fen=${encodeURIComponent(currentFen())}`);
  renderGame();
}

/** The position shown changed: the search of the one before is of no use, unless it is the review's. */
function changed() {
  if (!reviewing) stop();
  analysis = null;
  renderPosition();
  renderAnalysis();
  if (settings.auto && !reviewing) soon();
}

let pending: ReturnType<typeof setTimeout> | undefined;

/** Searches the position shown in a moment: edits in quick succession start one search, not one each. */
function soon() {
  clearTimeout(pending);
  pending = setTimeout(analyse, 80);
}

document.querySelectorAll<HTMLButtonElement>("#turn button").forEach((b) =>
  b.addEventListener("click", () => edit((p) => (p.turn = b.dataset.turn as Position["turn"]))),
);
document.querySelectorAll<HTMLInputElement>("#castling input").forEach((input) =>
  input.addEventListener("change", () =>
    edit((p) => {
      const rights = new Set(p.castling);
      if (input.checked) rights.add(input.dataset.castle!);
      else rights.delete(input.dataset.castle!);
      p.castling = [..."KQkq"].filter((c) => rights.has(c)).join("");
    }),
  ),
);

$("start").addEventListener("click", () => push(START));
$("kings").addEventListener("click", () => push(KINGS));
$("clear").addEventListener("click", () => push(EMPTY));
$("rotate").addEventListener("click", () => {
  settings.flipped = !settings.flipped;
  saveSettings();
  renderPalettes();
  renderAnalysis();
});

const fenInput = $<HTMLInputElement>("fen");
const applyFen = () => {
  const p = parse(fenInput.value);
  if (!p) {
    fenInput.classList.add("invalid");
    return;
  }
  push(p);
  fenInput.value = currentFen();
  fenInput.classList.remove("invalid");
};
fenInput.addEventListener("keydown", (e) => {
  if (e.key === "Enter") applyFen();
  if (e.key === "Escape") {
    fenInput.value = currentFen();
    fenInput.blur();
  }
});
fenInput.addEventListener("change", applyFen);
$("copy").addEventListener("click", async () => {
  await navigator.clipboard.writeText(currentFen());
  $("copy").textContent = "Copied";
  setTimeout(() => ($("copy").textContent = "Copy"), 1200);
});

$("analyse").addEventListener("click", () => {
  if (reviewing) {
    stopReview();
    return;
  }
  if (running) {
    Engine.stop(); // the search returns what it found so far
    return;
  }
  analyse();
});
$("move").addEventListener("click", () => {
  const move = bestMove();
  if (move) play(move);
});
$("back").addEventListener("click", () => step(-1));
$("forward").addEventListener("click", () => step(1));

function step(by: number) {
  const next = Math.min(Math.max(cursor + by, 0), history.length - 1);
  if (next === cursor) return;
  cursor = next;
  changed();
}

document.addEventListener("keydown", (e) => {
  if (e.target instanceof HTMLInputElement || e.target instanceof HTMLSelectElement || e.target instanceof HTMLTextAreaElement) return;
  if (e.key === "ArrowLeft") step(-1);
  else if (e.key === "ArrowRight") step(1);
  else if (e.key === "Home") step(-cursor);
  else if (e.key === "End") step(history.length);
  else if (e.key === " " || e.key === "Enter") {
    e.preventDefault();
    $("analyse").click();
  } else if (e.key === "m") $("move").click();
  else if (e.key === "Escape" && tool) selectTool(tool);
});

// The game: a finished game of chess.com, or a PGN, its moves and their review.

interface Reviewed {
  /** The best line of the position. */
  line: Line;
  best: string | null;
}

let game: Game | null = null;
let gamePgn = "";
let gamePositions: string[] = [];
/** What the review found of each position of the game, as it goes. */
let reviewed: (Reviewed | undefined)[] = [];
/** The review going on, by number; 0 when none is. */
let reviewing = 0;
let reviews = 0;

/** Reviews look less deep than the board: a game has a hundred positions. */
const reviewDepth = () => Math.min(searchDepth(), 16);

/** Where the position shown is in the game, or -1 when it is not one of its positions. */
function gameIndex(): number {
  if (!game || cursor >= gamePositions.length) return -1;
  return history[0] === gamePositions[0] && history[cursor] === gamePositions[cursor] ? cursor : -1;
}

/** Shows `pgn`'s game from its start. Throws when the text is not a game. */
function openGame(pgn: string, flip?: boolean, review: (Reviewed | undefined)[] = []) {
  const next = readPgn(pgn);
  stopReview();
  game = next;
  gamePgn = pgn;
  gamePositions = positions(next);
  reviewed = review.slice(0, gamePositions.length);
  if (flip !== undefined && flip !== settings.flipped) {
    settings.flipped = flip;
    saveSettings();
    renderPalettes();
  }
  saveGame();
  goTo(0);
}

/** Shows the position `index` of the game: what was played after the position shown is the game again. */
function goTo(index: number) {
  history.splice(0, Infinity, ...gamePositions);
  cursor = Math.min(Math.max(index, 0), gamePositions.length - 1);
  changed();
}

function closeGame() {
  stopReview();
  game = null;
  gamePositions = [];
  reviewed = [];
  saveGame();
  renderGame();
}

/** Keeps the game and its review between runs. */
function saveGame() {
  try {
    if (game) localStorage.setItem("game", JSON.stringify({ pgn: gamePgn, reviewed }));
    else localStorage.removeItem("game");
  } catch {
    // The game then lasts only as long as the page.
  }
}

function restoreGame() {
  try {
    const saved = JSON.parse(localStorage.getItem("game") ?? "null") as { pgn: string; reviewed: (Reviewed | null)[] } | null;
    if (!saved) return;
    const at = currentFen();
    openGame(saved.pgn, undefined, saved.reviewed.map((r) => r ?? undefined));
    // Back where the page was, when it was in the game.
    const index = gamePositions.indexOf(at);
    if (index > 0) goTo(index);
  } catch {
    localStorage.removeItem("game");
  }
}

async function review() {
  if (!game || reviewing) return;
  stop();
  clearTimeout(pending);
  const run = ++reviews;
  reviewing = run;
  const depth = reviewDepth();
  renderGame();
  renderAnalysis();
  for (let i = 0; i < gamePositions.length; i++) {
    if (reviewed[i]) continue;
    const fen = gamePositions[i];
    const final = finalLine(fen);
    let found: Analysis | undefined;
    if (!final) {
      found = analyses.get(fen);
      if (!found || found.depth < depth) {
        const id = ++search;
        running = true;
        try {
          found = await Engine.analyse(id, fen, depth, 3000, settings.lines);
        } catch (err) {
          if (reviewing !== run) return;
          reviewing = 0;
          running = false;
          renderGame();
          renderAnalysis(err instanceof CallError ? err.message : String(err));
          return;
        }
        if (reviewing !== run) return; // stopped: what it found is not as deep as the rest
        analyses.set(fen, found);
      }
    }
    const line = final ?? found?.lines[0] ?? { rank: 1, cp: 0, mate: null, moves: [] };
    reviewed[i] = { line, best: found ? (found.bestMove ?? line.moves[0] ?? null) : null };
    renderGame();
    renderAnalysis();
  }
  reviewing = 0;
  running = false;
  saveGame();
  renderGame();
  renderAnalysis();
  if (settings.auto) soon();
}

function stopReview() {
  if (!reviewing) return;
  reviewing = 0;
  stop();
  saveGame(); // what was reviewed so far
  renderGame();
  renderAnalysis();
}

/** The verdict on each move of the game, once both positions around it are reviewed. */
function verdicts(): (Verdict | undefined)[] {
  return game!.plies.map((ply, i) => {
    const before = reviewed[i];
    const after = reviewed[i + 1];
    return before && after ? judge(ply, before.line, after.line, before.best) : undefined;
  });
}

const judgementNames = { best: "Best move", good: "Good move", inaccuracy: "Inaccuracy", mistake: "Mistake", blunder: "Blunder" };

function renderGame() {
  $("game").hidden = !game;
  if (!game) return;
  // PGN writes "?" for what it does not know.
  const tag = (name: string) => (game!.headers[name] && game!.headers[name] !== "?" ? game!.headers[name] : null);
  const elo = (name: string) => (tag(name) ? ` (${tag(name)})` : "");
  $("players").textContent = `${tag("White") ?? "White"}${elo("WhiteElo")} – ${tag("Black") ?? "Black"}${elo("BlackElo")} · ${tag("Result") ?? "*"}`;

  const judged = verdicts();
  const at = gameIndex();
  const list = $("move-list");
  list.replaceChildren();
  let current: HTMLElement | null = null;
  game.plies.forEach((ply, i) => {
    if (ply.color === "w" || i === 0) {
      const number = document.createElement("li");
      number.className = "number";
      number.textContent = `${ply.number}.`;
      list.append(number);
      if (ply.color === "b") list.append(document.createElement("li")); // the game starts with Black
    }
    const item = document.createElement("li");
    item.className = "move";
    const verdict = judged[i];
    item.textContent = ply.san;
    if (verdict) {
      item.classList.add(verdict.judgement);
      if (symbols[verdict.judgement]) {
        const nag = document.createElement("span");
        nag.className = "nag";
        nag.textContent = symbols[verdict.judgement];
        item.append(nag);
      }
      const better = verdict.better ? ` · best was ${san(gamePositions[i], [verdict.better], 1).replace(/^\d+(\.|…) /, "")}` : "";
      item.title = `${judgementNames[verdict.judgement]}${better}`;
    }
    if (i + 1 === at) {
      item.classList.add("current");
      current = item;
    }
    item.addEventListener("click", () => goTo(i + 1));
    list.append(item);
  });
  const end = document.createElement("li");
  end.className = "result";
  end.textContent = game.headers.Result ?? "";
  list.append(end);
  (current as HTMLElement | null)?.scrollIntoView({ block: "nearest" });

  const sides = (["w", "b"] as const).map((color) => accuracy(judged.filter((v, i): v is Verdict => !!v && game!.plies[i].color === color)));
  $("accuracy").textContent = sides.some((a) => a !== null) ? `Accuracy · White ${sides[0]?.toFixed(1) ?? "–"} · Black ${sides[1]?.toFixed(1) ?? "–"}` : "";

  const all = reviewed.filter(Boolean).length === gamePositions.length;
  const button = $<HTMLButtonElement>("review");
  button.textContent = reviewing ? "■ Stop the review" : all ? "Reviewed" : reviewed.some(Boolean) ? "Go on with the review" : "Review the game";
  button.disabled = !reviewing && all;
  renderGraph(judged, at);
}

/** White's chances through the game, the moves judged worst marked on it. */
function renderGraph(judged: (Verdict | undefined)[], at: number) {
  const graph = $("graph") as unknown as SVGSVGElement;
  const ns = "http://www.w3.org/2000/svg";
  graph.replaceChildren();
  graph.style.display = reviewed.some(Boolean) ? "" : "none"; // nothing to draw before the review
  const last = Math.max(1, gamePositions.length - 1);
  const x = (i: number) => (i / last) * 100;
  const y = (i: number) => (1 - whiteChances(reviewed[i]!.line, gamePositions[i].split(" ")[1] as "w" | "b")) * 40;
  const points = reviewed.flatMap((r, i) => (r ? [`${x(i)},${y(i)}`] : []));
  if (points.length > 1) {
    const area = document.createElementNS(ns, "polygon");
    area.setAttribute("class", "white");
    area.setAttribute("points", `0,40 ${points.join(" ")} ${x(reviewed.length - 1)},40`);
    graph.append(area);
  }
  const middle = document.createElementNS(ns, "line");
  middle.setAttribute("class", "middle");
  middle.setAttribute("x1", "0");
  middle.setAttribute("x2", "100");
  middle.setAttribute("y1", "20");
  middle.setAttribute("y2", "20");
  graph.append(middle);
  judged.forEach((verdict, i) => {
    if (!verdict || !["inaccuracy", "mistake", "blunder"].includes(verdict.judgement)) return;
    const dot = document.createElementNS(ns, "ellipse");
    dot.setAttribute("class", verdict.judgement);
    // The graph is stretched to its box: an ellipse in its units is a circle on screen.
    dot.setAttribute("cx", String(x(i + 1)));
    dot.setAttribute("cy", String(y(i + 1)));
    dot.setAttribute("rx", "0.9");
    dot.setAttribute("ry", "2.2");
    graph.append(dot);
  });
  if (at >= 0) {
    const mark = document.createElementNS(ns, "line");
    mark.setAttribute("class", "cursor");
    mark.setAttribute("x1", String(x(at)));
    mark.setAttribute("x2", String(x(at)));
    mark.setAttribute("y1", "0");
    mark.setAttribute("y2", "40");
    graph.append(mark);
  }
}

$("graph").addEventListener("click", (e) => {
  const box = $("graph").getBoundingClientRect();
  goTo(Math.round(((e.clientX - box.left) / box.width) * (gamePositions.length - 1)));
});
$("review").addEventListener("click", () => (reviewing ? stopReview() : review()));
$("close-game").addEventListener("click", closeGame);

$("load-pgn").addEventListener("click", () => {
  const error = $("pgn-error");
  try {
    openGame($<HTMLTextAreaElement>("pgn").value);
    error.textContent = "";
    $<HTMLDetailsElement>("pgn-box").open = false;
  } catch (err) {
    error.textContent = err instanceof Error ? err.message : String(err);
  }
});

// chess.com's finished games of a player.

const usernameInput = $<HTMLInputElement>("username");
usernameInput.value = settings.username;
let listing: AbortController | null = null;

$("player").addEventListener("submit", async (e) => {
  e.preventDefault();
  const username = usernameInput.value.trim();
  settings.username = username;
  saveSettings();
  listing?.abort();
  const controller = (listing = new AbortController());
  const status = $("games-status");
  status.hidden = false;
  status.classList.remove("error");
  status.textContent = "Loading…";
  try {
    const games = await recentGames(username, 20, controller.signal);
    renderGames(games, username);
    status.textContent = games.length ? "" : "No finished game in the last three months.";
    status.hidden = games.length > 0;
  } catch (err) {
    if (controller.signal.aborted) return;
    status.classList.add("error");
    status.textContent = err instanceof Error ? err.message : String(err);
    $("games").hidden = true;
  }
});

function renderGames(games: ArchivedGame[], username: string) {
  const list = $("games");
  list.replaceChildren();
  list.hidden = games.length === 0;
  const me = username.toLowerCase();
  for (const archived of games) {
    const black = archived.black.username.toLowerCase() === me;
    const mine = black ? archived.black : archived.white;
    const other = black ? archived.white : archived.black;
    const li = document.createElement("li");
    li.title = archived.url;
    const who = document.createElement("span");
    const outcome = document.createElement("span");
    outcome.className = mine.result === "win" ? "won" : other.result === "win" ? "lost" : "";
    outcome.textContent = result(archived);
    who.append(outcome, ` vs ${other.username} (${other.rating}) · ${archived.timeClass}`);
    const when = document.createElement("span");
    when.className = "when";
    when.textContent = archived.end.toLocaleDateString(undefined, { day: "numeric", month: "short" });
    li.append(who, when);
    li.addEventListener("click", () => {
      openGame(archived.pgn, black);
      list.querySelectorAll("li").forEach((item) => item.classList.toggle("selected", item === li));
    });
    list.append(li);
  }
}

// The 3D view, loaded the first time it is shown.

let board3d: Board3D | null = null;

async function applyView() {
  const three = settings.view === "3d";
  $<HTMLButtonElement>("view").textContent = three ? "2D" : "3D";
  $<HTMLButtonElement>("view").title = three ? "Show the board in 2D" : "Show the board in 3D";
  boardEl.hidden = three;
  (arrowsEl as unknown as HTMLElement).style.display = three ? "none" : "";
  $("board3d").hidden = !three;
  if (!three) {
    board3d?.dispose();
    board3d = null;
    return;
  }
  if (board3d) return;
  try {
    const { Board3D } = await import("./board3d");
    if (settings.view !== "3d" || board3d) return; // switched back while it loaded
    board3d = new Board3D($("board3d"));
  } catch (err) {
    settings.view = "2d";
    saveSettings();
    await applyView();
    renderAnalysis(`The 3D view is not available · ${err instanceof Error ? err.message : String(err)}`);
    return;
  }
  renderBoard();
}

// Pieces are lifted, and tools used, on the 3D board as on the 2D one; any other drag turns the camera.
$("board3d").addEventListener(
  "pointerdown",
  (e) => {
    if (!board3d || e.button !== 0) return;
    const at = tool ? board3d.squareAt(e.clientX, e.clientY) : board3d.pieceAt(e.clientX, e.clientY);
    if (at === null) return;
    e.stopPropagation(); // the camera's controls do not see it
    startDrag(e, "board", at);
  },
  { capture: true },
);

$("view").addEventListener("click", () => {
  settings.view = settings.view === "3d" ? "2d" : "3d";
  saveSettings();
  applyView();
});

// The settings.

const depthSelect = $<HTMLSelectElement>("depth");
const customDepth = $<HTMLInputElement>("custom-depth");
const timeSelect = $<HTMLSelectElement>("time");
const linesSelect = $<HTMLSelectElement>("multipv");
const piecesSelect = $<HTMLSelectElement>("pieces");
const themeSelect = $<HTMLSelectElement>("theme");
const coordsBox = $<HTMLInputElement>("coords");
const autoBox = $<HTMLInputElement>("auto");

depthSelect.value = settings.depth;
customDepth.value = String(settings.customDepth);
customDepth.hidden = settings.depth !== "custom";
timeSelect.value = String(settings.time);
linesSelect.value = String(settings.lines);
piecesSelect.value = settings.pieces;
themeSelect.value = settings.theme;
coordsBox.checked = settings.coords;
autoBox.checked = settings.auto;

function applyTheme() {
  if (settings.theme === "system") delete document.documentElement.dataset.theme;
  else document.documentElement.dataset.theme = settings.theme;
}

/** `search`: whether the setting changes what a search finds, which then starts again. */
const onSetting = (el: HTMLElement, apply: () => void, search = false) =>
  el.addEventListener("change", () => {
    apply();
    saveSettings();
    renderPalettes();
    renderAnalysis();
    if (search && settings.auto) soon();
  });

onSetting(
  depthSelect,
  () => {
    settings.depth = depthSelect.value;
    customDepth.hidden = settings.depth !== "custom";
  },
  true,
);
onSetting(customDepth, () => (settings.customDepth = Math.min(Math.max(Number(customDepth.value) || 20, 1), 60)), true);
onSetting(timeSelect, () => (settings.time = Number(timeSelect.value)), true);
onSetting(linesSelect, () => (settings.lines = Number(linesSelect.value)), true);
onSetting(piecesSelect, () => (settings.pieces = piecesSelect.value));
onSetting(themeSelect, () => {
  settings.theme = themeSelect.value as Settings["theme"];
  applyTheme();
});
onSetting(coordsBox, () => (settings.coords = coordsBox.checked));
onSetting(autoBox, () => (settings.auto = autoBox.checked), true);

// Start.

applyTheme();
applyView();
renderPalettes();
renderPosition();
renderAnalysis();
restoreGame();
if (settings.auto && !game) analyse();

Engine.info()
  .then((info) => {
    $("engine-name").textContent = info.found ? info.name : "Stockfish not found: run setup.ps1";
    $("engine-name").classList.toggle("error", !info.found);
  })
  .catch((err) => ($("engine-name").textContent = String(err)));
