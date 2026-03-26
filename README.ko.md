<p align="center">
  <h1 align="center">MonoDebug</h1>
  <p align="center">
    AI 에이전트를 위한 Mono SDB 디버거
  </p>
  <p align="center">
    <a href="https://opensource.org/licenses/MIT"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="License: MIT"></a>
    <img src="https://img.shields.io/badge/.NET-8.0-purple?logo=dotnet" alt=".NET 8.0">
    <img src="https://img.shields.io/badge/Mono-SDB-green" alt="Mono SDB">
  </p>
  <p align="center">
    <a href="README.md">English</a> | <b>한국어</b>
  </p>
</p>

---

MonoDebug는 Mono 기반 런타임용 CLI 디버거입니다. AI 에이전트가 실행 중인 프로세스에 붙어서 브레이크포인트 설정, 코드 스텝, 변수 조사를 할 수 있습니다. JSON over Named Pipe로 통신합니다.

Unity 디버깅을 위해 만들었지만, Soft Debugger (SDB) 프로토콜을 노출하는 모든 Mono 런타임에서 동작합니다.

## 아키텍처

```
AI 에이전트 / 스크립트 → monodebug CLI → Named Pipe → daemon → SDB TCP → Mono 런타임
```

- **`monodebug`** — .NET 8 CLI. Named Pipe를 통해 daemon에 명령 전달.
- **daemon** — 백그라운드 프로세스. SDB 연결 및 세션 상태 관리.
- **Named Pipe** — `monodebug-{port}`. 크로스 플랫폼 IPC (Windows + Unix).

## 구조

```
cli/
├── Program.cs                   CLI 진입점
├── Commands/
│   ├── BreakHandler.cs          break / catch 명령
│   ├── FlowHandler.cs           flow 명령
│   ├── InspectHandler.cs        stack / thread / vars / eval
│   └── ProfileHandler.cs        profile 명령
└── Core/
    ├── DebugContext.cs           공유 컨텍스트 (Session + Profiles)
    ├── DebugDaemon.cs            Named Pipe 서버 + 디스패치
    ├── Constants.cs              공유 상수 + 에러 코드
    ├── DebugPoint/
    │   ├── DebugPoint.cs         추상 베이스
    │   ├── BreakPoint.cs         위치 브레이크포인트
    │   └── CatchPoint.cs         예외 캐치포인트
    ├── Profile/
    │   ├── DebugProfile.cs       프로필 (브레이크포인트 + 캐치포인트 소유)
    │   └── ProfileCollection.cs  프로필 관리 + 저장/로드
    └── Session/
        ├── MonoDebugSession.cs   SoftDebuggerSession + Roslyn 평가
        ├── StackInspector.cs     this/args/locals 추출
        ├── ValueFormatter.cs     SDB Value → JSON
        └── ExceptionHelper.cs    예외 정보 추출
```

## 빠른 시작

```bash
# SDB 포트 56400에서 리스닝 중인 Mono 프로세스에 연결
monodebug attach 56400 --profiles /path/to/project

# vmstart 이벤트 소비 + 재개
monodebug flow wait --timeout 5000
monodebug flow continue

# 브레이크포인트 설정 + 히트 대기
monodebug break set /path/to/PlayerController.cs 42
monodebug flow wait --timeout 30000

# 변수 조사
monodebug vars
monodebug eval 'player.health'
monodebug eval 'player.speed * 2'
monodebug eval 'enemies.Count'

# 코드 스텝
monodebug flow next
monodebug flow step
monodebug flow out

# 콜 스택 확인
monodebug stack --full

# 연결 해제
monodebug detach
```

## 명령어

### 세션

| 명령 | 설명 |
|------|------|
| `monodebug attach <port> [--host] [--profiles]` | daemon 시작 + SDB 연결 |
| `monodebug detach` | 연결 해제 + daemon 종료 |
| `monodebug status [--full]` | 연결 상태 확인 (--full: 스레드 포함) |

### 실행 제어

| 명령 | 설명 |
|------|------|
| `flow wait [--timeout N]` | BP 히트 대기 (기본 30초) |
| `flow continue` | 실행 재개 |
| `flow next [--count N]` | Step over |
| `flow step [--count N]` | Step into |
| `flow out [--count N]` | Step out |
| `flow until [file] <line>` | 특정 줄까지 실행 |
| `flow goto [file] <line>` | 명령 포인터 이동 |
| `flow pause` | VM 일시 정지 |

### 브레이크포인트

| 명령 | 설명 |
|------|------|
| `break set <file> <line> [옵션]` | BP 설정 |
| `break remove <id> [--all] [--profile]` | BP 제거 |
| `break list [--profile <name>]` | BP 목록 |
| `break enable <id>` | BP 활성화 |
| `break disable <id>` | BP 비활성화 |

옵션: `--condition '<expr>'`, `--hit-count N`, `--thread <id>`, `--temp`, `--profile '<name>'`, `--desc '<text>'`, `--eval '<expr>'`

### 예외 브레이크포인트

| 명령 | 설명 |
|------|------|
| `catch set <type> [옵션]` | 예외에서 중단 |
| `catch set --all` | 모든 예외에서 중단 |
| `catch remove <id> [--all] [--profile]` | 캐치포인트 제거 |
| `catch list` | 캐치포인트 목록 |
| `catch enable <id>` | 캐치포인트 활성화 |
| `catch disable <id>` | 캐치포인트 비활성화 |
| `catch info [--stack] [--inner N]` | 잡힌 예외 조사 |

옵션: `--all`, `--unhandled`, `--condition '<expr>'`, `--hit-count N`, `--thread <id>`, `--profile '<name>'`, `--desc '<text>'`

### 조사

| 명령 | 설명 |
|------|------|
| `stack [--full] [--all]` | 콜 스택 |
| `stack frame <n>` | 스택 프레임 전환 |
| `thread list` | 스레드 목록 |
| `thread <id>` | 스레드 전환 |
| `vars [--depth N] [--args] [--locals]` | 변수 조회 (this/args/locals) |
| `vars set <name> <value>` | 변수 값 설정 |
| `vars --static '<type>'` | 정적 필드 조회 |
| `eval '<expr>'` | C# 표현식 평가 (Roslyn) |

### 프로필

프로필은 브레이크포인트와 캐치포인트를 그룹화하여 디버깅 시나리오별로 관리합니다. 각 프로필은 별도 JSON 파일로 저장됩니다.

| 명령 | 설명 |
|------|------|
| `profile create <name> [--desc '<text>']` | 프로필 생성 |
| `profile remove <name>` | 프로필 제거 (포인트 연쇄 삭제) |
| `profile switch <name>` | 프로필 전환 (다른 프로필 비활성화) |
| `profile enable <name>` | 프로필 활성화 |
| `profile disable <name>` | 프로필 비활성화 |
| `profile edit <name> [--desc '<text>'] [--rename '<name>']` | 프로필 수정 |
| `profile list` | 전체 프로필 목록 |
| `profile info <name>` | 프로필 상세 |

## JSON 출력

모든 출력은 JSON. `jq`로 포맷팅:

```bash
monodebug vars | jq '.locals'
monodebug stack --full | jq '.frames[0].this'
monodebug thread list | jq '.threads[] | select(.name != "")'
```

## 표현식 평가

`eval`은 Roslyn 기반 C# 표현식 평가를 지원합니다:

```bash
monodebug eval 'this.speed'              # 필드 접근
monodebug eval '1 + 2'                   # 산술
monodebug eval 'this.speed * 2'          # 혼합
monodebug eval 'this.label.Length'        # 프로퍼티 접근
monodebug eval 'counter > 100'           # 비교
monodebug eval 'counter > 100 ? "high" : "low"'  # 삼항
```

조건부 브레이크포인트도 eval을 사용합니다:
```bash
monodebug break set /path/to/Player.cs 42 --condition 'health < 10'
```

## Unity와 함께 (UniCLI 연동)

```bash
# UniCLI로 Unity 인스턴스 확인 + Play 모드
unicli list
unicli editor play

# MonoDebug로 디버깅
monodebug attach 56400 --profiles "$(pwd)"
monodebug flow wait --timeout 5000    # vmstart
monodebug flow continue

monodebug break set /path/to/DebugTest.cs 19
monodebug flow wait --timeout 30000   # BP 히트
monodebug vars
monodebug eval 'this.speed * 2'
monodebug detach
```

## 빌드

```bash
git clone --recursive https://github.com/inonego-unity/MonoDebug.git
cd MonoDebug
dotnet publish cli/monodebug.csproj -c Release -o out
dotnet test test/MonoDebug.TEST.csproj
```

## 의존성

| 의존성 | 용도 | 라이선스 |
|--------|------|---------|
| [mono/debugger-libs](https://github.com/mono/debugger-libs) | Mono.Debugger.Soft + Mono.Debugging.Soft (SDB + Roslyn 평가) | MIT |
| [Mono.Cecil](https://www.nuget.org/packages/Mono.Cecil) | 어셈블리 메타데이터 (런타임 의존성) | MIT |
| [InoCLI](https://github.com/inonego/InoCLI) | CLI 프레임워크 (파서 + 커맨드 레지스트리) | MIT |
| [InoIPC](https://github.com/inonego/InoIPC) | IPC 전송 + 프레임 프로토콜 | MIT |

## 라이선스

[MIT](LICENSE)
