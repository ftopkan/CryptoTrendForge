#!/bin/sh
# Plesk scheduled task entry. A previous worker can sit forever in
# "Application is shutting down..." and then Plesk will not start the next run.
# Kill that leftover process before starting a fresh one-shot cycle.
set -u
DIR=$(CDPATH= cd -- "$(dirname "$0")" && pwd)
DOTNET=${DOTNET_BIN:-/usr/bin/dotnet}
if [ ! -x "$DOTNET" ]; then
  DOTNET=dotnet
fi

if [ -d /proc ]; then
  for cmdline in /proc/[0-9]*/cmdline; do
    pid=${cmdline#/proc/}
    pid=${pid%/cmdline}
    cmd=$(tr '\0' ' ' < "$cmdline" 2>/dev/null || true)
    case "$cmd" in
      *CryptoTrendForge.Worker.dll*)
        kill -9 "$pid" 2>/dev/null || true
        ;;
    esac
  done
fi

sleep 1
cd "$DIR"
export DOTNET_ENVIRONMENT="${DOTNET_ENVIRONMENT:-Production}"
exec "$DOTNET" "$DIR/CryptoTrendForge.Worker.dll"
