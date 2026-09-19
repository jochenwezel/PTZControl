#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run this script with sudo." >&2
  exit 1
fi

install_dir="${PTZCONTROL_INSTALL_DIR:-/opt/ptzcontrolserver}"
service_user="${PTZCONTROL_USER:-${SUDO_USER:-root}}"
source_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
server_args="${PTZCONTROL_SERVER_ARGS:-}"

install -d -m 0755 "${install_dir}"
if [[ "$(realpath "${source_dir}")" != "$(realpath "${install_dir}")" ]]; then
  cp -a "${source_dir}/." "${install_dir}/"
fi
chmod +x "${install_dir}/PTZControlServer"

cat >/etc/systemd/system/ptzcontrolserver.service <<EOF
[Unit]
Description=PTZControl HTTP Server
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
User=${service_user}
WorkingDirectory=${install_dir}
ExecStart=${install_dir}/PTZControlServer ${server_args}
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable --now ptzcontrolserver.service
systemctl status ptzcontrolserver.service --no-pager
