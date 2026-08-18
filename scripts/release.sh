#!/usr/bin/env bash
set -euo pipefail

# Builds framework-dependent, portable release archives for
# Arbor.Symbols.Server and Arbor.Symbols.ConsoleClient.
#
# "Framework-dependent" means no runtime identifier (RID) is baked in and
# the .NET runtime is NOT bundled: the archive runs on any OS/CPU that has
# the matching .NET runtime installed (`dotnet <Project>.dll`), instead of
# being tied to one machine's architecture.
#
# Output: artifacts/release/<Project>-<version>-portable.{zip|tar.gz} (+ checksum)
#
# Usage: scripts/release.sh [version]
#   version   Optional label embedded in the archive file name.
#             Defaults to the current short git commit hash.

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

solution="Arbor.Symbols.slnx"
configuration="Release"
artifacts_dir="$repo_root/artifacts"
publish_root="$artifacts_dir/publish"
release_dir="$artifacts_dir/release"

version="${1:-$(git rev-parse --short HEAD 2>/dev/null || echo "local")}"

projects=(
  "src/Arbor.Symbols.Server/Arbor.Symbols.Server.csproj"
  "src/Arbor.Symbols.ConsoleClient/Arbor.Symbols.ConsoleClient.csproj"
)

if ! command -v dotnet >/dev/null 2>&1; then
  echo "error: dotnet SDK not found on PATH" >&2
  exit 1
fi

echo "==> Restoring and building ($configuration)"
dotnet build "$solution" --configuration "$configuration"

rm -rf "$release_dir"
mkdir -p "$release_dir"

checksum() {
  local file="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    (cd "$(dirname "$file")" && sha256sum "$(basename "$file")")
  else
    (cd "$(dirname "$file")" && shasum -a 256 "$(basename "$file")")
  fi
}

for project in "${projects[@]}"; do
  project_name="$(basename "$project" .csproj)"
  publish_output="$publish_root/$project_name"
  echo "==> Publishing $project_name (framework-dependent, no RID)"

  rm -rf "$publish_output"

  # An explicit --output bypasses the artifacts-output layout's pivoted
  # publish directory, so the archive step below always packages exactly
  # what was just published, deterministically.
  dotnet publish "$project" \
    --configuration "$configuration" \
    --no-self-contained \
    --no-restore \
    --output "$publish_output"

  cat > "$publish_output/RUN.txt" <<EOF
$project_name - portable release ($version)

Requirements: .NET 10 runtime (no SDK needed) installed on the target machine.
https://dotnet.microsoft.com/download/dotnet/10.0

Run:
  dotnet $project_name.dll

$project_name defaults to plain HTTP. HTTPS is optional; see the README for
how to enable it (add a Kestrel "Https" endpoint in appsettings.Production.json).
EOF

  archive_stem="${project_name}-${version}-portable"

  if command -v zip >/dev/null 2>&1; then
    archive_path="$release_dir/${archive_stem}.zip"
    echo "==> Packaging $archive_path"
    (cd "$publish_output" && zip -r -q "$archive_path" .)
  else
    archive_path="$release_dir/${archive_stem}.tar.gz"
    echo "==> Packaging $archive_path"
    tar -C "$publish_output" -czf "$archive_path" .
  fi

  checksum "$archive_path" > "$release_dir/${archive_stem}.sha256"
done

echo "==> Release artifacts:"
ls -la "$release_dir"
