#!/bin/sh
set -e

# Resolve target uploads directory from Storage__UploadsPath or fallback to default
TARGET_DIR="${Storage__UploadsPath:-/app/wwwroot/uploads}"

# Ensure the uploads directory exists and is owned by non-root $APP_UID
mkdir -p "$TARGET_DIR" 2>/dev/null || true
chown -R "${APP_UID:-1654}:${APP_UID:-1654}" "$TARGET_DIR" 2>/dev/null || true

# If running as root and APP_UID is set, drop privileges and execute command as $APP_UID
if [ "$(id -u)" = "0" ] && [ -n "$APP_UID" ]; then
    if command -v setpriv >/dev/null 2>&1; then
        exec setpriv --reuid="$APP_UID" --regid="$APP_UID" --init-groups "$@"
    elif command -v gosu >/dev/null 2>&1; then
        exec gosu "$APP_UID" "$@"
    elif command -v su-exec >/dev/null 2>&1; then
        exec su-exec "$APP_UID" "$@"
    else
        exec su -s /bin/sh "$APP_UID" -c 'exec "$@"' -- "$@"
    fi
fi

exec "$@"

