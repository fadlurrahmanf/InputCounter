# Input Counter

Small, always-on-top Windows HUD that counts physical keyboard and mouse input without recording typed content. It lives in the notification area instead of the taskbar.

## Features

- Daily Total, High Total, All Time, current `key/sec`, and `key/sec` high score.
- One physical key-down per press; held keys are counted once until released.
- Idle decay after three seconds, Echo Cascade, Critical Hit, and rare Rewind events.
- A transparent Typing Aura companion with a white Living Core, particle orbits, planets, and bidirectional wind at high typing speed.
- Local-only persistence for scores, Aura diary, and daily planet collection. No key content is recorded or uploaded.
- Left-click drag; right-click the HUD or Core to close.

## Build and run

Requirements: Windows and the .NET Framework C# compiler included with .NET Framework 4.x.

```bat
Build-InputCounter.cmd
Start-InputCounter.cmd
```

The build script creates `InputCounter-compact-aura-overlay.exe`. The executable and all local score data are intentionally ignored by Git.
