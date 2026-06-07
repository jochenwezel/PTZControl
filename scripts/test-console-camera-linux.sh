#!/usr/bin/env bash
set -u

EXE_PATH="./PTZControlConsole"
CAMERA=""
DEVICE_PATH=""
SLOT=""
RAW_ZOOM_DELTA=100
RAW_MOVE_DELTA=3000
RAW_MOVE_ABSOLUTE=9000
LOG_PATH=""

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
    --slot)
      SLOT="$2"
      shift 2
      ;;
    --raw-zoom-delta)
      RAW_ZOOM_DELTA="$2"
      shift 2
      ;;
    --raw-move-delta)
      RAW_MOVE_DELTA="$2"
      shift 2
      ;;
    --raw-move-absolute)
      RAW_MOVE_ABSOLUTE="$2"
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

if [ -z "$LOG_PATH" ]; then
  LOG_PATH="./PTZControlConsole-camera-test-linux-$(date '+%Y%m%d-%H%M%S').log"
fi

log_line() {
  printf '%s %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$1" >>"$LOG_PATH"
}

parse_camera_devices() {
  DEVICE_NAMES=()
  DEVICE_PATHS=()
  local line name path
  while IFS= read -r line; do
    [ -n "$line" ] || continue
    if [[ "$line" == *$'\t'* ]]; then
      name="${line%%$'\t'*}"
      path="${line#*$'\t'}"
    else
      name="${line%%  *}"
      path="${line#*  }"
    fi

    if [ -n "$name" ] && [ -n "$path" ] && [ "$name" != "$path" ]; then
      DEVICE_NAMES+=("$name")
      DEVICE_PATHS+=("$path")
    fi
  done <<<"$1"
}

run_camera_command() {
  local title="$1"
  shift
  local ask_visible_result="$1"
  shift
  local args=("$@")
  local stdout_file stderr_file exit_code result
  stdout_file="$(mktemp)"
  stderr_file="$(mktemp)"

  echo
  if [ "$ask_visible_result" = "true" ]; then
    echo "Test: $title"
  fi

  log_line ""
  log_line "COMMAND: $title"
  log_line "VISIBLE-CHECK: $ask_visible_result"
  log_line "ARGS: ${args[*]}"

  "$EXE_PATH" "${args[@]}" >"$stdout_file" 2>"$stderr_file"
  exit_code=$?

  log_line "EXIT: $exit_code"
  log_line "STDOUT: $(cat "$stdout_file")"
  log_line "STDERR: $(cat "$stderr_file")"

  if [ "$ask_visible_result" = "true" ]; then
    read -r -p "Visible camera result OK? (y/n/skip, optional note) " result
    log_line "USER-RESULT: $result"
  else
    log_line "USER-RESULT: not requested"
  fi

  rm -f "$stdout_file" "$stderr_file"
}

run_logged_command() {
  local title="$1"
  shift
  local args=("$@")
  local stdout_file stderr_file exit_code stdout stderr
  stdout_file="$(mktemp)"
  stderr_file="$(mktemp)"

  log_line ""
  log_line "COMMAND: $title"
  log_line "VISIBLE-CHECK: false"
  log_line "ARGS: ${args[*]}"

  "$EXE_PATH" "${args[@]}" >"$stdout_file" 2>"$stderr_file"
  exit_code=$?
  stdout="$(cat "$stdout_file")"
  stderr="$(cat "$stderr_file")"

  log_line "EXIT: $exit_code"
  log_line "STDOUT: $stdout"
  log_line "STDERR: $stderr"
  log_line "USER-RESULT: not requested"

  rm -f "$stdout_file" "$stderr_file"
  RUN_LOGGED_STDOUT="$stdout"
  RUN_LOGGED_EXIT="$exit_code"
}

run_preparation_command() {
  local title="$1"
  shift
  run_logged_command "Prepare: $title" "$@"
  if [ "$RUN_LOGGED_EXIT" -ne 0 ]; then
    log_line "PREPARE-WARNING: preparation command failed; continuing with visible test"
  fi
}

select_visible_camera() {
  selector=()
  if [ "${#DEVICE_NAMES[@]}" -eq 0 ]; then
    echo "No camera devices found. Visible tests will run without an explicit selector."
    return
  fi

  echo
  echo "Select camera for visible tests:"
  local i choice
  for i in "${!DEVICE_NAMES[@]}"; do
    printf '  %s: %s\n' "$((i + 1))" "${DEVICE_NAMES[$i]}"
  done

  while true; do
    read -r -p "Camera number: " choice
    if [[ "$choice" =~ ^[0-9]+$ ]] && [ "$choice" -ge 1 ] && [ "$choice" -le "${#DEVICE_NAMES[@]}" ]; then
      break
    fi
  done

  local index=$((choice - 1))
  echo "Visible tests use: ${DEVICE_NAMES[$index]}"
  log_line "VISIBLE-TEST-CAMERA: ${DEVICE_NAMES[$index]}"
  log_line "VISIBLE-TEST-DEVICE-PATH: ${DEVICE_PATHS[$index]}"
  selector=(--device-path "${DEVICE_PATHS[$index]}")
}

: >"$LOG_PATH"

selector=()
if [ -n "$CAMERA" ]; then
  selector=(--camera "$CAMERA")
elif [ -n "$DEVICE_PATH" ]; then
  selector=(--device-path "$DEVICE_PATH")
elif [ -n "$SLOT" ]; then
  selector=(--slot "$SLOT")
fi

echo "PTZControlConsole guided Linux camera test"
echo "Log file: $LOG_PATH"

log_line "PTZControlConsole guided Linux camera test"
log_line "Executable: $EXE_PATH"
log_line "Selector: ${selector[*]}"
log_line "RawZoomDelta: $RAW_ZOOM_DELTA"
log_line "RawMoveDelta: $RAW_MOVE_DELTA"
log_line "RawMoveAbsolute: $RAW_MOVE_ABSOLUTE"

run_logged_command "List available camera devices" list-devices
parse_camera_devices "$RUN_LOGGED_STDOUT"

if [ "${#selector[@]}" -eq 0 ]; then
  select_visible_camera
else
  echo "Visible tests use selector: ${selector[*]}"
  log_line "VISIBLE-TEST-SELECTOR: ${selector[*]}"
fi

for i in "${!DEVICE_NAMES[@]}"; do
  device_selector=(--device-path "${DEVICE_PATHS[$i]}")
  run_logged_command "Collect camera device info and supported ranges for ${DEVICE_NAMES[$i]}" cam-device-info "${device_selector[@]}"
  run_logged_command "Collect preset names and storage details for ${DEVICE_NAMES[$i]}" list-presets "${device_selector[@]}"
done

run_preparation_command "Move zoom away from absolute percent value 0" zoom-absolute 100 --mode percent "${selector[@]}"
run_camera_command "Set zoom to absolute percent value 0" true zoom-absolute 0 --mode percent "${selector[@]}"
run_preparation_command "Move zoom away from absolute percent value 50" zoom-absolute 0 --mode percent "${selector[@]}"
run_camera_command "Set zoom to absolute percent value 50" true zoom-absolute 50 --mode percent "${selector[@]}"
run_preparation_command "Move zoom away from absolute percent value 100" zoom-absolute 0 --mode percent "${selector[@]}"
run_camera_command "Set zoom to absolute percent value 100" true zoom-absolute 100 --mode percent "${selector[@]}"
run_preparation_command "Move zoom to low percent value before relative +10" zoom-absolute 0 --mode percent "${selector[@]}"
run_camera_command "Change zoom by relative percent value +10" true zoom-relative 10 --mode percent "${selector[@]}"
run_preparation_command "Move zoom to high percent value before relative -10" zoom-absolute 100 --mode percent "${selector[@]}"
run_camera_command "Change zoom by relative percent value -10" true zoom-relative -10 --mode percent "${selector[@]}"
run_preparation_command "Move zoom away from absolute raw value 0" zoom-relative "$RAW_ZOOM_DELTA" --mode raw "${selector[@]}"
run_camera_command "Set zoom to absolute raw value 0" true zoom-absolute 0 --mode raw "${selector[@]}"
run_preparation_command "Move zoom down before relative raw +$RAW_ZOOM_DELTA" zoom-relative "-$RAW_ZOOM_DELTA" --mode raw "${selector[@]}"
run_camera_command "Change zoom by relative raw value +$RAW_ZOOM_DELTA" true zoom-relative "$RAW_ZOOM_DELTA" --mode raw "${selector[@]}"
run_preparation_command "Move zoom up before relative raw -$RAW_ZOOM_DELTA" zoom-relative "$RAW_ZOOM_DELTA" --mode raw "${selector[@]}"
run_camera_command "Change zoom by relative raw value -$RAW_ZOOM_DELTA" true zoom-relative "-$RAW_ZOOM_DELTA" --mode raw "${selector[@]}"
run_preparation_command "Move pan and tilt away from percent center" move-absolute --mode percent --pan 0 --tilt 0 "${selector[@]}"
run_camera_command "Move pan and tilt to absolute percent position 50/50" true move-absolute --mode percent --pan 50 --tilt 50 "${selector[@]}"
run_preparation_command "Move pan away from absolute percent position 40" move-absolute --mode percent --pan 60 "${selector[@]}"
run_camera_command "Move pan axis to absolute percent position 40" true move-absolute --mode percent --pan 40 "${selector[@]}"
run_preparation_command "Move tilt away from absolute percent position 60" move-absolute --mode percent --tilt 40 "${selector[@]}"
run_camera_command "Move tilt axis to absolute percent position 60" true move-absolute --mode percent --tilt 60 "${selector[@]}"
run_preparation_command "Move pan before relative percent +10" move-absolute --mode percent --pan 40 "${selector[@]}"
run_camera_command "Change pan axis by relative percent value +10" true move-relative --mode percent --pan 10 "${selector[@]}"
run_preparation_command "Move pan before relative percent -10" move-absolute --mode percent --pan 60 "${selector[@]}"
run_camera_command "Change pan axis by relative percent value -10" true move-relative --mode percent --pan -10 "${selector[@]}"
run_preparation_command "Move tilt before relative percent +10" move-absolute --mode percent --tilt 40 "${selector[@]}"
run_camera_command "Change tilt axis by relative percent value +10" true move-relative --mode percent --tilt 10 "${selector[@]}"
run_preparation_command "Move tilt before relative percent -10" move-absolute --mode percent --tilt 60 "${selector[@]}"
run_camera_command "Change tilt axis by relative percent value -10" true move-relative --mode percent --tilt -10 "${selector[@]}"
run_preparation_command "Move pan and tilt away from raw center" move-absolute --mode raw --pan "$RAW_MOVE_ABSOLUTE" --tilt "$RAW_MOVE_ABSOLUTE" "${selector[@]}"
run_camera_command "Move pan and tilt to absolute raw position 0/0" true move-absolute --mode raw --pan 0 --tilt 0 "${selector[@]}"
run_preparation_command "Move pan away from absolute raw +$RAW_MOVE_ABSOLUTE" move-absolute --mode raw --pan 0 "${selector[@]}"
run_camera_command "Move pan axis to absolute raw position +$RAW_MOVE_ABSOLUTE" true move-absolute --mode raw --pan "$RAW_MOVE_ABSOLUTE" "${selector[@]}"
run_preparation_command "Move pan away from absolute raw -$RAW_MOVE_ABSOLUTE" move-absolute --mode raw --pan 0 "${selector[@]}"
run_camera_command "Move pan axis to absolute raw position -$RAW_MOVE_ABSOLUTE" true move-absolute --mode raw --pan "-$RAW_MOVE_ABSOLUTE" "${selector[@]}"
run_preparation_command "Move tilt away from absolute raw +$RAW_MOVE_ABSOLUTE" move-absolute --mode raw --tilt 0 "${selector[@]}"
run_camera_command "Move tilt axis to absolute raw position +$RAW_MOVE_ABSOLUTE" true move-absolute --mode raw --tilt "$RAW_MOVE_ABSOLUTE" "${selector[@]}"
run_preparation_command "Move tilt away from absolute raw -$RAW_MOVE_ABSOLUTE" move-absolute --mode raw --tilt 0 "${selector[@]}"
run_camera_command "Move tilt axis to absolute raw position -$RAW_MOVE_ABSOLUTE" true move-absolute --mode raw --tilt "-$RAW_MOVE_ABSOLUTE" "${selector[@]}"
run_preparation_command "Move pan before relative raw +$RAW_MOVE_DELTA" move-absolute --mode raw --pan 0 "${selector[@]}"
run_camera_command "Change pan axis by relative raw value +$RAW_MOVE_DELTA" true move-relative --mode raw --pan "$RAW_MOVE_DELTA" "${selector[@]}"
run_preparation_command "Move pan before relative raw -$RAW_MOVE_DELTA" move-absolute --mode raw --pan 0 "${selector[@]}"
run_camera_command "Change pan axis by relative raw value -$RAW_MOVE_DELTA" true move-relative --mode raw --pan "-$RAW_MOVE_DELTA" "${selector[@]}"
run_preparation_command "Move tilt before relative raw +$RAW_MOVE_DELTA" move-absolute --mode raw --tilt 0 "${selector[@]}"
run_camera_command "Change tilt axis by relative raw value +$RAW_MOVE_DELTA" true move-relative --mode raw --tilt "$RAW_MOVE_DELTA" "${selector[@]}"
run_preparation_command "Move tilt before relative raw -$RAW_MOVE_DELTA" move-absolute --mode raw --tilt 0 "${selector[@]}"
run_camera_command "Change tilt axis by relative raw value -$RAW_MOVE_DELTA" true move-relative --mode raw --tilt "-$RAW_MOVE_DELTA" "${selector[@]}"
run_preparation_command "Move pan and tilt away from home before restore-home" move-absolute --mode percent --pan 0 --tilt 0 "${selector[@]}"
run_camera_command "Restore home position for pan and tilt" true restore-home --target move "${selector[@]}"
run_preparation_command "Move zoom away from default before restore-default" zoom-absolute 100 --mode percent "${selector[@]}"
run_camera_command "Restore default zoom value" true restore-default --target zoom "${selector[@]}"
run_camera_command "Restore preset 1" true restore-preset 1 "${selector[@]}"
run_camera_command "Restore preset 2" true restore-preset 2 "${selector[@]}"

echo
echo "Log written to $LOG_PATH"
