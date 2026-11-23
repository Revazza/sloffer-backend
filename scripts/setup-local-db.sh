#!/usr/bin/env bash
# One-time local setup (Git Bash):
#   - creates the 'sloffer' PostgreSQL user and the 'sloffer' + 'sloffer_test' databases
#   - stores both connection strings in user-secrets (id: sloffer-backend)
# Asks for the 'postgres' superuser password once. The 'sloffer' user gets a random password that
# only lives in user-secrets, never in the repo. Safe to re-run (resets that password).
#
# Usage:  scripts/setup-local-db.sh
# Override with env vars: PSQL, DB_HOST, DB_PORT
set -euo pipefail

PSQL="${PSQL:-/c/Program Files/PostgreSQL/17/bin/psql.exe}"
DB_HOST="${DB_HOST:-localhost}"
DB_PORT="${DB_PORT:-5432}"

if [[ ! -x "$PSQL" ]]; then
  echo "psql not found at '$PSQL'. Run with PSQL=/path/to/psql.exe $0" >&2
  exit 1
fi

if command -v dotnet >/dev/null 2>&1; then
  DOTNET=dotnet
elif [[ -x "$HOME/.dotnet/dotnet.exe" ]]; then
  DOTNET="$HOME/.dotnet/dotnet.exe"
else
  echo "dotnet not found on PATH or in ~/.dotnet" >&2
  exit 1
fi

# Random hex password: nothing that needs quoting in SQL or connection strings.
PASSWORD="$(od -An -tx1 -N24 /dev/urandom | tr -d ' \n')"

read -r -s -p "Password for the 'postgres' superuser: " PG_SUPER_PASSWORD
echo

PGPASSWORD="$PG_SUPER_PASSWORD" "$PSQL" -h "$DB_HOST" -p "$DB_PORT" -U postgres -d postgres \
  -v ON_ERROR_STOP=1 -q -f - <<SQL
DO \$\$
BEGIN
  IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'sloffer') THEN
    ALTER ROLE sloffer WITH LOGIN CREATEDB PASSWORD '$PASSWORD';
  ELSE
    CREATE ROLE sloffer WITH LOGIN CREATEDB PASSWORD '$PASSWORD';
  END IF;
END
\$\$;
SELECT 'CREATE DATABASE sloffer OWNER sloffer'
  WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'sloffer')\gexec
SELECT 'CREATE DATABASE sloffer_test OWNER sloffer'
  WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'sloffer_test')\gexec
-- Databases that already existed (e.g. created by hand as postgres) must belong to sloffer too,
-- otherwise migrations fail with "permission denied for schema public".
ALTER DATABASE sloffer OWNER TO sloffer;
ALTER DATABASE sloffer_test OWNER TO sloffer;
SQL
unset PG_SUPER_PASSWORD

BASE="Host=$DB_HOST;Port=$DB_PORT;Username=sloffer;Password=$PASSWORD"
"$DOTNET" user-secrets set --id sloffer-backend "ConnectionStrings:Sloffer" "$BASE;Database=sloffer" >/dev/null
"$DOTNET" user-secrets set --id sloffer-backend "ConnectionStrings:SlofferTest" "$BASE;Database=sloffer_test" >/dev/null

echo "Done: user 'sloffer', databases 'sloffer' + 'sloffer_test', connection strings saved to user-secrets (sloffer-backend)."
echo "Next: dotnet ef database update --project src/Sloffer.Data"
