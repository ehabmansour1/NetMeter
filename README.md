# NetMeter

A tiny Windows 11 taskbar meter that shows live **upload ↑** and **download ↓** speed next to the clock.
Hover over it to see how much data you used **today** and **this month**.

- Speed updates every second (physical adapters only; Hyper-V / VPN / virtual adapters are skipped).
- Today / month totals come from Windows' own usage data — the same numbers as
  *Settings → Network & internet → Data usage*.
- Follows the Windows light / dark theme; hides while a full-screen app is in front.
- Right-click the meter for **Refresh usage** and **Exit**.
- ~20 KB, no install, no dependencies (.NET Framework 4.8, built into Windows).

## Install

1. Download `NetMeter.exe` from the [latest release](../../releases/latest) and put it in a folder you keep, e.g. `C:\Users\<you>\NetMeter\`.
2. Run it. (Until signed releases are available, Windows SmartScreen may ask you to confirm — *More info → Run anyway*.)
3. To start it with Windows, press `Win + R`, type `shell:startup`, and put a shortcut to `NetMeter.exe` in that folder.

To remove it: right-click the meter → **Exit**, then delete the exe and the startup shortcut.

## Build from source

Uses the C# compiler that ships with Windows — nothing to install:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Release builds are made by GitHub Actions (`.github/workflows/build.yml`) from the tagged source.

For testing: `NetMeter.exe --dump usage.txt` writes today's and this month's usage to a file, and
`NetMeter.exe --card card.png` saves the hover card as an image.

## Privacy

NetMeter does not send any data anywhere. It only reads Windows' local network counters and data-usage records, and this program will not transfer any information to other networked systems unless specifically requested by the user. It stores nothing on disk.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

- Committers and reviewers: [ehabmansour1](https://github.com/ehabmansour1)
- Approvers: [ehabmansour1](https://github.com/ehabmansour1)

Only release builds produced by this repository's GitHub Actions workflow from tagged source are signed, and each signing request is approved manually.

## License

[MIT](LICENSE)

## Notes

- Windows 11 doesn't let apps embed into the taskbar, so NetMeter is a small always-on-top window placed just left of the tray icons.
- Units are binary (1 GB = 1024³ bytes), like Windows Settings. Your ISP may count 1 GB = 10⁹ bytes, which reads ~7% higher.
