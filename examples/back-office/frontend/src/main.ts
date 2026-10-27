import "./style.css";
import { CallError, Customers, type Customer, type SortBy, type Tier } from "./oriel";

const ROW = 32; // px, as in style.css
const PAGE = 200; // rows a call brings

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const viewport = $<HTMLDivElement>("viewport");
const rows = $<HTMLDivElement>("rows");
const money = new Intl.NumberFormat(undefined, { style: "currency", currency: "USD" });
const day = new Intl.DateTimeFormat(undefined, { dateStyle: "medium" });

// The grid shows the rows in view only, out of pages fetched as it scrolls.
let total = 0;
let search = "";
let sort: SortBy = "id";
let descending = false;
let generation = 0; // a new query drops the pages of the one before
const pages = new Map<number, Customer[] | Promise<void>>();

function query(): void {
  generation++;
  pages.clear();
  viewport.scrollTop = 0;
  void load(0).then(render);
}

function load(page: number): Promise<void> {
  const known = pages.get(page);
  if (known instanceof Promise) return known;
  if (known) return Promise.resolve();
  const asked = generation;
  const loading = Customers.page(page * PAGE, PAGE, search, sort, descending).then((result) => {
    if (asked !== generation) return;
    total = result.total;
    pages.set(page, result.rows);
    $("count").textContent = `${total.toLocaleString()} customers`;
  });
  pages.set(page, loading);
  return loading;
}

function render(): void {
  rows.style.height = `${total * ROW}px`;
  const first = Math.max(0, Math.floor(viewport.scrollTop / ROW) - 10);
  const last = Math.min(total, Math.ceil((viewport.scrollTop + viewport.clientHeight) / ROW) + 10);
  const missing = new Set<number>();
  const html: string[] = [];
  for (let i = first; i < last; i++) {
    const page = pages.get(Math.floor(i / PAGE));
    const customer = Array.isArray(page) ? page[i % PAGE] : undefined;
    if (!customer) missing.add(Math.floor(i / PAGE));
    html.push(customer ? rowHtml(customer, i) : `<div class="row loading" style="top:${i * ROW}px"></div>`);
  }
  rows.innerHTML = html.join("");
  for (const page of missing) void load(page).then(render);
}

function rowHtml(c: Customer, index: number): string {
  const e = (s: string) => s.replace(/[&<>"]/g, (ch) => `&#${ch.charCodeAt(0)};`);
  return `<div class="row" role="row" data-id="${c.id}" style="top:${index * ROW}px">
    <span>${c.id}</span><span>${e(c.name)}</span><span>${e(c.email)}</span>
    <span class="tier ${c.tier}">${c.tier}</span><span class="number">${money.format(c.balance)}</span>
    <span>${day.format(new Date(c.joined))}</span></div>`;
}

viewport.addEventListener("scroll", () => requestAnimationFrame(render));
new ResizeObserver(render).observe(viewport);

let timer = 0;
$<HTMLInputElement>("search").addEventListener("input", (e) => {
  clearTimeout(timer);
  timer = setTimeout(() => {
    search = (e.target as HTMLInputElement).value;
    query();
  }, 200);
});

document.querySelectorAll<HTMLButtonElement>(".head button").forEach((button) =>
  button.addEventListener("click", () => {
    const by = button.dataset.sort as SortBy;
    descending = by === sort ? !descending : false;
    sort = by;
    document.querySelectorAll(".head button").forEach((b) => b.removeAttribute("aria-sort"));
    button.setAttribute("aria-sort", descending ? "descending" : "ascending");
    query();
  }),
);

// Editing: the form shows what C# found wrong, field by field.
const editor = $<HTMLDialogElement>("editor");
const form = $<HTMLFormElement>("form");
let editing: Customer | null = null;

function edit(customer: Customer | null): void {
  editing = customer;
  $("editor-title").textContent = customer ? `Customer ${customer.id}` : "New customer";
  field("name").value = customer?.name ?? "";
  field("email").value = customer?.email ?? "";
  field("tier").value = customer?.tier ?? "free";
  field("balance").value = String(customer?.balance ?? 0);
  $("delete").hidden = !customer;
  showErrors({});
  editor.showModal();
}

function field(name: string): HTMLInputElement {
  return form.elements.namedItem(name) as HTMLInputElement;
}

function showErrors(errors: Record<string, string>, message = ""): void {
  form.querySelectorAll<HTMLElement>("[data-error]").forEach((small) => {
    small.textContent = errors[small.dataset.error!] ?? "";
    field(small.dataset.error!).toggleAttribute("aria-invalid", small.dataset.error! in errors);
  });
  $("form-error").textContent = message;
}

rows.addEventListener("click", (e) => {
  const id = Number((e.target as HTMLElement).closest<HTMLElement>(".row[data-id]")?.dataset.id);
  for (const page of pages.values()) {
    const found = Array.isArray(page) && page.find((c) => c.id === id);
    if (found) return edit(found);
  }
});
$("add").addEventListener("click", () => edit(null));
$("cancel").addEventListener("click", () => editor.close());

form.addEventListener("submit", async (e) => {
  e.preventDefault();
  try {
    await Customers.save({
      id: editing?.id ?? 0,
      name: field("name").value,
      email: field("email").value,
      tier: field("tier").value as Tier,
      balance: Number(field("balance").value),
      joined: editing?.joined ?? new Date().toISOString(),
    });
    editor.close();
  } catch (err) {
    if (err instanceof CallError && err.code === "invalid") showErrors(err.data.errors);
    else showErrors({}, err instanceof Error ? err.message : String(err));
  }
});

$("delete").addEventListener("click", async () => {
  if (!editing || !confirm(`Delete ${editing.name}?`)) return;
  try {
    await Customers.delete(editing.id);
    editor.close();
  } catch (err) {
    showErrors({}, err instanceof CallError && err.code === "not-found" ? "Deleted already." : String(err));
  }
});

// Saved or deleted in any window: the grid shows it.
Customers.onSaved((saved) => {
  for (const page of pages.values()) {
    if (!Array.isArray(page)) continue;
    const i = page.findIndex((c) => c.id === saved.id);
    if (i >= 0) page[i] = saved;
  }
  if (!editing) query(); // a new one: where it goes depends on the sort
  else render();
});
Customers.onDeleted(() => query());

query();
