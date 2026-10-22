# Chess Next Move

A local take on [chessnextmove.com](https://www.chessnextmove.com/): set up any
position by dragging pieces, and Stockfish suggests the best move as you go. The page is TypeScript; the engine is the C# service `Engine`
(`Engine.cs`), which runs Stockfish as a process and speaks UCI to it.

- Board editor: drag pieces from the palettes or around the board, drop one
  off the board to remove it, or select a piece or the eraser (×) and click
  squares. Right-click empties a square.
- Side to move, castling rights, FEN (edit it, copy it; the URL carries it as
  `?fen=`), Rotate, Start position, Kings only, Clear.
- Each new position is searched at once; "Analyse as you edit" turns that
  off, and **Play** then searches (Space). **Move** plays the suggested move (M),
  **← Back** and **Move →** step through the positions (arrow keys).
- Up to three lines, drawn as numbered arrows and written in SAN; an
  evaluation bar and each side's score.
- Settings: depth (12, 18, 24 or custom), time (2 s, 5 s, unlimited), lines,
  piece set (cburnett, merida, kosal), theme, coordinates.

## Run it

```powershell
./setup.ps1                 # downloads Stockfish 17.1 and the piece sets
cd frontend; npm install; npm run build; cd ..
dotnet run
```

`npm run dev` in `frontend` runs it with Vite: the page reloads as you edit it.
`CHESS_ENGINE` points the app at another UCI engine.

`dotnet publish -c Release -r win-x64` gives a native executable of 2.6 MB,
with Stockfish next to it in `engine/` (its neural networks are built into
it), then the installer: `bin/installer/Chess-Next-Move-Setup-1.0.0.exe`, of
about 64 MB. It needs [Inno Setup 6](https://jrsoftware.org/isinfo.php) and,
for NativeAOT, the C++ build tools of Visual Studio.

Stockfish (GPL-3.0) and the lichess piece sets (GPL-2.0+, CC BY-SA 4.0 for
kosal) are downloaded, not kept in the repository.
