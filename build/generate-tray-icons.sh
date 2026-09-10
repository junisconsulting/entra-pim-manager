#!/usr/bin/env bash
#
# Regenerates the tray icons from their SVG sources.
#
# The SVGs are the source of truth; the .ico files next to them are build
# output committed to the repo so a Windows build needs no image tooling.
# Run this after editing any tray-icon-*.svg.
#
# Each icon is packed with every size the Windows shell asks for (16 px at
# 100 % DPI up to 48 px), because letting the shell downscale a single 32 px
# image visibly softens the key and the badge.
#
# Two variants per state, named for the taskbar they are drawn for: -onlight
# has a dark shield, -ondark a light one. TrayPopupController picks between
# them from the Windows setting that colours the taskbar.
#
# Requires: librsvg2-bin (rsvg-convert) and imagemagick (magick).
# rsvg-convert does the rendering — ImageMagick's own SVG renderer mishandles
# the <mask> the keyhole relies on; magick only assembles the .ico container.
set -euo pipefail

readonly STATES=(red grey amber green)
readonly VARIANTS=(onlight ondark)
readonly SIZES=(16 20 24 32 48)

for tool in rsvg-convert magick; do
    if ! command -v "$tool" >/dev/null; then
        echo "error: $tool not found — install with: sudo apt-get install -y librsvg2-bin imagemagick" >&2
        exit 1
    fi
done

assets="$(cd "$(dirname "${BASH_SOURCE[0]}")/../src/Entra-PIM-Manager.App.Avalonia/Assets" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

for state in "${STATES[@]}"; do
    for variant in "${VARIANTS[@]}"; do
        name="tray-icon-$state-$variant"
        svg="$assets/$name.svg"
        [[ -f "$svg" ]] || { echo "error: missing $svg" >&2; exit 1; }

        frames=()
        for size in "${SIZES[@]}"; do
            frame="$work/$name-$size.png"
            rsvg-convert -w "$size" -h "$size" "$svg" -o "$frame"
            frames+=("$frame")
        done

        magick "${frames[@]}" "$assets/$name.ico"
        echo "$name.ico  <- ${SIZES[*]}"
    done
done
