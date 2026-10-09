#!/usr/bin/env bash
# Usage: SQLSERVER=localhost SQLUSER=sa SQLPASS='...' ./deploy.sh   (requires sqlcmd)
set -euo pipefail
cd "$(dirname "$0")"
for f in 0*.sql; do echo "Running $f..."; sqlcmd -S "${SQLSERVER:-localhost}" -U "${SQLUSER:?}" -P "${SQLPASS:?}" -C -b -i "$f"; done
echo "Database deployed."
