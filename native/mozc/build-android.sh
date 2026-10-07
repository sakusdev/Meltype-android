#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Build Meltype's Mozc C ABI bridge for Android arm64-v8a.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
mozc="${1:-$HOME/mozc-android}"
cache="${2:-}"
commit="$(tr -d '[:space:]' < "$here/MOZC_COMMIT")"
out="$root/src/Meltype.Android/jniLibs/arm64-v8a"

if [[ ! -d "$mozc/.git" ]]; then
  mkdir -p "$mozc"
  git -C "$mozc" init -q
  git -C "$mozc" remote add origin https://github.com/google/mozc.git
  git -C "$mozc" fetch -q --depth 1 origin "$commit"
  git -C "$mozc" checkout -q FETCH_HEAD
  git -C "$mozc" submodule update -q --init --recursive --depth 1
fi

src="$mozc/src"
cp "$here/android/meltype_mozc_android.cc" "$src/android/jni/"
if ! grep -q 'name = "meltype_mozc"' "$src/android/jni/BUILD.bazel"; then
  printf '\n%s\n' "$(cat "$here/android/BUILD.fragment")" >> "$src/android/jni/BUILD.bazel"
fi

cd "$src"
# Populate third-party archives used by the Android build (including the NDK).
python3 build_tools/update_deps.py

options=(build //android/jni:meltype_mozc.arm64 --config oss_android --config release_build)
[[ -n "$cache" ]] && options+=("--disk_cache=$cache")
"${BAZEL:-bazel}" "${options[@]}"

artifact="$(find bazel-out -type f -name 'libmeltype_mozc.so' -print -quit)"
if [[ -z "$artifact" ]]; then
  echo "libmeltype_mozc.so was not produced" >&2
  exit 1
fi

mkdir -p "$out"
cp -f "$artifact" "$out/libmeltype_mozc.so"
chmod u+w "$out/libmeltype_mozc.so"
echo "Created: $out/libmeltype_mozc.so"
