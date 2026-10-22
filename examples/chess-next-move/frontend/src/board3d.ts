// The board in 3D, with three.js: the same position, arrows and highlights as
// the 2D board, pieces turned on a lathe, and a camera to orbit around it.
// main.ts loads this module only when the 3D view is first shown.

import * as THREE from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import type { Piece } from "./position";

/** A move, by its squares: 0 is a8, 63 is h1. */
export type Squares = [from: number, to: number];

export interface Arrow {
  squares: Squares;
  /** A CSS color: rgba(21, 120, 27, 0.85). */
  color: string;
}

export interface Marks {
  best: Squares | null;
  /** The game's move that led to the position. */
  last: Squares | null;
}

const LIGHT = "#f0d9b5";
const DARK = "#b58863";
const BORDER = 0.45;

/** Where the centre of square `i` is, from the side the board is seen from: a8 far left, unless flipped. */
export function place(i: number, flipped: boolean) {
  const d = flipped ? 63 - i : i;
  return new THREE.Vector3((d % 8) - 3.5, 0, Math.floor(d / 8) - 3.5);
}

/** The square at the point (x, z) of the board's top, or null when it is off the squares. */
export function squareOf(x: number, z: number, flipped: boolean): number | null {
  const col = Math.floor(x + 4);
  const row = Math.floor(z + 4);
  if (col < 0 || col > 7 || row < 0 || row > 7) return null;
  const d = row * 8 + col;
  return flipped ? 63 - d : d;
}

// The pieces: profiles turned on a lathe, a square being 1 wide.

const v = (x: number, y: number) => new THREE.Vector2(x, y);

/** Points on a circle of centre (0, cy), from angle `from` to 90°: the top of a piece. */
function dome(cy: number, r: number, from: number, steps = 10): THREE.Vector2[] {
  const points: THREE.Vector2[] = [];
  for (let s = 0; s <= steps; s++) {
    const a = from + ((Math.PI / 2 - from) * s) / steps;
    points.push(v(Math.max(0, r * Math.cos(a)), cy + r * Math.sin(a)));
  }
  return points;
}

const base = (r = 0.36) => [v(0, 0), v(r, 0), v(r, 0.06), v(r - 0.03, 0.1), v(r - 0.07, 0.13)];

const profiles: Record<string, THREE.Vector2[]> = {
  P: [...base(0.32), v(0.2, 0.17), v(0.12, 0.24), v(0.1, 0.38), v(0.16, 0.41), v(0.16, 0.44), v(0.09, 0.47), ...dome(0.6, 0.15, -1.2)],
  R: [...base(), v(0.26, 0.17), v(0.21, 0.24), v(0.19, 0.62), v(0.25, 0.66), v(0.27, 0.86), v(0.19, 0.86), v(0.19, 0.8), v(0, 0.8)],
  B: [
    ...base(),
    v(0.22, 0.17),
    v(0.13, 0.26),
    v(0.1, 0.56),
    v(0.19, 0.6),
    v(0.19, 0.64),
    v(0.1, 0.67),
    v(0.16, 0.76),
    v(0.17, 0.86),
    v(0.12, 0.96),
    v(0.05, 1.02),
    ...dome(1.06, 0.05, -1.4, 6),
  ],
  Q: [
    ...base(0.38),
    v(0.26, 0.17),
    v(0.15, 0.27),
    v(0.11, 0.7),
    v(0.21, 0.75),
    v(0.21, 0.79),
    v(0.13, 0.83),
    v(0.23, 1.05),
    v(0.18, 1.07),
    v(0.1, 1.09),
    ...dome(1.15, 0.07, -1.2, 6),
  ],
  K: [
    ...base(0.38),
    v(0.26, 0.17),
    v(0.15, 0.27),
    v(0.12, 0.76),
    v(0.22, 0.81),
    v(0.22, 0.85),
    v(0.14, 0.89),
    v(0.21, 1.1),
    v(0.13, 1.14),
    v(0, 1.15),
  ],
  // The knight's stand; its head is extruded.
  N: [...base(), v(0.26, 0.17), v(0.24, 0.24), v(0, 0.24)],
};

/** The knight's head, seen from its side, facing +x. */
function knightHead(): THREE.BufferGeometry {
  const shape = new THREE.Shape();
  const points = [
    [-0.24, 0.2],
    [0.2, 0.2],
    [0.14, 0.44],
    [0.1, 0.52],
    [0.3, 0.6],
    [0.36, 0.7],
    [0.32, 0.78],
    [0.16, 0.86],
    [0.06, 0.96],
    [0.02, 1.04],
    [-0.04, 0.96],
    [-0.14, 0.9],
    [-0.24, 0.74],
    [-0.27, 0.48],
  ];
  shape.moveTo(points[0][0], points[0][1]);
  for (const [x, y] of points.slice(1)) shape.lineTo(x, y);
  const geometry = new THREE.ExtrudeGeometry(shape, { depth: 0.18, bevelEnabled: true, bevelThickness: 0.05, bevelSize: 0.04, bevelSegments: 3, curveSegments: 4 });
  geometry.translate(0, 0, -0.09);
  return geometry;
}

let geometries: Record<string, THREE.BufferGeometry[]> | null = null;

function pieceGeometries(): Record<string, THREE.BufferGeometry[]> {
  if (geometries) return geometries;
  geometries = {};
  for (const [kind, profile] of Object.entries(profiles)) geometries[kind] = [new THREE.LatheGeometry(profile, 40)];
  geometries.N.push(knightHead());
  // The king's cross.
  const upright = new THREE.BoxGeometry(0.07, 0.24, 0.07).translate(0, 1.25, 0);
  const arm = new THREE.BoxGeometry(0.2, 0.07, 0.07).translate(0, 1.27, 0);
  geometries.K.push(upright, arm);
  return geometries;
}

const materials = {
  w: new THREE.MeshStandardMaterial({ color: "#efe6d2", roughness: 0.45, metalness: 0.05 }),
  b: new THREE.MeshStandardMaterial({ color: "#2e2925", roughness: 0.35, metalness: 0.1 }),
};

function makePiece(piece: Piece): THREE.Group {
  const group = new THREE.Group();
  for (const geometry of pieceGeometries()[piece[1]]) {
    const mesh = new THREE.Mesh(geometry, materials[piece[0] as "w" | "b"]);
    mesh.castShadow = true;
    mesh.receiveShadow = true;
    group.add(mesh);
  }
  group.userData.piece = piece;
  return group;
}

/** The board's top: the squares, and the coordinates in its border, drawn as the 2D board shows them. */
function boardTexture(flipped: boolean, coords: boolean): THREE.CanvasTexture {
  const size = 1024;
  const canvas = document.createElement("canvas");
  canvas.width = canvas.height = size;
  const ctx = canvas.getContext("2d")!;
  const unit = size / (8 + 2 * BORDER);
  const edge = BORDER * unit;
  ctx.fillStyle = "#5a3c25";
  ctx.fillRect(0, 0, size, size);
  for (let d = 0; d < 64; d++) {
    const col = d % 8;
    const row = Math.floor(d / 8);
    ctx.fillStyle = (col + row) % 2 === 1 ? DARK : LIGHT;
    ctx.fillRect(edge + col * unit, edge + row * unit, unit + 0.5, unit + 0.5);
  }
  if (coords) {
    ctx.fillStyle = "#e9d8bd";
    ctx.font = `600 ${Math.round(edge * 0.55)}px system-ui, "Segoe UI", sans-serif`;
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    for (let n = 0; n < 8; n++) {
      const file = "abcdefgh"[flipped ? 7 - n : n];
      const rank = String(flipped ? n + 1 : 8 - n);
      ctx.fillText(file, edge + (n + 0.5) * unit, size - edge / 2);
      ctx.fillText(rank, edge / 2, edge + (n + 0.5) * unit);
    }
  }
  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  texture.anisotropy = 8;
  return texture;
}

/** A flat arrow from `from` to `to`, on the board. */
function makeArrow(from: THREE.Vector3, to: THREE.Vector3, color: string): THREE.Mesh {
  const length = from.distanceTo(to);
  const head = 0.34;
  const shaft = 0.08;
  const wing = 0.25;
  const shape = new THREE.Shape();
  shape.moveTo(0, -shaft);
  shape.lineTo(length - head, -shaft);
  shape.lineTo(length - head, -wing);
  shape.lineTo(length, 0);
  shape.lineTo(length - head, wing);
  shape.lineTo(length - head, shaft);
  shape.lineTo(0, shaft);
  const geometry = new THREE.ShapeGeometry(shape).rotateX(-Math.PI / 2);
  const parsed = parseColor(color);
  const material = new THREE.MeshBasicMaterial({ color: parsed.color, opacity: parsed.alpha, transparent: true, depthTest: false });
  const mesh = new THREE.Mesh(geometry, material);
  mesh.position.set(from.x, 0.02, from.z);
  mesh.rotation.y = Math.atan2(-(to.z - from.z), to.x - from.x);
  mesh.renderOrder = 2; // over the pieces, as on the 2D board
  return mesh;
}

/** A CSS color, rgba() included, as three.js takes it: the color and its opacity apart. */
export function parseColor(css: string): { color: THREE.Color; alpha: number } {
  const rgba = css.match(/rgba?\(([^)]+)\)/);
  if (!rgba) return { color: new THREE.Color(css.trim() || "#000"), alpha: 1 };
  const [r, g, b, a = "1"] = rgba[1].split(",").map((s) => s.trim());
  return { color: new THREE.Color(`rgb(${r}, ${g}, ${b})`), alpha: Number(a) };
}

interface Motion {
  group: THREE.Group;
  from: THREE.Vector3;
  to: THREE.Vector3;
  start: number;
  /** A knight jumps; other pieces slide. */
  hop: number;
}

const MOVE_MS = 220;

export class Board3D {
  readonly canvas: HTMLCanvasElement;
  private readonly renderer: THREE.WebGLRenderer;
  private readonly scene = new THREE.Scene();
  private readonly camera = new THREE.PerspectiveCamera(38, 1, 0.1, 100);
  private readonly controls: OrbitControls;
  private readonly top: THREE.MeshStandardMaterial;
  private readonly marks = new THREE.Group();
  private readonly arrows = new THREE.Group();
  private readonly raycaster = new THREE.Raycaster();
  private readonly plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
  /** The pieces, by square. */
  private pieces = new Map<number, THREE.Group>();
  private motions: Motion[] = [];
  private flipped = false;
  private coords = true;
  private lifted: THREE.Group | null = null;
  private frame = 0;
  private readonly host: HTMLElement;
  private readonly resize: ResizeObserver;

  constructor(host: HTMLElement) {
    this.host = host;
    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    this.renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.canvas = this.renderer.domElement;
    host.append(this.canvas);

    this.scene.add(new THREE.HemisphereLight("#fff8ee", "#3a3026", 1.4));
    const sun = new THREE.DirectionalLight("#ffffff", 2.2);
    sun.position.set(-4, 10, 6);
    sun.castShadow = true;
    sun.shadow.mapSize.set(2048, 2048);
    sun.shadow.camera.left = sun.shadow.camera.bottom = -6;
    sun.shadow.camera.right = sun.shadow.camera.top = 6;
    sun.shadow.bias = -0.0005;
    this.scene.add(sun);

    // The board: its top drawn on a canvas, its sides wood.
    const wood = new THREE.MeshStandardMaterial({ color: "#4a3120", roughness: 0.7 });
    this.top = new THREE.MeshStandardMaterial({ map: boardTexture(false, true), roughness: 0.55 });
    const width = 8 + 2 * BORDER;
    const board = new THREE.Mesh(new THREE.BoxGeometry(width, 0.3, width), [wood, wood, this.top, wood, wood, wood]);
    board.position.y = -0.15;
    board.receiveShadow = true;
    this.scene.add(board, this.marks, this.arrows);

    this.camera.position.set(0, 12.5, 9.6);
    this.controls = new OrbitControls(this.camera, this.canvas);
    this.controls.target.set(0, 0, 0.45);
    this.controls.enableDamping = true;
    this.controls.minDistance = 6;
    this.controls.maxDistance = 22;
    this.controls.maxPolarAngle = Math.PI * 0.46; // never under the board
    this.controls.addEventListener("change", () => this.render());

    this.resize = new ResizeObserver(() => this.fit());
    this.resize.observe(host);
    this.fit();
  }

  /** Shows `board`, the pieces that moved going to their new squares. */
  setPosition(board: (Piece | null)[], flipped: boolean, coords: boolean) {
    if (flipped !== this.flipped || coords !== this.coords) {
      this.flipped = flipped;
      this.coords = coords;
      this.top.map?.dispose();
      this.top.map = boardTexture(flipped, coords);
      this.top.needsUpdate = true;
      for (const [i, group] of this.pieces) this.put(group, i);
    }

    const next = new Map<number, THREE.Group>();
    const gone = new Map<number, THREE.Group>();
    for (const [i, group] of this.pieces) {
      if (board[i] === group.userData.piece) next.set(i, group);
      else gone.set(i, group);
    }
    const arrivals = board.flatMap((piece, i) => (piece && !next.has(i) ? [i] : []));
    for (const i of arrivals) {
      const piece = board[i]!;
      // The nearest piece of its kind that left its square: the one that moved there.
      let from: number | null = null;
      for (const [j, group] of gone)
        if (group.userData.piece === piece && (from === null || distance(i, j) < distance(i, from))) from = j;
      let group: THREE.Group;
      if (from !== null) {
        group = gone.get(from)!;
        gone.delete(from);
        this.slide(group, i, piece[1] === "N");
      } else {
        group = makePiece(piece);
        this.put(group, i);
        this.scene.add(group);
      }
      next.set(i, group);
    }
    for (const group of gone.values()) this.scene.remove(group); // taken, or removed
    // A piece dropped back where it was.
    if (this.lifted && [...next.values()].includes(this.lifted)) {
      const square = [...next].find(([, g]) => g === this.lifted)![0];
      this.slide(this.lifted, square, false);
    }
    this.lifted = null;
    this.pieces = next;
    this.render();
  }

  /** The arrows of the engine's lines, and the squares of the best and the last moves. */
  setMarks(arrows: Arrow[], marks: Marks) {
    for (const group of [this.arrows, this.marks]) {
      for (const child of group.children) {
        const mesh = child as THREE.Mesh;
        mesh.geometry.dispose();
        (mesh.material as THREE.Material).dispose();
      }
      group.clear();
    }
    for (const { squares, color } of arrows) this.arrows.add(makeArrow(place(squares[0], this.flipped), place(squares[1], this.flipped), color));
    const square = (i: number, color: string, opacity: number) => {
      const mesh = new THREE.Mesh(
        new THREE.PlaneGeometry(1, 1).rotateX(-Math.PI / 2),
        new THREE.MeshBasicMaterial({ color, opacity, transparent: true, depthWrite: false }),
      );
      mesh.position.copy(place(i, this.flipped)).setY(0.004);
      this.marks.add(mesh);
    };
    if (marks.last) for (const i of marks.last) square(i, "#ffaa00", 0.32);
    if (marks.best) {
      square(marks.best[0], "#9bc700", 0.42);
      square(marks.best[1], "#9bc700", 0.6);
    }
    this.render();
  }

  /** The square under the point (x, y) of the page, or null when it is off the board. */
  squareAt(x: number, y: number): number | null {
    const point = this.pointOnBoard(x, y);
    return point ? squareOf(point.x, point.z, this.flipped) : null;
  }

  /** The square of the piece under the point (x, y), the piece itself counting, not only its square. */
  pieceAt(x: number, y: number): number | null {
    this.aim(x, y);
    const hit = this.raycaster.intersectObjects([...this.pieces.values()], true)[0];
    if (hit) for (const [i, group] of this.pieces) if (group === hit.object.parent) return i;
    const i = this.squareAt(x, y);
    return i !== null && this.pieces.has(i) ? i : null;
  }

  /** Lifts the piece of square `i`, which then follows the pointer until the position changes. */
  lift(i: number) {
    this.lifted = this.pieces.get(i) ?? null;
    this.motions = this.motions.filter((m) => m.group !== this.lifted);
  }

  drag(x: number, y: number) {
    const point = this.pointOnBoard(x, y);
    if (!this.lifted || !point) return;
    this.lifted.position.set(point.x, 0.35, point.z);
    this.render();
  }

  /** Stops drawing, and lets the page have its canvas back. */
  dispose() {
    cancelAnimationFrame(this.frame);
    this.resize.disconnect();
    this.controls.dispose();
    this.renderer.dispose();
    // Pages get a few WebGL contexts only: switching views back and forth must not use them up.
    this.renderer.forceContextLoss();
    this.canvas.remove();
  }

  private put(group: THREE.Group, i: number) {
    group.position.copy(place(i, this.flipped));
    // Knights look at the other side.
    const white = group.userData.piece[0] === "w";
    group.rotation.y = white !== this.flipped ? Math.PI / 2 : -Math.PI / 2;
  }

  private slide(group: THREE.Group, i: number, hop: boolean) {
    const to = place(i, this.flipped);
    this.motions = this.motions.filter((m) => m.group !== group);
    this.motions.push({ group, from: group.position.clone(), to, start: performance.now(), hop: hop ? 0.6 : 0 });
  }

  private aim(x: number, y: number) {
    const box = this.canvas.getBoundingClientRect();
    const ndc = new THREE.Vector2(((x - box.left) / box.width) * 2 - 1, -((y - box.top) / box.height) * 2 + 1);
    this.raycaster.setFromCamera(ndc, this.camera);
  }

  private pointOnBoard(x: number, y: number): THREE.Vector3 | null {
    this.aim(x, y);
    return this.raycaster.ray.intersectPlane(this.plane, new THREE.Vector3());
  }

  private fit() {
    const { width, height } = this.host.getBoundingClientRect();
    if (width === 0 || height === 0) return;
    this.renderer.setSize(width, height, false);
    this.camera.aspect = width / height;
    this.camera.updateProjectionMatrix();
    this.render();
  }

  /** Draws a frame, and asks for the next one while something moves: nothing is drawn while all is still. */
  private render() {
    if (this.frame) return;
    this.frame = requestAnimationFrame(() => {
      this.frame = 0;
      const now = performance.now();
      this.motions = this.motions.filter((m) => {
        const t = Math.min(1, (now - m.start) / MOVE_MS);
        const eased = t * t * (3 - 2 * t);
        m.group.position.lerpVectors(m.from, m.to, eased);
        m.group.position.y = m.hop * Math.sin(Math.PI * t);
        return t < 1;
      });
      const moving = this.controls.update();
      this.renderer.render(this.scene, this.camera);
      if (moving || this.motions.length) this.render();
    });
  }
}

const distance = (a: number, b: number) => Math.hypot((a % 8) - (b % 8), Math.floor(a / 8) - Math.floor(b / 8));
