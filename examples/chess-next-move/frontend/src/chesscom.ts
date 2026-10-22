// A player's finished games, from chess.com's published data
// (https://www.chess.com/news/view/published-data-api). The archives hold
// games once they are over, never one being played.

export interface Side {
  username: string;
  rating: number;
  /** "win", "checkmated", "resigned", "timeout", "agreed", "repetition"… */
  result: string;
}

export interface ArchivedGame {
  url: string;
  pgn: string;
  white: Side;
  black: Side;
  /** "bullet", "blitz", "rapid", "daily". */
  timeClass: string;
  /** When the game ended. */
  end: Date;
}

interface Archive {
  games: {
    url: string;
    pgn?: string;
    rules: string;
    time_class: string;
    end_time: number;
    white: Side;
    black: Side;
  }[];
}

const api = "https://api.chess.com/pub/player";

async function get<T>(url: string, signal?: AbortSignal, missing = "chess.com has no player of that name."): Promise<T> {
  const response = await fetch(url, { signal });
  if (response.status === 404) throw new Error(missing);
  if (!response.ok) throw new Error(`chess.com answered ${response.status} ${response.statusText}.`);
  return (await response.json()) as T;
}

/** The player's last `count` games of standard chess, the latest first, from the last few months. */
export async function recentGames(username: string, count = 20, signal?: AbortSignal): Promise<ArchivedGame[]> {
  const name = encodeURIComponent(username.trim().toLowerCase());
  if (!name) throw new Error("Enter a chess.com username.");
  const { archives } = await get<{ archives: string[] }>(`${api}/${name}/games/archives`, signal);
  const games: ArchivedGame[] = [];
  let failed: unknown = null;
  // An archive is a month; a player with few games needs more than the last one.
  for (const month of archives.slice(-3).reverse()) {
    let archive: Archive;
    try {
      archive = await get<Archive>(month, signal, "chess.com could not give this month's games.");
    } catch (err) {
      // chess.com sometimes fails on a month, the current one of busy players especially.
      if (signal?.aborted) throw err;
      failed = err;
      continue;
    }
    for (const game of archive.games) {
      if (game.rules !== "chess" || !game.pgn) continue;
      games.push({
        url: game.url,
        pgn: game.pgn,
        white: game.white,
        black: game.black,
        timeClass: game.time_class,
        end: new Date(game.end_time * 1000),
      });
    }
    if (games.length >= count) break;
  }
  if (games.length === 0 && failed) throw failed;
  return games.sort((a, b) => b.end.getTime() - a.end.getTime()).slice(0, count);
}

/** "1-0", "0-1" or "½-½". */
export const result = (game: ArchivedGame) =>
  game.white.result === "win" ? "1-0" : game.black.result === "win" ? "0-1" : "½-½";
