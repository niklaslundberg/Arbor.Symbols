#!/bin/sh
# Installs a pre-commit hook that runs the full test suite before every commit.
# Run once after cloning: ./scripts/install-hooks.sh

set -e

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
HOOKS_DIR="$REPO_ROOT/.git/hooks"
HOOK_FILE="$HOOKS_DIR/pre-commit"

mkdir -p "$HOOKS_DIR"

cat > "$HOOK_FILE" << 'HOOK'
#!/bin/sh
set -e
cd "$(git rev-parse --show-toplevel)"
dotnet test Arbor.Symbols.slnx
HOOK

chmod +x "$HOOK_FILE"
echo "Pre-commit hook installed at $HOOK_FILE"
