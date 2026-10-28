import { StrictMode, useState, type FormEvent } from "react";
import { createRoot } from "react-dom/client";
import { useOrielEvent, useOrielQuery } from "oriel-react";
import { CallError, Tasks } from "./oriel";
import "./style.css";

function App() {
  // Called once, then again whenever C# says the list changed: in this window or another.
  const tasks = useOrielQuery(() => Tasks.all(), [], { refreshOn: [Tasks.onChanged] });
  const [left, setLeft] = useState<number | null>(null);
  useOrielEvent(Tasks.onChanged, setLeft);
  const [title, setTitle] = useState("");
  const [error, setError] = useState("");

  async function add(e: FormEvent) {
    e.preventDefault();
    try {
      await Tasks.add(title);
      setTitle("");
      setError("");
    } catch (err) {
      setError(err instanceof CallError && err.code === "invalid-title" ? err.message : String(err));
    }
  }

  return (
    <main>
      <h1>Tasks</h1>
      <form onSubmit={add}>
        <input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Something to do" autoFocus />
        <button type="submit">Add</button>
      </form>
      {error && <p className="error">{error}</p>}
      <ul>
        {tasks.data?.map((task) => (
          <li key={task.id}>
            <label className={task.done ? "done" : ""}>
              <input type="checkbox" checked={task.done} onChange={() => void Tasks.toggle(task.id)} />
              {task.title}
            </label>
          </li>
        ))}
      </ul>
      <footer>
        <span>{left ?? tasks.data?.filter((t) => !t.done).length ?? 0} left</span>
        <button type="button" onClick={() => void Tasks.clear()}>Clear done</button>
      </footer>
    </main>
  );
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
