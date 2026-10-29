<p align="center">
  <img src="Minecraft_NeoForge.cs/Minecraft_NeoForge.png" alt="NeoForge" width="128">
</p>

<h1 align="center">WindowsGSM.Minecraft_NeoForge</h1>

<p align="center">
  MeFriendos build for running a Minecraft NeoForge server with WindowsGSM, without a bundled modpack.
</p>

<p align="center">
  <a href="https://github.com/PapaGordon/WindowsGSM/releases/latest"><img src="https://img.shields.io/badge/WindowsGSM-MeFriendos%20latest-38CDD4" alt="Latest MeFriendos WindowsGSM"></a>
  <a href="CHANGELOG.md"><img src="https://img.shields.io/badge/version-0.1.0-D77D42" alt="Version 0.1.0"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT License"></a>
</p>

This plugin uses the same installation, version selection and update logic as the NeoForge server built into MeFriendos WindowsGSM. It includes its own runtime helpers and does not require our fork or the ATM10 plugin. Other WindowsGSM hosts still need the normal plugin APIs listed below.

If you use [MeFriendos WindowsGSM](https://github.com/PapaGordon/WindowsGSM/releases/latest), select the built-in **Minecraft: NeoForge** entry instead. Keep **Minecraft: All the Mods 10 (ATM10)** for ATM10 packs. This standalone plugin is for a plain NeoForge server; add your own compatible mods if wanted.

## Features

- Downloads the official NeoForge installer and checks its SHA-256 before running it.
- Uses the matching 64-bit Java installation for the selected NeoForge family.
- Supports exact version pins or stable updates within one Minecraft family.
- Starts Java directly with the selected `win_args.txt` and existing `user_jvm_args.txt`.
- Supports the WindowsGSM embedded console and the Minecraft `stop` command.
- Applies WGSM's game port, query port and player limit while preserving an existing MOTD.
- Prepares updates separately and retains the complete previous server directory as a backup.
- Keeps worlds, mods, configuration, player lists and custom JVM arguments during updates.
- Includes the official NeoForge fox icon and its artwork credits.

## Quick overview

| Setting | Value |
| --- | --- |
| Server entry | Minecraft: NeoForge |
| Installer source | Official NeoForged Maven |
| Default version family | `21.1` for Minecraft 1.21.1 |
| Default game/query port | `25565` |
| Default player limit | `20` |
| Query method | Minecraft UDP query (UT3) |
| Memory settings | `user_jvm_args.txt` |
| SteamCMD login | Not used |
| Firewall | Host-managed Auto Firewall or manual game/query rules |

## Requirements

- A compatible WindowsGSM build on 64-bit Windows. [Current MeFriendos build](https://github.com/PapaGordon/WindowsGSM/releases/latest).
- The host APIs `ServerConfig`, `ServerPath.GetServersServerFiles`, `ServerConsole`, `UI.CreateYesNoPromptV1` and `Query.UT3`, including the usual console and server-setting fields.
- Matching 64-bit Java: Java 17 for NeoForge 20.2-20.4, Java 21 for 20.5-21.x, Java 25 for 26.x.
- Internet access to the official NeoForged Maven and the runtime dependencies used by its installer.

The plugin is compiled as C# 6 against a minimal host API fixture and checked against the real MeFriendos WGSM assembly. That does not prove compatibility with every historical fork. It does not call MeFriendos-only credential or firewall APIs, install Java, or require an ATM10 server pack.

## Plugin installation

1. Download the [latest release archive](https://github.com/PapaGordon/WindowsGSM.Minecraft_NeoForge/releases/latest).
2. Extract the complete `Minecraft_NeoForge.cs` folder into `<WindowsGSM>\plugins\`.
3. Click **Reload Plugins** or restart WindowsGSM.
4. Add **Minecraft: NeoForge [Minecraft_NeoForge.cs]** and click **Install**.
5. Accept the Minecraft EULA if you agree to it. WGSM downloads and runs the official installer.
6. Adjust RAM in `serverfiles\user_jvm_args.txt`, configure your ports and start the server.

Set `JAVA_HOME` to the matching JDK or add its `bin` folder to `PATH`. Common JDK directories are also checked. Additional WGSM start parameters are game arguments, matching ATM10; keep Java options in `user_jvm_args.txt`.

## Versions and updates

The file `serverfiles\neoforge-version.txt` controls version selection. `21.1` means the latest stable NeoForge release within that family. An exact value such as `21.1.265` pins that release. Beta versions require an explicit exact pin. NeoForge version numbers differ from Minecraft version numbers; use the [official version list](https://maven.neoforged.net/releases/net/neoforged/neoforge/) when choosing one.

An imported server keeps its installed family by default. Updates do not jump to another Minecraft family unless you edit the selector. Check mod and world compatibility before changing families.

Stop the server and make a world backup before updating. The plugin prepares a full copy beside `serverfiles`, installs fresh runtime libraries there and activates it only after preparation succeeds. The previous directory stays under `servers\<id>\neoforge-update-backups`. Allow enough free space for another complete server copy. Backups are not removed automatically.

Failed preparation leaves the live server files untouched and keeps the prepared directory for inspection. If activation is interrupted, `.wgsm-neoforge-update-recovery.txt` blocks startup and contains the recovery paths. Check those paths and restore the previous directory if needed before removing that marker.

## Import

An existing server needs `user_jvm_args.txt` and `libraries/net/neoforged/neoforge/<version>/win_args.txt`. If multiple runtimes exist, set `.wgsm-neoforge-version.txt` to the intended installed version first. ATM10 folders with an ATM10 version marker belong to the separate ATM10 plugin.

## Firewall and Steam credentials

This plugin does not create or remove firewall rules. MeFriendos WGSM manages per-server TCP game and UDP query rules for its NeoForge entry. On other hosts, configure those rules yourself if automatic port handling is unavailable. Router forwarding is separate.

NeoForge does not use SteamCMD or a Steam account. Encrypted Steam login data does not affect this plugin. Steam password encryption is a feature of the host and is not added to other forks by installing NeoForge.

## Validation

With .NET 8 and a built WindowsGSM assembly, run `dotnet run --project tests/NeoForgeChecks.csproj`. Set `WindowsGSMDirectory` if your build is elsewhere. `dotnet build tests/ApiCompatibility.csproj` checks the source as C# 6 using minimal standard WGSM API stubs, without our fork or ATM10.

The fixtures cover version selection, Java requirements, import, launch arguments, configuration preservation, failed preparation, activation, backups, locks and recovery. The official installer can be checked separately with `--online-installer <absolute-java.exe-path>`. This downloads dependencies but does not start a Minecraft game server. Live multiplayer and every third-party WGSM fork are not covered by these checks.

## Project links

- Source: [PapaGordon/WindowsGSM.Minecraft_NeoForge](https://github.com/PapaGordon/WindowsGSM.Minecraft_NeoForge)
- WindowsGSM: [Latest MeFriendos release](https://github.com/PapaGordon/WindowsGSM/releases/latest)
- NeoForge: [Server installation](https://docs.neoforged.net/user/docs/server/)
- Community: [mefriendos.de](https://mefriendos.de)

## License

Made by PapaGordon for MeFriendos. Plugin source is released under the [MIT License](LICENSE).

The NeoForge fox artwork is by [Ridanisaurus](https://github.com/Ridanisaurus), copyright the NeoForged team, under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The ICO is unchanged; the PNG is a lossless format conversion for display. See [artwork credits](licenses/NeoForge-art-attribution.txt).

This is an independent community plugin. It is not affiliated with or endorsed by NeoForged, Mojang, Microsoft or WindowsGSM. Minecraft, Java, NeoForge and mods are not redistributed with the plugin.

The published MeFriendos host from v1.25.4.3 also recognizes this standalone filename and hides the duplicate built-in choice. Older hosts can still load the plugin separately; their firewall handling depends on the host.
