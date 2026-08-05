#!/bin/zsh
# KimiWebBox macOS installer:
#  1. builds Resources/kimiweb.icns from icon-src/*.png (needs Xcode's iconutil, built-in on macOS)
#  2. copies KimiWebBox.app to ~/Applications
#  3. optionally registers a login item
#
# usage: ./install.sh [--login]

set -e
cd "$(dirname "$0")"
APP="KimiWebBox.app"
SRC="icon-src"
ICNS="$APP/Contents/Resources/kimiweb.icns"

mkdir -p "$APP/Contents/Resources"

if [ -d "$SRC" ]; then
  TMP=$(mktemp -d)/kimiweb.iconset
  mkdir -p "$TMP"
  cpif() { [ -f "$SRC/KimiWeb主图标_$1.png" ] && cp "$SRC/KimiWeb主图标_$1.png" "$TMP/$2"; }
  cpif 16  icon_16x16.png
  cpif 32  icon_16x16@2x.png
  cpif 32  icon_32x32.png
  cpif 64  icon_32x32@2x.png
  cpif 128 icon_128x128.png
  cpif 256 icon_128x128@2x.png
  cpif 256 icon_256x256.png
  cpif 256 icon_256x256@2x.png
  cpif 256 icon_512x512.png
  iconutil -c icns "$TMP" -o "$ICNS"
  echo "icon built: $ICNS"
else
  echo "warning: $SRC not found, app will use the default icon"
fi

chmod +x "$APP/Contents/MacOS/KimiWebBox"
mkdir -p ~/Applications
rm -rf ~/Applications/KimiWebBox.app
cp -R "$APP" ~/Applications/
xattr -dr com.apple.quarantine ~/Applications/KimiWebBox.app 2>/dev/null || true
echo "installed to ~/Applications/KimiWebBox.app"

if [ "$1" = "--login" ]; then
  osascript -e 'tell application "System Events" to make login item at end with properties {path:"~/Applications/KimiWebBox.app", hidden:false}' >/dev/null
  echo "login item registered"
fi

echo "done. Launch from Launchpad or: open ~/Applications/KimiWebBox.app"
