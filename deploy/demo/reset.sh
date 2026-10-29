#!/usr/bin/env bash
# Resets the demo instance: throw away the database and files, start again with the example data.
# Run nightly from cron, e.g.:  30 3 * * *  /home/azureuser/Elocaro/deploy/demo/reset.sh >> /var/log/elocaro-demo-reset.log 2>&1
set -euo pipefail
cd "$(dirname "$0")/.."
DC="docker compose -f docker-compose.yml -f docker-compose.demo.yml --env-file .env"
$DC down
# Only the data volumes; the certificate volumes (caddy-*) stay, so Let's Encrypt is not asked again.
docker volume rm elocaro_postgres elocaro_files
$DC up -d
echo "$(date -Is) Demo zurückgesetzt"
