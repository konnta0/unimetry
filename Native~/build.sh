#!/bin/sh
set -eu
root=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
src="$root/Native~/unimetry_crash.c"
mkdir -p "$root/Plugins/macOS" "$root/Plugins/iOS"

clang -dynamiclib -O2 -Wall -Wextra -arch arm64 -arch x86_64 \
  -o "$root/Plugins/macOS/libunimetry_crash.dylib" "$src"

sdk=$(xcrun --sdk iphoneos --show-sdk-path)
xcrun --sdk iphoneos clang -c -O2 -Wall -Wextra -arch arm64 -isysroot "$sdk" \
  -o "$root/Native~/unimetry_crash_ios.o" "$src"
libtool -static -o "$root/Plugins/iOS/libunimetry_crash.a" "$root/Native~/unimetry_crash_ios.o"
rm -f "$root/Native~/unimetry_crash_ios.o"
