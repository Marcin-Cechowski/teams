# AntiAway (C# .NET 8 console)

A tiny Windows console app to help prevent Microsoft Teams from going "Away" by:
- Keeping the system/display awake (Windows Execution State)
- Optionally simulating minimal user input (subtle mouse jiggle or SHIFT key tap)
- Idle-aware: only simulates input if you've been idle longer than a threshold

## Build

From a Developer PowerShell or PowerShell in the repo root:

```bash
cd AntiAway
dotnet build -c Release
```

The executable will be at `AntiAway\bin\Release\net8.0-windows\AntiAway.exe`.

## Run

Run with new defaults (execution state, check every 30s, act if idle >= 60s):

```bash
AntiAway.exe
```

Mouse jiggle mode (relative 1px wiggle, only if idle >= 60s):

```bash
AntiAway.exe --mode=mouse --interval=30s --idle-threshold=60s --jiggle-pixels=1
```

Key tap mode (SHIFT tap, only if idle >= 60s):

```bash
AntiAway.exe --mode=key --interval=30s --idle-threshold=60s
```

### Options
- `--mode=es|mouse|key`
- `--interval=<seconds|hh:mm:ss|Ns|Nm|Nh>` (default `30s`)
- `--idle-threshold=<seconds|hh:mm:ss|Ns|Nm|Nh>` (default `60s`)
- `--jiggle-pixels=1-20` (default `1`, only for `--mode=mouse`)

Press Ctrl+C to stop. The app restores normal Windows sleep behavior on exit.

### Notes
- In some enterprise environments, simulated input may be blocked or ignored. If so, prefer `--mode=es` and lower `--interval`.
- Teams may base presence on app focus/activity. Keeping the Teams window open and not minimized can help.
- If status still flips to Away, try: `--mode=mouse --interval=20s --idle-threshold=45s --jiggle-pixels=2`. 