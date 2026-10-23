#!/usr/bin/env bash
set -euo pipefail
image="${1:-planner-web:verify}"
container="planner-web-verify-$$"
volume="planner-web-data-$$"
cleanup() {
  docker logs "$container" || true
  docker rm -f "$container" >/dev/null 2>&1 || true
  docker volume rm "$volume" >/dev/null 2>&1 || true
}
trap cleanup EXIT
docker volume create "$volume" >/dev/null
start() {
  docker run -d --name "$container" -p 8080:8080 -v "$volume:/data" "$image" >/dev/null
  for i in $(seq 1 60); do
    if curl --fail --silent http://localhost:8080/health >/dev/null; then return 0; fi
    sleep 1
  done
  return 1
}
start
curl --fail --silent http://localhost:8080/Account/Login >/dev/null
before=$(docker exec "$container" sh -c 'sha256sum /data/planner-web.db /data/keys/*.xml')
docker exec "$container" sh -c 'test "$(stat -c %u /data/planner-web.db)" = "$(id -u app)"'
docker rm -f "$container" >/dev/null
start
curl --fail --silent http://localhost:8080/Account/Login >/dev/null
after=$(docker exec "$container" sh -c 'sha256sum /data/planner-web.db /data/keys/*.xml')
test "$before" = "$after"
echo 'PASS Docker volume preserves SQLite and session keys after container replacement'
