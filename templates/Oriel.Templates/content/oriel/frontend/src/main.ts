import "./style.css";
import { CallError, Greeter } from "./oriel"; // generated from Services.cs on each build

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;

// A call: typed arguments and result; C# exceptions arrive as CallError.
$<HTMLFormElement>("greet").addEventListener("submit", async (e) => {
  e.preventDefault();
  const output = $("greeting");
  try {
    const greeting = await Greeter.greet($<HTMLInputElement>("name").value);
    output.textContent = greeting.text;
    output.className = "";
  } catch (err) {
    output.textContent = err instanceof CallError ? err.message : String(err);
    output.className = "error";
  }
});

// An event, fired in C#.
Greeter.onGreeted((name) => {
  const item = document.createElement("li");
  item.textContent = `Greeted ${name}`;
  $("log").prepend(item);
});
