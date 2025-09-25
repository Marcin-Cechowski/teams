# AntiAway (C# .NET 8 console)

A tiny Windows console app to help prevent Microsoft Teams from going "Away" by:
- Keeping the system/display awake (Windows Execution State)
- Optionally simulating minimal user input (subtle mouse jiggle, left click, or SHIFT key tap)
- Idle-aware: only simulates input if you've been idle longer than a threshold

## Build

From a Developer PowerShell or PowerShell in the repo root:

```bash
cd AntiAway
dotnet build -c Release
```

The executable will be at `AntiAway\bin\Release\net8.0-windows\AntiAway.exe`.

## Run

Run with idle-aware mouse jiggle, two actions every 10s, bigger movement:

```bash
AntiAway.exe --mode=mouse --interval=10s --idle-threshold=45s --jiggle-pixels=20 --actions=2
```

Run with idle-aware double click (2 clicks per 10s):

```bash
AntiAway.exe --mode=click --interval=10s --idle-threshold=45s --actions=2
```

Run with idle-aware key taps (SHIFT twice per 10s):

```bash
AntiAway.exe --mode=key --interval=10s --idle-threshold=45s --actions=2
```

### Options
- `--mode=es|mouse|click|key`
- `--interval=<seconds|hh:mm:ss|Ns|Nm|Nh>` (default `30s`)
- `--idle-threshold=<seconds|hh:mm:ss|Ns|Nm|Nh>` (default `60s`)
- `--jiggle-pixels=1-100` (default `10`, only for `--mode=mouse`)
- `--actions=1-10` (default `1`)

Press Ctrl+C to stop. The app restores normal Windows sleep behavior on exit.

### Notes
- In some enterprise environments, simulated input may be blocked or ignored. If so, prefer `--mode=es` and lower `--interval`.
- Actions are only performed when your real idle time exceeds `--idle-threshold`.
- Larger jiggle values (`--jiggle-pixels`) move the cursor more; choose what suits you. 