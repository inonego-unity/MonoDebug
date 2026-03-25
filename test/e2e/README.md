# E2E Tests

End-to-end test suite for MonoDebug CLI. Requires Unity running in Play mode with DebugTest component.

## Setup

1. Find SDB port:
```bash
grep "debugger-agent" "$LOCALAPPDATA/Unity/Editor/Editor.log" | tail -1
```

2. Enter Play mode:
```bash
unicli editor play
```

## Run

```bash
./run_all.sh <sdb_port> [monodebug_path]

# Example
./run_all.sh 56556 "path/to/monodebug.exe"
```

## Test Sections

| Section | Tests |
|---------|-------|
| A. Error Cases | no args, no daemon, no port |
| B. Session | attach, status, status --full |
| C. Break | set, duplicate, temp, list, disable, enable, remove, errors |
| D. Catch | set, list, remove --all, BP isolation |
| E. Flow + BP Hit | vmstart, continue, BP hit |
| F. Inspect + Eval | vars, depth, static, stack, eval (Roslyn), thread, vars set |
| G. Stepping | step, next, out, frame switch, until, goto, --count |
| H. Pause + Running | pause, vars/eval after pause, running state errors (NOT_STOPPED) |
| I. Profile | create, dup, info, edit, switch, disable --all, remove |
| J. Errors | unknown flow, unknown group, eval no expr |
| K. Detach | detach, daemon exit |
