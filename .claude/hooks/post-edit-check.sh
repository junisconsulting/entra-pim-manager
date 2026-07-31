#!/usr/bin/env bash
# PostToolUse hook: instant syntax feedback for the files the build does NOT check.
# - *.axaml  -> XML well-formedness (a broken tag otherwise surfaces as a cryptic
#               Avalonia compile error several seconds later)
# - *.json   -> JSON parse (appsettings*.json is copied to output and parsed at
#               RUNTIME only — on a Linux dev host that never happens, so a broken
#               file ships silently)
# Exit 2 feeds the error back to Claude for self-correction; anything else passes
# silently. Must stay <1s — no dotnet build, no tests here.
#
# ponytail: well-formedness only. Invalid bindings, unknown controls and missing
# classes are caught by the build (compiled bindings are on). Upgrading this to a
# real XAML compile would blow the <1s budget — run the verify skill instead.

set -u

# python3 does the checking below, so it parses the hook payload too — no jq dependency.
command -v python3 >/dev/null 2>&1 || exit 0
FILE="$(python3 -c "import sys,json; print(json.load(sys.stdin).get('tool_input',{}).get('file_path',''))" 2>/dev/null)"

# Nothing to do: no file path, or the file is gone.
[ -n "${FILE:-}" ] || exit 0
[ -f "$FILE" ] || exit 0

# Build output is generated, not hand-edited.
case "$FILE" in
  */bin/*|*/obj/*|*/artifacts/*|*/TestResults/*) exit 0 ;;
esac

case "$FILE" in
  *.axaml)
    # tail -1: the last traceback line carries the message and position; the rest is noise.
    ERR="$(python3 -c "import sys,xml.etree.ElementTree as ET; ET.parse(sys.argv[1])" "$FILE" 2>&1 | tail -1)" \
      && [ -z "$ERR" ] || {
      echo "Malformed XAML in $FILE: $ERR" >&2
      exit 2
    }
    ;;
  *.json)
    ERR="$(python3 -c "import sys,json; json.load(open(sys.argv[1], encoding='utf-8-sig'))" "$FILE" 2>&1 | tail -1)" \
      && [ -z "$ERR" ] || {
      echo "Invalid JSON in $FILE: $ERR" >&2
      exit 2
    }
    ;;
esac

exit 0
