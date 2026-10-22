# Chess Next Move

A local take on [chessnextmove.com](https://www.chessnextmove.com/): set up any
position by dragging pieces, or load a finished game, and Stockfish suggests
the best move as you go. The page is TypeScript; the engine is the C# service `Engine`
(`Engine.cs`), which runs Stockfish as a process and speaks UCI to it.

- Board editor: drag pieces from the palettes or around the board. A legal
  move is played: the turn passes, and the engine shows the other side's
  best moves. Any other drag edits the board. Drop a piece off the board to
  remove it, or select a piece or the eraser (×) and click
  squares. Right-click empties a square.
- Side to move, castling rights, FEN (edit it, copy it; the URL carries it as
  `?fen=`), Rotate, Start position, Kings only, Clear.
- Each new position is searched at once; "Analyse as you edit" turns that
  off, and **Play** then searches (Space). **Move** plays the suggested move (M),
  **← Back** and **Move →** step through the positions (arrow keys).
- Up to three lines, drawn as numbered arrows and written in SAN, with the
  expected reply to the best one as a dashed arrow; an evaluation bar and
  each side's score.
- Games: enter a chess.com username for the player's last 20 finished games,
  from chess.com's [published data](https://www.chess.com/news/view/published-data-api)
  (games appear there once they are over), or paste a PGN. The moves are
  listed; click one, or use the arrow keys, Home and End, to go through the
  game. Moves played on the board start a variation; clicking a move of the
  list goes back to the game.
- Review: Stockfish looks at each position of the game (depth 16 at most, 3 s
  each) and judges each move by the winning chances it gave away, as lichess
  does: inaccuracies (?!), mistakes (?) and blunders (??), with the move it
  preferred. A graph shows White's chances through the game, and each side gets
  an accuracy. The game and its review are kept until it is closed.
- 3D: the **3D** button shows the board with three.js, loaded only then: the
  same position, arrows and highlights, pieces turned on a lathe, and moves
  that slide (knights jump). Drag a piece to move it, or drop pieces from the
  palettes, as in 2D; drag elsewhere to turn the camera, right-drag to pan,
  and scroll to zoom. Rotate turns the board.
- Settings: depth (12, 18, 24 or custom), time (2 s, 5 s, unlimited), lines,
  piece set (cburnett, merida, kosal), theme, coordinates.

## Run it

```powershell
./setup.ps1                 # downloads Stockfish 17.1 and the piece sets
cd frontend; npm install; npm run build; cd ..
dotnet run
```

`npm run dev` in `frontend` runs it with Vite: the page reloads as you edit it.
`npm test` there tests reading games, judging moves and placing the 3D board's
squares, without the engine or a window.
`CHESS_ENGINE` points the app at another UCI engine.

`dotnet publish -c Release -r win-x64` gives a native executable of 2.6 MB,
with Stockfish next to it in `engine/` (its neural networks are built into
it), then the installer: `bin/installer/Chess-Next-Move-Setup-1.0.0.exe`, of
about 64 MB. It needs [Inno Setup 6](https://jrsoftware.org/isinfo.php) and,
for NativeAOT, the C++ build tools of Visual Studio.

Stockfish (GPL-3.0) and the lichess piece sets (GPL-2.0+, CC BY-SA 4.0 for
kosal) are downloaded, not kept in the repository.
