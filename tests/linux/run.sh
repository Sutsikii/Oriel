#!/bin/bash
# Copies the repository mounted at /repo, then runs the end-to-end tests on
# a virtual display. Xvfb is started by hand: xvfb-run waits forever for it
# when it is the container's first process.
set -euo pipefail
mkdir -p /work
tar -C /repo --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git -cf - . | tar -xf - -C /work
cd /work
Xvfb :99 -screen 0 1280x1024x24 -nolisten tcp &
export DISPLAY=:99
for _ in $(seq 50); do [ -e /tmp/.X11-unix/X99 ] && break; sleep 0.1; done
dbus-run-session -- dotnet test tests/Oriel.EndToEnd.Tests -c Release --logger "console;verbosity=normal" "$@"
