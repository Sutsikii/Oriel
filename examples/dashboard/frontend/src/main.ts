import "./style.css";
import { Metrics, type LogEntry, type Sample } from "./oriel";

const HISTORY = 600; // samples drawn: 5 seconds at 120 Hz

/** A line chart of the last values, drawn on a canvas. */
class Chart {
  private readonly values: number[] = [];
  private readonly context: CanvasRenderingContext2D;

  constructor(
    private readonly canvas: HTMLCanvasElement,
    private readonly color: string,
    private readonly range?: [number, number],
  ) {
    this.context = canvas.getContext("2d")!;
  }

  push(value: number): void {
    this.values.push(value);
    if (this.values.length > HISTORY) this.values.shift();
  }

  draw(): void {
    const { canvas, context, values } = this;
    const width = (canvas.width = canvas.clientWidth * devicePixelRatio);
    const height = (canvas.height = canvas.clientHeight * devicePixelRatio);
    if (values.length < 2) return;
    const [low, high] = this.range ?? [Math.min(...values) * 0.95, Math.max(...values) * 1.05 + 1e-9];
    context.lineWidth = 2 * devicePixelRatio;
    context.strokeStyle = this.color;
    context.beginPath();
    values.forEach((v, i) => {
      const x = (i / (HISTORY - 1)) * width;
      const y = height - ((v - low) / (high - low || 1)) * height;
      if (i === 0) context.moveTo(x, y);
      else context.lineTo(x, y);
    });
    context.stroke();
  }
}

const $ = (id: string) => document.getElementById(id)!;
const signal = new Chart($("signal") as HTMLCanvasElement, "#2563eb", [-1, 1]);
const cpu = new Chart($("cpu") as HTMLCanvasElement, "#16a34a", [0, 100]);
const memory = new Chart($("memory") as HTMLCanvasElement, "#d97706");

// Samples come at 120 Hz, coalesced: the page gets the latest when it is
// ready, and draws once per frame however many came.
let latest: Sample | null = null;
let received = 0;
Metrics.onSampled((sample) => {
  latest = sample;
  received++;
  signal.push(sample.signal);
  cpu.push(sample.cpu);
  memory.push(sample.memoryMb);
});

let second = performance.now();
function frame(now: number): void {
  if (latest) {
    $("signal-value").textContent = latest.signal.toFixed(2);
    $("cpu-value").textContent = `${latest.cpu.toFixed(1)} %`;
    $("memory-value").textContent = `${latest.memoryMb.toFixed(0)} MB, heap ${latest.heapMb.toFixed(0)} MB, ${latest.threads} threads`;
  }
  signal.draw();
  cpu.draw();
  memory.draw();
  if (now - second >= 1000) {
    $("rate").textContent = `${received} samples a second`;
    received = 0;
    second = now;
  }
  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);

// The log is a stream: `for await` takes its entries as C# writes them.
// Pausing breaks the loop, which cancels the stream in C#.
const log = $("log");
let paused = false;

async function follow(): Promise<void> {
  for await (const entry of Metrics.log()) {
    if (paused) break;
    show(entry);
  }
}

function show(entry: LogEntry): void {
  const item = document.createElement("li");
  item.className = entry.level;
  item.textContent = `${new Date(entry.at).toLocaleTimeString()}  ${entry.message}`;
  log.prepend(item);
  while (log.childElementCount > 200) log.lastElementChild!.remove();
}

$("pause").addEventListener("click", () => {
  paused = !paused;
  $("pause").textContent = paused ? "Resume" : "Pause";
  if (!paused) void follow();
});
void follow();
