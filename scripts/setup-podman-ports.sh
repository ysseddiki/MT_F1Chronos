#!/usr/bin/env bash
# Autorise Podman rootless à écouter 80/443 (sinon : bind permission denied).
# À lancer une fois sur le VPS avec sudo.
set -euo pipefail

if [[ "$(id -u)" -ne 0 ]]; then
  echo "Relance avec sudo : sudo $0" >&2
  exit 1
fi

CONF="/etc/sysctl.d/99-rootless-unprivileged-ports.conf"
echo 'net.ipv4.ip_unprivileged_port_start=80' > "$CONF"
sysctl --system >/dev/null
echo "OK — ports 80–1023 autorisés pour les conteneurs rootless."
echo "Relance : podman compose up -d --build"
