#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Build Meltype's Mozc C ABI bridge and OSS data set for Android arm64-v8a.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
mozc="${1:-$HOME/mozc-android}"
cache="${2:-}"
commit="$(tr -d '[:space:]' < "$here/MOZC_COMMIT")"
out="$root/src/Meltype.Android/jniLibs/arm64-v8a"
assets="$root/src/Meltype.Android/Assets"

if [[ ! -d "$mozc/.git" ]]; then
  mkdir -p "$mozc"
  git -C "$mozc" init -q
  git -C "$mozc" remote add origin https://github.com/google/mozc.git
  git -C "$mozc" fetch -q --depth 1 origin "$commit"
  git -C "$mozc" checkout -q FETCH_HEAD
  git -C "$mozc" submodule update -q --init --recursive --depth 1
fi

if [[ "$(git -C "$mozc" rev-parse HEAD)" != "$commit" ]]; then
  echo "Mozc checkout differs from MOZC_COMMIT; use a fresh source directory: $mozc" >&2
  exit 1
fi

src="$mozc/src"
cp "$here/android/meltype_mozc_android.cc" "$src/converter/"
if ! grep -q 'name = "meltype_mozc_android"' "$src/converter/BUILD.bazel"; then
  printf '\n%s\n' "$(cat "$here/android/CONVERTER_BUILD.fragment")" >> "$src/converter/BUILD.bazel"
fi
if ! grep -q 'name = "meltype_mozc.arm64"' "$src/android/jni/BUILD.bazel"; then
  printf '\n%s\n' "$(cat "$here/android/BUILD.fragment")" >> "$src/android/jni/BUILD.bazel"
fi

cd "$src"
python3 build_tools/update_deps.py

cache_arg=()
[[ -n "$cache" ]] && cache_arg+=("--disk_cache=$cache")

# The Android library loads the OSS dictionary from a regular file at runtime.
# Build that file for the host first; trying to embed it in the cross-built
# library pulls host-only data generators into the Android transition.
"${BAZEL:-bazel}" build //data_manager/oss:mozc.data --config oss_linux --config release_build "${cache_arg[@]}"
mkdir -p "$assets"
cp -f bazel-bin/data_manager/oss/mozc.data "$assets/mozc.data"

android_target="//android/jni:meltype_mozc.arm64"
"${BAZEL:-bazel}" build "$android_target" --config oss_android --config release_build "${cache_arg[@]}"

# cross_build_binary forwards the actual cc_binary output.  With linkshared=1
# Mozc intentionally keeps the Bazel target name (e.g. the official target is
# named `mozc`) and the Android packaging step renames that ELF to libmozc.so.
# Ask Bazel for the transitioned output path instead of assuming a lib*.so name.
artifact="$("${BAZEL:-bazel}" cquery "$android_target" \
  --config oss_android --config release_build "${cache_arg[@]}" \
  --output=files 2>/dev/null | awk 'NF { print; exit }')"
if [[ -z "$artifact" || ! -f "$artifact" ]]; then
  echo "Could not resolve the built Meltype Mozc shared-library artifact" >&2
  "${BAZEL:-bazel}" cquery "$android_target" \
    --config oss_android --config release_build "${cache_arg[@]}" \
    --output=files || true
  exit 1
fi

mkdir -p "$out"
cp -f "$artifact" "$out/libmeltype_mozc.so"
chmod u+w "$out/libmeltype_mozc.so"
echo "Created: $out/libmeltype_mozc.so"
echo "Created: $assets/mozc.data"
