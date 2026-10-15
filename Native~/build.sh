#!/bin/sh
set -eu
root=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
src="$root/Native~/unimetry_crash.c"
mkdir -p "$root/Plugins/macOS" "$root/Plugins/iOS" \
  "$root/Plugins/Android/libs/arm64-v8a" \
  "$root/Plugins/Android/libs/armeabi-v7a" \
  "$root/Plugins/Android/libs/x86_64"

clang -dynamiclib -O2 -Wall -Wextra -arch arm64 -arch x86_64 \
  -o "$root/Plugins/macOS/libunimetry_crash.dylib" "$src"

sdk=$(xcrun --sdk iphoneos --show-sdk-path)
xcrun --sdk iphoneos clang -c -O2 -Wall -Wextra -arch arm64 -isysroot "$sdk" \
  -o "$root/Native~/unimetry_crash_ios.o" "$src"
libtool -static -o "$root/Plugins/iOS/libunimetry_crash.a" "$root/Native~/unimetry_crash_ios.o"
rm -f "$root/Native~/unimetry_crash_ios.o"

ndk=""
if [ -n "${ANDROID_NDK_HOME:-}" ] && [ -d "${ANDROID_NDK_HOME}" ]; then
  ndk="${ANDROID_NDK_HOME}"
elif [ -n "${ANDROID_NDK_ROOT:-}" ] && [ -d "${ANDROID_NDK_ROOT}" ]; then
  ndk="${ANDROID_NDK_ROOT}"
else
  sdk_root="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-$HOME/Library/Android/sdk}}"
  if [ -d "${sdk_root}/ndk" ]; then
    ndk=$(find "${sdk_root}/ndk" -mindepth 1 -maxdepth 1 -type d | sort | tail -n 1)
  fi
fi

if [ -z "${ndk}" ] || [ ! -d "${ndk}" ]; then
  unity_ndk=""
  unity_2021=""
  for candidate in /Applications/Unity/Hub/Editor/*/PlaybackEngines/AndroidPlayer/NDK; do
    [ -d "${candidate}/toolchains/llvm/prebuilt" ] || continue
    case "${candidate}" in
      */2021.3.*/PlaybackEngines/AndroidPlayer/NDK)
        unity_2021="${candidate}"
        ;;
    esac
    unity_ndk="${candidate}"
  done
  if [ -n "${unity_2021}" ]; then
    ndk="${unity_2021}"
  else
    ndk="${unity_ndk}"
  fi
fi

if [ -z "${ndk}" ] || [ ! -d "${ndk}" ]; then
  echo "Android NDK not found; skipped libunimetry_crash.so. Set ANDROID_NDK_HOME and re-run." >&2
  exit 0
fi

prebuilt=$(find "${ndk}/toolchains/llvm/prebuilt" -mindepth 1 -maxdepth 1 -type d | head -n 1)
clang_bin="${prebuilt}/bin/clang"
if [ ! -x "${clang_bin}" ]; then
  echo "Android NDK clang not found under ${ndk}." >&2
  exit 1
fi

api=22
"${clang_bin}" --target="aarch64-linux-android${api}" -shared -fPIC -O2 -Wall -Wextra \
  -o "$root/Plugins/Android/libs/arm64-v8a/libunimetry_crash.so" "$src"
"${clang_bin}" --target="armv7a-linux-androideabi${api}" -shared -fPIC -O2 -Wall -Wextra \
  -o "$root/Plugins/Android/libs/armeabi-v7a/libunimetry_crash.so" "$src"
"${clang_bin}" --target="x86_64-linux-android${api}" -shared -fPIC -O2 -Wall -Wextra \
  -o "$root/Plugins/Android/libs/x86_64/libunimetry_crash.so" "$src"
