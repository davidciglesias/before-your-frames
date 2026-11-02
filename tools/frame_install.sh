#!/bin/sh
# Install Before Your Frames for playing Before Your Eyes on the Steam Frame itself (no PC).
#   sh frame_install.sh [game folder]
# Run it in the Frame's desktop terminal (Konsole) with the game installed and not running. It downloads, from their
# official GitHub releases and checked against known hashes: BepInEx 5 (the mod loader), UUVR 0.3.1 (the VR mod, Unity
# Mono legacy build) and this mod, and puts them in the game folder. Nothing outside the game folder is changed,
# except frameeyeosc's target if you pass --eyes-local. Re-running it updates the mod and keeps your settings.
set -eu

BEPINEX_URL=https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.6/BepInEx_win_x64_5.4.23.6.zip
BEPINEX_SHA=6c57a714a892efb4e9e1fef63d608a3391f30ec984031c465404e4ee381a0755
UUVR_URL=https://github.com/Raicuparta/uuvr/releases/download/v0.3.1/uuvr-mono-legacy.zip
UUVR_SHA=e23b18bed81ef1964087c434299265783567ca8ae58037488d5589590f9db680
MOD_URL=${BYE_ZIP:-https://github.com/davidciglesias/before-your-frames/releases/latest/download/BYEFrameBlink.zip}

EYES_LOCAL=0
GAME=""
for a in "$@"; do
    case "$a" in
        --eyes-local) EYES_LOCAL=1 ;;
        *) GAME="$a" ;;
    esac
done

# Find the game in any Steam library.
if [ -z "$GAME" ]; then
    STEAM="$HOME/.local/share/Steam"
    for lib in "$STEAM" $(grep -o '"path"[[:space:]]*"[^"]*"' "$STEAM/steamapps/libraryfolders.vdf" 2>/dev/null | sed 's/.*"\([^"]*\)"$/\1/'); do
        if [ -f "$lib/steamapps/common/Before Your Eyes/Before Your Eyes.exe" ]; then GAME="$lib/steamapps/common/Before Your Eyes"; break; fi
    done
fi
if [ -z "$GAME" ] || [ ! -f "$GAME/Before Your Eyes.exe" ]; then
    echo "Before Your Eyes not found. Install it in Steam first, or pass its folder: sh frame_install.sh \"/path/to/Before Your Eyes\"" >&2
    exit 1
fi
if pgrep -f "[B]efore Your Eyes.exe" >/dev/null; then echo "The game is running; quit it first." >&2; exit 1; fi
echo "Game folder: $GAME"

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

fetch() { # url, file, sha256 (empty = don't check)
    case "$1" in
        http*) curl -fL --retry 3 -o "$2" "$1" ;;
        *) cp "$1" "$2" ;;
    esac
    if [ -n "$3" ] && [ "$(sha256sum "$2" | cut -d' ' -f1)" != "$3" ]; then echo "Checksum mismatch for $1" >&2; exit 1; fi
}

echo "Downloading BepInEx, UUVR and Before Your Frames..."
fetch "$BEPINEX_URL" "$TMP/bepinex.zip" "$BEPINEX_SHA"
fetch "$UUVR_URL" "$TMP/uuvr.zip" "$UUVR_SHA"
fetch "$MOD_URL" "$TMP/mod.zip" ""

B="$GAME/BepInEx"
unzip -qo "$TMP/bepinex.zip" -d "$GAME"
mkdir -p "$TMP/uuvr" "$B/patchers/uuvr-mono-legacy" "$B/plugins/uuvr-mono-legacy" "$B/plugins/BYEFrameBlink" "$B/config"
unzip -qo "$TMP/uuvr.zip" -d "$TMP/uuvr"
cp -a "$TMP/uuvr/patchers/." "$B/patchers/uuvr-mono-legacy/"
cp -a "$TMP/uuvr/plugins/." "$B/plugins/uuvr-mono-legacy/"
unzip -qo "$TMP/mod.zip" BYEFrameBlink.dll -d "$B/plugins/BYEFrameBlink"

# Settings that differ from the defaults (the rest are written at the first launch):
# UUVR draws the game's UI on its panel (CanvasRedirect: the default mirror shows a black frame), and BepInEx's
# console window stays closed (under Proton it would be a stray window).
set_cfg() { # file, section, key, value
    f="$B/config/$1"
    touch "$f"
    # Only the key inside its own section (BepInEx.cfg has an "Enabled" in several); add the section if missing.
    awk -v s="[$2]" -v k="$3" -v v="$4" '
        { sub(/\r$/, "") }
        /^\[/ { if (in_s && !done) { print k " = " v; done = 1 } in_s = ($0 == s) }
        in_s && index($0, k) == 1 && $0 ~ ("^" k " *=") { print k " = " v; done = 1; next }
        { print }
        END { if (!done) { if (!in_s) print "\n" s; print k " = " v } }
    ' "$f" > "$f.tmp" && mv "$f.tmp" "$f"
}
set_cfg raicuparta.uuvr-legacy.cfg UI "UI Patch Mode" CanvasRedirect
set_cfg BepInEx.cfg Logging.Console Enabled false

if [ "$EYES_LOCAL" = 1 ]; then
    C="$HOME/.config/frameeyeosc/config.json"
    if [ -f "$C" ]; then
        [ -f "$C.before-bye" ] || cp "$C" "$C.before-bye"
        sed -i 's|"host": *"[^"]*"|"host": "127.0.0.1"|' "$C"
        systemctl --user restart frameeyeosc.service 2>/dev/null || true
        echo "frameeyeosc now sends to this headset (127.0.0.1). Your previous config is in $C.before-bye."
    else
        echo "frameeyeosc isn't installed yet (no $C); install it, then run this again with --eyes-local." >&2
    fi
fi

echo
echo "Done. One step left in Steam: Before Your Eyes > Properties > Launch Options:"
echo '    WINEDLLOVERRIDES="winhttp=n,b" %command%'
