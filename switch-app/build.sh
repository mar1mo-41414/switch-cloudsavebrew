#!/usr/bin/env bash
# Builds the homebrew .nro using the official devkitPro devkitA64 Docker
# image, so no local devkitPro install is required. apt.devkitpro.org is
# blocked by Cloudflare from this network — Docker Hub isn't, so this is
# the path that actually works here.
#
# Runs as the host UID/GID so build output isn't left root-owned.
set -euo pipefail
cd "$(dirname "$0")"

docker run --rm --user "$(id -u):$(id -g)" -v "$PWD":/project -w /project \
    devkitpro/devkita64:latest make "$@"
