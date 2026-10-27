import { useEffect, useState, type FormEvent } from "react";
import { CallError, Greeter } from "./oriel"; // generated from Services.cs on each build

export function App() {
  const [name, setName] = useState("");
  const [greeting, setGreeting] = useState<{ text: string; error: boolean } | null>(null);
  const [log, setLog] = useState<string[]>([]);

  // An event, fired in C#: listening returns the function that stops, which React calls.
  useEffect(() => Greeter.onGreeted((who) => setLog((items) => [`Greeted ${who}`, ...items])), []);

  // A call: typed arguments and result; C# exceptions arrive as CallError.
  async function greet(e: FormEvent) {
    e.preventDefault();
    try {
      setGreeting({ text: (await Greeter.greet(name)).text, error: false });
    } catch (err) {
      setGreeting({ text: err instanceof CallError ? err.message : String(err), error: true });
    }
  }

  return (
    <main>
      <h1>OrielTemplate</h1>
      <form onSubmit={greet}>
        <input value={name} onChange={(e) => setName(e.target.value)} autoComplete="off" placeholder="Your name" autoFocus />
        <button type="submit">Greet</button>
      </form>
      <output role="status" className={greeting?.error ? "error" : ""}>
        {greeting?.text}
      </output>
      <ul>
        {log.map((item, i) => (
          <li key={log.length - i}>{item}</li>
        ))}
      </ul>
    </main>
  );
}
