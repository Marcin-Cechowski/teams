# AntiAway (C# .NET 8 console)

A tiny Windows console app to help prevent Microsoft Teams from going "Away" by:
- Keeping the system/display awake (Windows Execution State)
- Optionally simulating minimal user input (subtle mouse jiggle or SHIFT key tap)

## Build

From a Developer PowerShell or PowerShell in the repo root:

```bash
cd AntiAway
dotnet build -c Release
```

The executable will be at `AntiAway\bin\Release\net8.0-windows\AntiAway.exe`.

## Run

Run with defaults (execution state only, 2-minute interval):

```bash
AntiAway.exe
```

Recommended if Teams marks idle only on no-input: add a subtle input every 2 minutes:

```bash
AntiAway.exe --mode=mouse --interval=2m --jiggle-pixels=2
```

Or use a quick SHIFT key tap:

```bash
AntiAway.exe --mode=key --interval=2m
```

### Options
- `--mode=es|mouse|key`
- `--interval=<seconds|hh:mm:ss|Ns|Nm|Nh>`
- `--jiggle-pixels=1-20` (only for `--mode=mouse`)

Press Ctrl+C to stop. The app will restore normal Windows sleep behavior on exit. 