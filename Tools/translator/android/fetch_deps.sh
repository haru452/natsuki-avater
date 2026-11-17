#!/bin/bash
# Downloads the sherpa-onnx AAR (not in git: 38 MB) into app/libs.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
V=2.0.1
F="$HERE/app/libs/sherpa-onnx-static-link-onnxruntime-$V.aar"
[ -f "$F" ] || curl -sSL -o "$F" "https://github.com/k2-fsa/sherpa-onnx/releases/download/v$V/sherpa-onnx-static-link-onnxruntime-$V.aar"
ls -la "$F"
