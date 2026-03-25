# MonoDebug

`monodebug <command> [args...] [--options]` — all commands return JSON.

## Architecture

```
monodebug CLI → Named Pipe → daemon → SDB TCP → Mono Runtime
```

The daemon runs as a background process. It maintains the SDB connection and session state across multiple CLI invocations.

## Output Format

```json
{"success":true,"result":...}
{"success":false,"error":{"code":"...","message":"..."}}
```

Error codes: `INVALID_ARGS`, `NOT_STOPPED`, `NO_SESSION`, `CONNECT_FAILED`, `EVAL_ERROR`, `NOT_FOUND`, `ALREADY_EXISTS`

Use `jq` to extract specific fields and reduce output tokens:
```bash
monodebug vars | jq '.this.fields'
monodebug stack --full | jq '.frames[0]'
monodebug thread list | jq '.threads[] | select(.name != "")'
```

## Important

- **Each command must be a separate Bash call.** Do NOT chain monodebug commands with `;` or `&&` in a single Bash call. The event queue between daemon and CLI requires separate process invocations.
- **Attach before use**: `monodebug attach <port>` must be called first. Use `--profiles <path>` to persist breakpoints.
- **SDB port**: Found in process launch args (`--debugger-agent=...address=127.0.0.1:<port>`). Changes on process restart.
- **Event loop**: After attach, consume `vmstart` with `flow wait`, then `flow continue` before breakpoints will hit.
- **Detach cleanly**: `monodebug detach` stops daemon. Re-attach is possible without process restart.
- **VM must be stopped**: `vars`, `stack`, `eval`, `flow step/next/out/goto` require the VM to be suspended (breakpoint hit or `flow pause`).

---

## Session

```bash
monodebug attach <port> [--host <host>] [--profiles <path>]
monodebug detach
monodebug status [--full]
```

`--profiles` path enables breakpoint persistence across detach/re-attach cycles. Profiles are saved as JSON files in `<path>/.monodebug/profiles/`.

`status --full` includes thread list.

---

## flow — Flow Control

| Command | Description |
|---------|-------------|
| `flow wait [--timeout N]` | Wait for event (default 30000ms) |
| `flow continue` | Resume execution |
| `flow next [--count N]` | Step over |
| `flow step [--count N]` | Step into |
| `flow out [--count N]` | Step out |
| `flow until [file] <line>` | Run to line (temporary breakpoint) |
| `flow goto [file] <line>` | Set instruction pointer |
| `flow pause` | Suspend VM |

**Typical flow**:
```bash
monodebug attach 56400 --profiles /path/to/project
monodebug flow wait --timeout 3000     # consume vmstart
monodebug flow continue                # resume
monodebug flow wait --timeout 10000    # wait for BP hit
monodebug vars                         # inspect
monodebug flow next                    # step
monodebug flow wait --timeout 5000     # wait for step complete
monodebug detach
```

`flow wait` returns:
```json
{"reason":"vmstart","success":true}
{"reason":"breakpoint","thread":2,"method":"...","file":"...","line":14,"eval":{"this.counter":"5","this.speed":"5.5"},"success":true}
{"reason":"step","thread":2,"method":"...","file":"...","line":15,"success":true}
{"reason":"timeout","success":true}
```

The `eval` field appears only when the hit BP has `--eval` expressions attached.

---

## break — Breakpoints

| Command | Description |
|---------|-------------|
| `break set <file> <line> [options]` | Set breakpoint |
| `break remove <id> [--all] [--profile <name>]` | Remove breakpoint |
| `break list [--profile <name>]` | List breakpoints |
| `break enable <id>` | Enable breakpoint |
| `break disable <id>` | Disable breakpoint |

Options: `--condition '<expr>'`, `--hit-count N`, `--thread <id>`, `--temp`, `--profile '<name>'`, `--desc '<text>'`, `--eval '<expr>'`

File must be the full source file path as reported by the debugger.

---

## catch — Exception Breakpoints

| Command | Description |
|---------|-------------|
| `catch set <type> [options]` | Break on exception type |
| `catch set --all` | Break on all exceptions |
| `catch set <type> --unhandled` | Break on unhandled only |
| `catch remove <id> [--all] [--profile <name>]` | Remove catchpoint |
| `catch list` | List catchpoints |
| `catch enable <id>` | Enable catchpoint |
| `catch disable <id>` | Disable catchpoint |
| `catch info [--stack] [--inner N]` | Inspect current exception |

---

## Inspection

### stack

```bash
monodebug stack                    # current thread frames
monodebug stack --full             # frames with variables
monodebug stack --all              # all threads
monodebug stack frame <n>          # switch to frame N
```

### thread

```bash
monodebug thread list              # list all threads
monodebug thread <id>              # switch to thread
```

### vars

```bash
monodebug vars                     # this + args + locals
monodebug vars --depth 2           # expand nested objects
monodebug vars --args              # args only
monodebug vars --locals            # locals only
monodebug vars set <name> <value>  # set variable (int, float, double, bool, long, string)
monodebug vars --static '<type>'   # static fields of a type
```

`vars` output:
```json
{
  "thread": 2, "frame": 0,
  "this": {"type":"Player","fields":{"health":100,"name":"Hero"}},
  "args": [{"name":"delta","type":"System.Single","value":0.016}],
  "locals": [{"name":"speed","type":"System.Single","value":5.5}]
}
```

### eval

Roslyn-based C# expression evaluation:

```bash
monodebug eval '<expr>'            # evaluate expression
monodebug eval 'this.health'       # field access
monodebug eval '1 + 2'             # arithmetic
monodebug eval 'this.speed * 2'    # mixed expressions
monodebug eval 'this.label.Length'  # property access
monodebug eval 'counter > 100'     # comparison
monodebug eval 'counter > 100 ? "high" : "low"'  # ternary
```

Supports: arithmetic, property access, method calls, comparisons, ternary, indexers, casts, and more.

---

## profile — Debug Profiles

Profiles group breakpoints and catchpoints. Each profile is saved as a separate JSON file.

| Command | Description |
|---------|-------------|
| `profile create <name> [--desc '<text>']` | Create profile |
| `profile remove <name>` | Remove (cascade deletes points) |
| `profile edit <name> [--desc] [--rename]` | Edit profile |
| `profile switch <name>` | Activate (disables others) |
| `profile enable <name>` | Enable profile |
| `profile disable <name> [--all]` | Disable profile |
| `profile list` | List all profiles |
| `profile info <name>` | Show profile with breakpoints |

---

## Breakpoint Features

- `--condition '<expr>'` — conditional BP using Roslyn eval. Skips hit if expression is false.
- `--eval '<expr>'` — auto-evaluate expressions on BP hit. Results included in wait response as `"eval":{...}`.
- `--temp` — one-shot BP, auto-removed after first hit.
- `--hit-count N` — only stop on Nth hit.
- `--thread <id>` — only stop on specific thread.

## Limitations

- SDB allows one debugger connection at a time. Detach before re-attaching.
