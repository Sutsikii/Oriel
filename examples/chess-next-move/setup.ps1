# Downloads what the example needs and does not keep in the repository:
# Stockfish (GPL-3.0), into engine/, and the piece sets of lichess
# (cburnett, merida: GPL-2.0+; kosal: CC BY-SA 4.0), into frontend/public/pieces/.
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

$engine = Join-Path $here 'engine/stockfish.exe'
if (-not (Test-Path $engine)) {
    $temp = Join-Path ([IO.Path]::GetTempPath()) "stockfish-$([guid]::NewGuid())"
    New-Item -ItemType Directory -Force $temp | Out-Null
    $zip = Join-Path $temp 'stockfish.zip'
    # The AVX2 build runs on CPUs since 2013; use stockfish-windows-x86-64.zip for older ones.
    Invoke-WebRequest 'https://github.com/official-stockfish/Stockfish/releases/download/sf_17.1/stockfish-windows-x86-64-avx2.zip' -OutFile $zip
    Expand-Archive $zip -DestinationPath $temp
    New-Item -ItemType Directory -Force (Split-Path $engine) | Out-Null
    Copy-Item (Get-ChildItem $temp -Recurse -Filter 'stockfish-*.exe' | Select-Object -First 1).FullName $engine
    Remove-Item -Recurse -Force $temp
    Write-Host "Stockfish: $engine"
}

foreach ($set in 'cburnett', 'merida', 'kosal') {
    $folder = Join-Path $here "frontend/public/pieces/$set"
    New-Item -ItemType Directory -Force $folder | Out-Null
    foreach ($piece in 'wK', 'wQ', 'wR', 'wB', 'wN', 'wP', 'bK', 'bQ', 'bR', 'bB', 'bN', 'bP') {
        $file = Join-Path $folder "$piece.svg"
        if (-not (Test-Path $file)) {
            Invoke-WebRequest "https://raw.githubusercontent.com/lichess-org/lila/master/public/piece/$set/$piece.svg" -OutFile $file
        }
    }
}
Write-Host 'Pieces: frontend/public/pieces'
