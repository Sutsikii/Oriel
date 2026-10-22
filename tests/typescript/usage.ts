// Type-level tests of the generated client: this file must compile under
// `tsc --strict`, and each @ts-expect-error must be an error.
import { CallError, Decks, Greeter, type Deck, type Slide, type Theme } from "./generated/client.ts";

export async function usage(): Promise<void> {
  const greeting: string = await Greeter.greet("Ada");

  const deck: Deck | null = await Decks.open("/decks/demo.json");
  if (deck) {
    const first: Slide | undefined = deck.slides[0];
    const rating: number | null = first?.rating ?? null;
    const note: string | null | undefined = deck.meta["note"];
    await Decks.save("/decks/demo.json", deck);
    void [rating, note];
  }

  // Optional parameters may be left out, or given.
  const all: (string | null)[] = await Decks.search("query");
  await Decks.search("query", 5, "dark");
  await Decks.search("query", undefined, null);

  try {
    await Greeter.delete("x", true);
  } catch (err) {
    if (err instanceof CallError) {
      const method: string = err.method;
      // The code tells the type of the data.
      if (err.code === "conflict") {
        const version: number = err.data.version;
        void version;
      } else {
        const none: null = err.data;
        void none;
      }
      void method;
    }
  }
  const stop: () => void = Decks.onChanged((changed: Deck) => void changed.title);
  stop();
  void [greeting, all];
}

export function errors(deck: Deck, err: CallError): void {
  // @ts-expect-error the data is not known before the code is checked
  void err.data.version;
  // @ts-expect-error codes are those of the app's errors
  if (err.code === "nope") void err;
  // @ts-expect-error an enum is a union of its names
  const theme: Theme = "Dark";
  // @ts-expect-error a property without a setter is read-only
  deck.slideCount = 3;
  // @ts-expect-error arguments are typed
  void Greeter.greet(42);
  // @ts-expect-error required parameters cannot be left out
  void Decks.open();
  // @ts-expect-error what Oriel passes (a CancellationToken) is not a parameter
  void Decks.save("p", deck, undefined);
  // @ts-expect-error listeners get the payload's type
  Decks.onChanged((n: number) => void n);
  void theme;
}
