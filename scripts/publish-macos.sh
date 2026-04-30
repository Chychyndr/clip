#!/usr/bin/env bash
set -euo pipefail

configuration="${CONFIGURATION:-Release}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/src/Clip.App/Clip.App.csproj"
nuget_config="$root/NuGet.Config"

publish_runtime() {
  local runtime="$1"
  local artifact="$2"
  local output="$root/artifacts/$artifact"

  rm -rf "$output"
  mkdir -p "$output"

  dotnet restore "$project" --configfile "$nuget_config"
  dotnet publish "$project" \
    -c "$configuration" \
    -r "$runtime" \
    --self-contained true \
    -o "$output"

  (cd "$root/artifacts" && zip -qr "$artifact.zip" "$artifact")
  echo "Published $runtime to $output"
}

publish_runtime "osx-arm64" "Clip-macos-arm64"
publish_runtime "osx-x64" "Clip-macos-x64"
