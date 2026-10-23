#!/bin/sh
set -eu
# A mounted Amvera volume replaces the ownership prepared in the image.
# Initialize permissions as root, then run ASP.NET Core as the built-in app user.
if [ "$(id -u)" = "0" ]; then
    mkdir -p /data
    chown -R app:app /data
    chmod 700 /data
    exec gosu app "$@"
fi
exec "$@"
