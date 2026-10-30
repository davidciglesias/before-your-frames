# Shared helpers for the dev tools. Dot-source: . "$PSScriptRoot\_common.ps1"
function Get-BepInExDir {
    # BepInEx folder Rai Pal installed for the game: the one whose plugins folder holds BYEFrameBlink (override with $env:BYE_BEPINEX).
    if ($env:BYE_BEPINEX) { return $env:BYE_BEPINEX }
    $mods = Join-Path $env:APPDATA 'raicuparta\rai-pal\data\installed-mods'
    $hit = Get-ChildItem $mods -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'bepinex\BepInEx' } |
        Where-Object { Test-Path (Join-Path $_ 'plugins\BYEFrameBlink') } | Select-Object -First 1
    if (-not $hit) { throw "Could not find the game's BepInEx folder under $mods. Set `$env:BYE_BEPINEX to it." }
    $hit
}
