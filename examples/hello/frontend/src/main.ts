import "./style.css";
import { CallError, Clock, Greeter, Machine, type Tone } from "./oriel";

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;

// Calls: typed arguments, typed results, C# exceptions as CallError.
$<HTMLFormElement>("greet").addEventListener("submit", async (e) => {
  e.preventDefault();
  const output = $("greeting");
  const tone = $<HTMLSelectElement>("tone").value as Tone;
  try {
    const greeting = await Greeter.greet($<HTMLInputElement>("name").value, tone);
    output.textContent = greeting.text;
    output.className = "ok";
  } catch (err) {
    output.textContent = err instanceof CallError ? err.message : String(err);
    output.className = "error";
  }
});

// Events: the clock ticks in C#.
const clock = $("clock");
Clock.onTick((now) => {
  clock.textContent = new Date(now).toLocaleTimeString();
});

// Records come back as plain objects.
const info = await Machine.describe();
const rows: [string, string][] = [
  ["System", info.system],
  ["Runtime", info.runtime],
  ["Processors", String(info.processors)],
  ["Native AOT", info.nativeAot ? "yes" : "no"],
];
$("machine").replaceChildren(
  ...rows.flatMap(([term, value]) => {
    const dt = document.createElement("dt");
    dt.textContent = term;
    const dd = document.createElement("dd");
    dd.textContent = value;
    return [dt, dd];
  }),
);
