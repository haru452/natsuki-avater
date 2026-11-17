#!/bin/bash
# Builds libtranslator_llm.so (llama.cpp, arm64 + dotprod + i8mm) into Assets/Translator/Plugins/Android/libs/arm64-v8a.
# Needs: Unity 2022.3.62f3 Android NDK, cmake + ninja on PATH (pip install cmake ninja), git.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
LLAMA_COMMIT=9581154
CACHE="${LLAMA_CACHE:-$HERE/.llama.cpp}"
NDK="${NDK:-/c/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Data/PlaybackEngines/AndroidPlayer/NDK}"
if [ ! -d "$CACHE/.git" ]; then
  git clone -q -c core.longpaths=true https://github.com/ggml-org/llama.cpp "$CACHE"
fi
if [ "$(git -C "$CACHE" rev-parse --short=7 HEAD)" != "$LLAMA_COMMIT" ]; then
  git -C "$CACHE" -c core.longpaths=true checkout -q "$LLAMA_COMMIT"
fi
cmake -S "$HERE" -B "$HERE/.build" -G Ninja -DLLAMA_DIR="$CACHE" \
  -DCMAKE_TOOLCHAIN_FILE="$NDK/build/cmake/android.toolchain.cmake" -DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-28 \
  -DCMAKE_BUILD_TYPE=Release -DGGML_CPU_ARM_ARCH=armv8.2-a+dotprod+i8mm -DLLAMA_OPENSSL=OFF
cmake --build "$HERE/.build" --target translator_llm -j 12
OUT="$HERE/../Assets/Translator/Plugins/Android/libs/arm64-v8a"
mkdir -p "$OUT"
cp "$HERE/.build/libtranslator_llm.so" "$OUT/"
"$NDK"/toolchains/llvm/prebuilt/*/bin/llvm-strip.exe "$OUT/libtranslator_llm.so" 2>/dev/null || true
ls -la "$OUT"
