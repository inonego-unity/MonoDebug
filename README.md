<p align="center">
  <h1 align="center">MonoDebug</h1>
  <p align="center">
    Mono SDB Debugger for AI Agents
  </p>
  <p align="center">
    <a href="https://opensource.org/licenses/MIT"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="License: MIT"></a>
    <img src="https://img.shields.io/badge/.NET-8.0-purple?logo=dotnet" alt=".NET 8.0">
    <img src="https://img.shields.io/badge/Mono-SDB-green" alt="Mono SDB">
  </p>
  <p align="center">
    <b>English</b> | <a href="README.ko.md">한국어</a>
  </p>
</p>

---

MonoDebug is a command-line debugger for Mono-based runtimes. AI agents can attach to a running process, set breakpoints, step through code, and inspect variables — all through JSON over Named Pipes.

Built for Unity debugging but works with any Mono runtime that exposes the Soft Debugger (SDB) protocol.

## Architecture

```
AI Agent / Script → monodebug CLI → Named Pipe → daemon → SDB TCP → Mono Runtime
```

- **`monodebug`** — .NET 8 CLI. Sends commands to the daemon via Named Pipe.
- **daemon** — Background process. Maintains the SDB connection and session state.
- **Named Pipe** — `monodebug-{port}`. Cross-platform IPC (Windows + Unix).

## Structure

```
cli/
├── Program.cs                   CLI entry point
├── Commands/
│   ├── BreakHandler.cs          break / catch commands
│   ├── FlowHandler.cs           flow commands
│   ├── InspectHandler.cs        stack / thread / vars / eval
│   └── ProfileHandler.cs        profile commands
└── Core/
    ├── DebugContext.cs           Shared context (Session + Profiles)
    ├── DebugDaemon.cs            Named Pipe server + dispatch
    ├── Constants.cs              Shared constants + error codes
    ├── OptionExtensions.cs       Optionals/args helpers
    ├── DebugPoint/
    │   ├── DebugPoint.cs         Abstract base
    │   ├── BreakPoint.cs         Location breakpoint
    │   └── CatchPoint.cs         Exception catchpoint
    ├── Profile/
    │   ├── DebugProfile.cs       Profile (owns breakpoints + catchpoints)
    │   └── ProfileCollection.cs  Profile management + save/load
    └── Session/
        ├── MonoDebugSession.cs   SoftDebuggerSession + Roslyn eval
        ├── StackInspector.cs     this/args/locals extraction
        ├── ValueFormatter.cs     SDB Value → JSON
        └── ExceptionHelper.cs    Exception info extraction
```

## Quick Start

```bash
# Attach to a Mono process listening on SDB port 56400
monodebug attach 56400 --profiles /path/to/project

# Consume vmstart event and resume
monodebug flow wait --timeout 5000
monodebug flow continue

# Set a breakpoint and wait for hit
monodebug break set /path/to/PlayerController.cs 42
monodebug flow wait --timeout 30000

# Inspect variables
monodebug vars
monodebug eval 'player.health'
monodebug eval 'player.speed * 2'
monodebug eval 'enemies.Count'

# Step through code
monodebug flow next
monodebug flow step
monodebug flow out

# View call stack
monodebug stack --full

# Detach
monodebug detach
```

## Commands

### Session

| Command | Description |
|---------|-------------|
| `monodebug attach <port> [--host] [--profiles]` | Start daemon and connect to SDB |
| `monodebug detach` | Disconnect and stop daemon |
| `monodebug status [--full]` | Show connection state (--full includes threads) |

### Flow Control

| Command | Description |
|---------|-------------|
| `flow wait [--timeout N]` | Wait for breakpoint hit (default 30s) |
| `flow continue` | Resume execution |
| `flow next [--count N]` | Step over |
| `flow step [--count N]` | Step into |
| `flow out [--count N]` | Step out |
| `flow until [file] <line>` | Run to line |
| `flow goto [file] <line>` | Set instruction pointer |
| `flow pause` | Suspend VM |

### Breakpoints

| Command | Description |
|---------|-------------|
| `break set <file> <line> [options]` | Set breakpoint |
| `break remove <id> [--all] [--profile]` | Remove breakpoint |
| `break list [--profile <name>]` | List breakpoints |
| `break enable <id>` | Enable breakpoint |
| `break disable <id>` | Disable breakpoint |

Options: `--condition '<expr>'`, `--hit-count N`, `--thread <id>`, `--temp`, `--profile '<name>'`, `--desc '<text>'`, `--eval '<expr>'`

### Exception Breakpoints

| Command | Description |
|---------|-------------|
| `catch set <type> [options]` | Break on exception |
| `catch set --all` | Break on all exceptions |
| `catch remove <id> [--all] [--profile]` | Remove catchpoint |
| `catch list` | List catchpoints |
| `catch enable <id>` | Enable catchpoint |
| `catch disable <id>` | Disable catchpoint |
| `catch info [--stack] [--inner N]` | Inspect caught exception |

Options: `--all`, `--unhandled`, `--condition '<expr>'`, `--hit-count N`, `--thread <id>`, `--profile '<name>'`, `--desc '<text>'`

### Inspection

| Command | Description |
|---------|-------------|
| `stack [--full] [--all]` | Call stack |
| `stack frame <n>` | Switch stack frame |
| `thread list` | List threads |
| `thread <id>` | Switch thread |
| `vars [--depth N] [--args] [--locals]` | View variables (this/args/locals) |
| `vars set <name> <value>` | Set variable value |
| `vars --static '<type>'` | View static fields |
| `eval '<expr>'` | Evaluate C# expression (Roslyn) |

### Profiles

Debug profiles group breakpoints and catchpoints for different debugging scenarios. Each profile is saved as a separate JSON file.

| Command | Description |
|---------|-------------|
| `profile create <name> [--desc '<text>']` | Create profile |
| `profile remove <name>` | Remove profile (cascade deletes points) |
| `profile switch <name>` | Activate profile (disables others) |
| `profile enable <name>` | Enable profile |
| `profile disable <name>` | Disable profile |
| `profile edit <name> [--desc '<text>'] [--rename '<name>']` | Edit profile |
| `profile list` | List all profiles |
| `profile info <name>` | Show profile details |

## JSON Output

All output is JSON. Pipe through `jq` for formatting:

```bash
monodebug vars | jq '.locals'
monodebug stack --full | jq '.frames[0].this'
monodebug thread list | jq '.threads[] | select(.name != "")'
```

## Expression Evaluation

`eval` uses Roslyn-based C# expression evaluation:

```bash
monodebug eval 'this.speed'              # field access
monodebug eval '1 + 2'                   # arithmetic
monodebug eval 'this.speed * 2'          # mixed
monodebug eval 'this.label.Length'        # property access
monodebug eval 'counter > 100'           # comparison
monodebug eval 'counter > 100 ? "high" : "low"'  # ternary
```

Conditional breakpoints also use eval:
```bash
monodebug break set /path/to/Player.cs 42 --condition 'health < 10'
```

## With Unity (via UniCLI)

```bash
# Use UniCLI to find Unity instances and enter Play mode
unicli list
unicli editor play

# Use MonoDebug for debugging
monodebug attach 56400 --profiles "$(pwd)"
monodebug flow wait --timeout 5000    # vmstart
monodebug flow continue

monodebug break set /path/to/DebugTest.cs 19
monodebug flow wait --timeout 30000   # BP hit
monodebug vars
monodebug eval 'this.speed * 2'
monodebug detach
```

## Building

```bash
git clone --recursive https://github.com/inonego-unity/MonoDebug.git
cd MonoDebug
dotnet publish cli/monodebug.csproj -c Release -o out
dotnet test test/MonoDebug.TEST.csproj
```

## Dependencies

| Dependency | Purpose | License |
|------------|---------|---------|
| [mono/debugger-libs](https://github.com/mono/debugger-libs) | Mono.Debugger.Soft + Mono.Debugging.Soft (SDB + Roslyn eval) | MIT |
| [Mono.Cecil](https://www.nuget.org/packages/Mono.Cecil) | Assembly metadata (runtime dependency) | MIT |
| [InoCLI](https://github.com/inonego/InoCLI) | CLI argument parser | MIT |
| [InoIPC](https://github.com/inonego/InoIPC) | IPC transport + frame protocol | MIT |

## License

[MIT](LICENSE)
