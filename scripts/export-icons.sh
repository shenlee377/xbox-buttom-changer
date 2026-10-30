#!/usr/bin/env bash
# Render new branding assets from the versioned vector source. Requires Inkscape + ImageMagick 7.
set -euo pipefail
cd "$(dirname "$0")/.."
command -v inkscape >/dev/null
command -v magick >/dev/null
mkdir -p src/GuideDeck.Desktop/Assets src/GuideDeck.GameBar/Assets
inkscape assets/branding/GuideDeck.svg --export-type=png --export-width=1024 --export-filename=assets/branding/GuideDeck.png
magick -define png:color-type=6 assets/branding/GuideDeck.png -define icon:auto-resize=256,128,64,48,32,24,20,16 src/GuideDeck.Desktop/Assets/GuideDeck.ico
magick -define png:color-type=6 assets/branding/GuideDeck.png -resize 128x128 src/GuideDeck.Desktop/Assets/GuideDeck.png
for size in 44 150; do
  magick -define png:color-type=6 assets/branding/GuideDeck.png -resize "${size}x${size}" "src/GuideDeck.GameBar/Assets/Square${size}x${size}Logo.png"
  for scale in 200 400; do
    pixels=$((size * scale / 100))
    magick -define png:color-type=6 assets/branding/GuideDeck.png -resize "${pixels}x${pixels}" "src/GuideDeck.GameBar/Assets/Square${size}x${size}Logo.scale-${scale}.png"
  done
done
for size in 16 24 32 48 256; do
  magick -define png:color-type=6 assets/branding/GuideDeck.png -resize "${size}x${size}" "src/GuideDeck.GameBar/Assets/Square44x44Logo.targetsize-${size}_altform-unplated.png"
done
magick -define png:color-type=6 assets/branding/GuideDeck.png -resize 50x50 src/GuideDeck.GameBar/Assets/StoreLogo.png
for scale in 200 400; do
  pixels=$((50 * scale / 100))
  magick -define png:color-type=6 assets/branding/GuideDeck.png -resize "${pixels}x${pixels}" "src/GuideDeck.GameBar/Assets/StoreLogo.scale-${scale}.png"
done
for scale in 100 200 400; do
  icon=$((180 * scale / 100)); width=$((620 * scale / 100)); height=$((300 * scale / 100))
  suffix=".scale-${scale}"; if [[ "$scale" == 100 ]]; then suffix=""; fi
  magick -define png:color-type=6 assets/branding/GuideDeck.png -resize "${icon}x${icon}" -gravity center -background none -extent "${width}x${height}" "src/GuideDeck.GameBar/Assets/SplashScreen${suffix}.png"
done
