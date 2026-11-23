# Watch Dogs 2 Hybrid CPU Fix

An experimental x64 Windows installer for Microsoft's `ProcessorCountLieForHybridCPU` compatibility fix. It registers a compatibility database for supported Watch Dogs 2 builds. The game files and anti-cheat are untouched.

## Install or uninstall

Download `WatchDogs2-HybridCPU-Setup.exe` from **Releases**. Open the EXE, point it at your Watch Dogs 2 folder, and select **Install fix**. Windows asks for administrator approval. To remove the fix, open the same EXE and select **Uninstall fix**.

The installer is unsigned and uses .NET Framework 4.x and Windows' built-in `sdbinst.exe`. The database currently matches these file sizes.

| Build folder | `WatchDogs2.exe` bytes | `Disrupt_64.dll` bytes |
| --- | ---: | ---: |
| `bin` | 537008 | 348780712 |
| `bin_plus` | 537008 | 427002024 |

Installer matching and compatibility database registration were verified. Whether the fix replaces the CPU affinity workaround in-game is still unverified.

## Build from source

Run the following on x64 Windows with .NET Framework 4.x.

```powershell
.\Build.ps1
```

The installer is written to `dist\WatchDogs2-HybridCPU-Setup.exe`. The build embeds the checked-in database and needs no game installation or Python. The source is in [src](src/), and the database and its generator are in [compatibility](compatibility/).

To regenerate the database, use 64-bit Python on Windows with a Watch Dogs 2 installation containing both supported build folders.

```powershell
python .\compatibility\build-compatibility-db.py "C:\Games\WATCH_DOGS2"
```

Review the generated database's matching criteria before distributing a build for a different game version.

## GitHub releases

The [release workflow](.github/workflows/release.yml) builds on the repository's default branch and publishes the EXE to GitHub Releases every Monday at 10 a.m. Pacific (`America/Los_Angeles`, including daylight saving time). It can also be started manually from **Actions**. Once the repository is on GitHub with Actions enabled, the workflow uses the repository's built-in token. The release notes include the installer's SHA-256.
