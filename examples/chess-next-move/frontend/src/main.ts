import "./style.css";
import { Chess, validateFen } from "chess.js";
import { CallError, Engine, type Analysis, type Line } from "./oriel";
import { EMPTY, KINGS, PIECES, START, format, parse, possibleCastling, squareIndex, squareName, type Piece, type Position } from "./position";

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
}

const defaults: Settings = { depth: "18", customDepth: 30, time: 5000, lines: 3, pieces: "cburnett", theme: "system", coords: true, auto: true, flipped: false };

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
  const r = boardEl.getBoundingClientRect();
  if (x < r.left || x >= r.right || y < r.top || y >= r.bottom) return null;
  const col = Math.floor(((x - r.left) / r.width) * 8);
  const row = Math.floor(((y - r.top) / r.height) * 8);
  return shown(row * 8 + col);
}

function renderBoard() {
  const p = current();
  const best = bestMove();
  squares.forEach((square, d) => {
    const i = shown(d);
    const file = i % 8;
    const rank = 8 - Math.floor(i / 8);
    square.classList.toggle("dark", (file + rank) % 2 === 1); // a1 is dark
    square.classList.toggle("from", best?.slice(0, 2) === squareName(i));
    square.classList.toggle("to", best?.slice(2, 4) === squareName(i));
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
  const size = boardEl.getBoundingClientRect().width / 8;
  ghost.style.width = ghost.style.height = `${size}px`;
  drag = { piece, from: source === "board" ? from : null, ghost, x: e.clientX, y: e.clientY, moved: false };
  moveGhost(e.clientX, e.clientY);
}

function moveGhost(x: number, y: number) {
  if (!drag) return;
  const half = parseFloat(drag.ghost.style.width) / 2;
  drag.ghost.style.transform = `translate(${x - half}px, ${y - half}px)`;
}

document.addEventListener("pointermove", (e) => {
  if (!drag) return;
  if (!drag.moved && Math.hypot(e.clientX - drag.x, e.clientY - drag.y) > 4) {
    drag.moved = true;
    document.body.append(drag.ghost);
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
    edit((p) => {
      p.board[from] = null;
      if (target !== null) p.board[target] = was.piece; // dropped off the board: removed
    });
  } else if (target !== null) {
    edit((p) => (p.board[target] = was.piece));
  }
});

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

/** The analysis of the position shown, when there is one. */
const shownAnalysis = () => (analysis && analysis.fen === currentFen() ? analysis : null);
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
  else if (running)
    status.textContent = a ? `Analysing · depth ${a.depth}/${searchDepth()} · ${compact(a.nps)} nodes/s` : "Analysing…";
  else if (a) status.textContent = `Done · depth ${a.depth} · ${compact(a.nodes)} nodes · best ${san(a.fen, [bestMove() ?? ""], 1) || "none"}`;
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

function renderArrows(a: Analysis | null) {
  arrowsEl.replaceChildren();
  const ns = "http://www.w3.org/2000/svg";
  const place = (name: string) => {
    const i = squareIndex(name);
    const d = settings.flipped ? 63 - i : i;
    return { x: (d % 8) + 0.5, y: Math.floor(d / 8) + 0.5 };
  };
  // The worst line first, so that the best one is drawn on top.
  const lines = (a?.lines ?? []).slice(0, settings.lines).reverse();
  for (const line of lines) {
    const move = line.moves[0];
    if (!move) continue;
    const from = place(move.slice(0, 2));
    const to = place(move.slice(2, 4));
    const length = Math.hypot(to.x - from.x, to.y - from.y);
    const ux = (to.x - from.x) / length;
    const uy = (to.y - from.y) / length;
    const head = 0.32;
    const end = { x: to.x - ux * head, y: to.y - uy * head };
    const g = document.createElementNS(ns, "g");
    g.setAttribute("class", `arrow rank${line.rank}`);
    const shaft = document.createElementNS(ns, "line");
    shaft.setAttribute("x1", String(from.x));
    shaft.setAttribute("y1", String(from.y));
    shaft.setAttribute("x2", String(end.x));
    shaft.setAttribute("y2", String(end.y));
    const tip = document.createElementNS(ns, "polygon");
    const w = 0.24;
    tip.setAttribute("points", `${to.x},${to.y} ${end.x - uy * w},${end.y + ux * w} ${end.x + uy * w},${end.y - ux * w}`);
    g.append(shaft, tip);
    if (settings.lines > 1) {
      const badge = document.createElementNS(ns, "circle");
      badge.setAttribute("cx", String(from.x));
      badge.setAttribute("cy", String(from.y));
      badge.setAttribute("r", "0.18");
      const label = document.createElementNS(ns, "text");
      label.setAttribute("x", String(from.x));
      label.setAttribute("y", String(from.y));
      label.textContent = String(line.rank);
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
}

/** The position shown changed: the search of the one before is of no use. */
function changed() {
  stop();
  analysis = null;
  renderPosition();
  renderAnalysis();
  if (settings.auto) soon();
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
  if (e.target instanceof HTMLInputElement || e.target instanceof HTMLSelectElement) return;
  if (e.key === "ArrowLeft") step(-1);
  else if (e.key === "ArrowRight") step(1);
  else if (e.key === " " || e.key === "Enter") {
    e.preventDefault();
    $("analyse").click();
  } else if (e.key === "m") $("move").click();
  else if (e.key === "Escape" && tool) selectTool(tool);
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
renderPalettes();
renderPosition();
renderAnalysis();
if (settings.auto) analyse();

Engine.info()
  .then((info) => {
    $("engine-name").textContent = info.found ? info.name : "Stockfish not found: run setup.ps1";
    $("engine-name").classList.toggle("error", !info.found);
  })
  .catch((err) => ($("engine-name").textContent = String(err)));
