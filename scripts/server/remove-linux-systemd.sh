#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run this script with sudo." >&2
  exit 1
fi

systemctl disable --now ptzcontrolserver.service 2>/dev/null || true
rm -f /etc/systemd/system/ptzcontrolserver.service
systemctl daemon-reload
echo 'Removed ptzcontrolserver.service. Program files were left in place.'
