#!/usr/bin/env bash
set -u

EXE_PATH="./PTZControlConsole"
CAMERA=""
DEVICE_PATH=""
LOG_PATH="./PTZControlConsole-camera-test-linux.log"

while [ $# -gt 0 ]; do
  case "$1" in
    --exe)
      EXE_PATH="$2"
      shift 2
      ;;
    --camera)
      CAMERA="$2"
      shift 2
      ;;
    --device-path)
      DEVICE_PATH="$2"
      shift 2
      ;;
    --log)
      LOG_PATH="$2"
      shift 2
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 2
      ;;
  esac
done

log_line() {
  printf '%s %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$1" | tee -a "$LOG_PATH"
}

run_camera_command() {
  local title="$1"
  shift
  local args=("$@")
  local stdout_file stderr_file exit_code result
  stdout_file="$(mktemp)"
  stderr_file="$(mktemp)"

  echo
  echo "== $title =="
  printf '%q ' "$EXE_PATH" "${args[@]}"
  echo

  log_line ""
  log_line "COMMAND: $title"
  log_line "ARGS: ${args[*]}"

  "$EXE_PATH" "${args[@]}" >"$stdout_file" 2>"$stderr_file"
  exit_code=$?

  echo "Exit code: $exit_code"
  if [ -s "$stdout_file" ]; then
    echo "STDOUT:"
    cat "$stdout_file"
  fi
  if [ -s "$stderr_file" ]; then
    echo "STDERR:"
    cat "$stderr_file"
  fi

  log_line "EXIT: $exit_code"
  log_line "STDOUT: $(cat "$stdout_file")"
  log_line "STDERR: $(cat "$stderr_file")"

  read -r -p "Visible camera result OK? (y/n/skip, optional note) " result
  log_line "USER-RESULT: $result"

  rm -f "$stdout_file" "$stderr_file"
}

: >"$LOG_PATH"

selector=()
if [ -n "$CAMERA" ]; then
  selector=(--camera "$CAMERA")
elif [ -n "$DEVICE_PATH" ]; then
  selector=(--device-path "$DEVICE_PATH")
fi

log_line "PTZControlConsole guided Linux camera test"
log_line "Executable: $EXE_PATH"
log_line "Selector: ${selector[*]}"

run_camera_command "List devices" list-devices
run_camera_command "Camera device info" cam-device-info "${selector[@]}"
run_camera_command "Zoom absolute percent 0" zoom-absolute 0 --mode percent "${selector[@]}"
run_camera_command "Zoom absolute percent 50" zoom-absolute 50 --mode percent "${selector[@]}"
run_camera_command "Zoom relative percent +10" zoom-relative 10 --mode percent "${selector[@]}"
run_camera_command "Zoom relative percent -10" zoom-relative -10 --mode percent "${selector[@]}"
run_camera_command "Move relative percent X +10" move-relative --mode percent --x 10 "${selector[@]}"
run_camera_command "Move relative percent X -10" move-relative --mode percent --x -10 "${selector[@]}"
run_camera_command "Move relative percent Y +10" move-relative --mode percent --y 10 "${selector[@]}"
run_camera_command "Move relative percent Y -10" move-relative --mode percent --y -10 "${selector[@]}"
run_camera_command "Restore home move" restore-home --target move "${selector[@]}"
run_camera_command "Restore default zoom" restore-default --target zoom "${selector[@]}"
run_camera_command "Restore preset 1" restore-preset 1 "${selector[@]}"
run_camera_command "Restore preset 2" restore-preset 2 "${selector[@]}"
run_camera_command "List presets" list-presets "${selector[@]}"

echo
echo "Log written to $LOG_PATH"
