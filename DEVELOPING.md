# Developing your own version

This guide is for making your own game from this template: where things are, how to change the look, the sounds and
the rules, how to give it your own name and server, how to build it for Quest and PC, and what to keep the same if you
want to keep playing against Project Arena players. For what the game is, see the [README](README.md).

## What you need

- **Unity 6000.3.21f1** (Unity 6.3 LTS) with **Android Build Support** (OpenJDK, Android SDK and NDK) for Quest.
- A **Meta Quest** in developer mode (Quest 2, 3, 3S or Pro), or a **PC VR headset** with an OpenXR runtime (Meta
  Horizon Link or SteamVR).
- **Python 3.8 or newer**, for the tools and for the tests that run a local server.
- The **native Project Arena repository** ([public mirror](https://github.com/jendoebelin/ProjectArena-public)) next to
  this one, for the server (`server/projectarena_relay.py`) and the open asset set. Point the tools at it with the
  environment variable `PA_NATIVE_REPO` (its folder).

Command-line runs can keep Unity's caches off a full drive: set `UPM_CACHE_ROOT` and `GRADLE_USER_HOME` to folders on
a drive with room.

## The first time

Open the folder in Unity Hub. The project's settings are made by one editor command, which you can run again at any
time:

```
Unity.exe -batchmode -quit -projectPath . -executeMethod ProjectArena.EditorTools.ProjectConfigure.Run -logFile -
```

It sets the player settings (name, app ID, IL2CPP, ARM64, Vulkan, 48 kHz audio), the render pipeline (URP: 4x MSAA,
no HDR, no shadows), the OpenXR features (Quest and PC; everything else is turned off), and **rebuilds the scene**
`Assets/ProjectArena/Scenes/Arena.unity` from the models. If you edit the scene by hand, don't run `Run` again
afterwards, or make your change in `ProjectConfigure.Scene()` so it survives.

## Where things are

| Folder | What is in it |
|---|---|
| `Assets/ProjectArena/Sim` | The rules and the ball physics in plain C#, with no Unity code: `Game*.cs` (the match, scoring, rounds, the arenas), `Bot.cs`, `Training.cs`, `LookAhead.cs`. Keep this compatible to play with Project Arena. |
| `Assets/ProjectArena/Net` | The network protocol (`Net.cs`): rooms, the LAN and the community server. Keep this compatible too. |
| `Assets/ProjectArena/Avatar` | Avatar options, the countries and their kits, presets, the body rig. |
| `Assets/ProjectArena/Runtime` | The game in Unity: headset input (`XrInput.cs`), drawing (`MatchView.cs`, `FxRenderer.cs`), the menus (`Menu.cs`), the lobby, rooms, spectating, the director, voice, sound (`NativeMixer.cs`), TRAINING, the SOUND STUDIO, the PC spectator window (`DesktopSpectator.cs`), and two OpenXR features (`RoomFeelFeature.cs` for FIELD OF VIEW, `PassthroughFeature.cs` for PASSTHROUGH). |
| `Assets/ProjectArena/Shaders` | `Native.shader` (the arena and the players), `Fx.shader` (glows and trails), `Text.shader` (text and panels). |
| `Assets/ProjectArena/Art/Open` | The open art and sound (CC BY 4.0): models (`.mdl`), textures, sounds and `events.txt`, the arena's collision. |
| `Assets/ProjectArena/Editor` | The model importer (`MdlImporter.cs`), the project setup (`ProjectConfigure.cs`), the builds (`Builds.cs`), the Android manifest additions, and `SceneShots.cs` (pictures rendered in the editor, written to `Logs/`). |
| `Assets/ProjectArena/Tests/EditMode` | The tests (see below). |
| `Assets/ProjectArena/SimCompare` | A small player that runs the rules on IL2CPP and writes traces, to check them against the native game. |
| `tools` | `convert_assets.py` (the native open set into Unity files), `check_assets.py` (every asset file must be from the open set or yours), `gen_nations_cs.py` (the countries), `sim_compare` and `cross_play` (the checks against the native game). |

Positions in the code are in the native game's axes (`Sim.Vec3`, metres, Z flipped from Unity's); `NativeSpace` and
`XrInput.ToNative` convert.

## Changing the look

**Models.** The arena and the players are `.mdl` files (the native game's format), imported by
`Editor/MdlImporter.cs` into ordinary Unity prefabs with `Native.shader` materials. The arena's parts sit under
**Arena** in the scene, and `MatchView` finds them by name: `environment` (the stadium), `floor`, `floor_reflect`,
`floor_reflect_env`, `arena` (the glass court), `court_fx`, `court_pulse`, `env_fx`, `stadium_fx` and `court_outline`.
You can replace a part with any Unity model of the same name, or make new `.mdl` files with the native repository's
Blender tools and convert them with `tools/convert_assets.py`. The court's collision is
`Art/Open/Data/arena.col.bytes`: if you change the court's shape, the ball needs a matching collision file, and the
rules change with it (see *Changing the rules*).

**Textures.** PNG files in `Art/Open/Textures`, imported by `OpenTexturePostprocessor.cs` (sRGB or linear, mipmaps and
wrap as the native game uses them).

**Sounds.** WAV files in `Art/Open/Audio`, imported at their own sample rate by `OpenAudioPostprocessor.cs`, and mixed
as the native game mixes them by `NativeMixer.cs` (48 kHz, 24 voices). `events.txt` says which clip each game event
plays (`ShieldBreak shield_break`, one per line). To try new sounds without building anything, use the **SOUND
STUDIO** on the title page: put `.wav` files in the game's `my_sounds` folder (named after a slot, `S12_glass.wav`, to
put them first in that slot), and play the moment they belong to.

**Before you share it,** run `python -I tools/check_assets.py` (it lists any art or sound file that isn't from the
open set) and credit every new asset in `Art/Open/NOTICE.txt` and `LICENSE-ASSETS.md`. Assets from other games or
stores you don't have the rights to share can't go in a public repository. `tools/held_assets.txt` lists the files
kept at an earlier version on purpose: where the native game's newer version is under other terms than CC BY 4.0 (its
shield break, a community member's sound), `convert_assets.py` leaves the open one as it is.

## Changing the rules

The rules live in `Sim`: `Game.cs` and its partial files (the match states, scoring, rounds, the ball, shields,
knuckle guards, strikes, FOUR CORNERS, FIREBALL BLITZ), with the numbers in `Tuning` (`g.tun`: speeds, sizes, times;
MATCH OPTIONS set some of them). The rules don't touch Unity, so the tests run them directly.

**The host runs the match.** Everyone in a room plays by the host's rules, so a change in your version is a change
for everyone in your rooms. If you change the physics or the rules, your game becomes something new: run your own
server (below) rather than mixing it into Project Arena rooms. `Tests/EditMode/SameResultsTests.cs` and
`tools/sim_compare` check that the C# rules give the same results as the native game's, step by step; they will tell
you when a change makes your version incompatible.

## Making your own version

**Name and app ID.** In `Editor/ProjectConfigure.cs`, `Player()` sets `companyName`, `productName`, the application
identifier (`com.projectarena.unity`: use your own, such as `com.yourstudio.yourgame`) and the version. The game's
settings, statistics key and sounds are kept in Unity's `persistentDataPath` (on Quest the app's own folder, on PC a
folder named after the company and product), so your version never shares them with ours. The title page's name is in `Menu.cs` (search for
`PROJECT ARENA`). Please give your version its own name and don't present it as the official Project Arena.

**Your own server.** The community server is the native repository's `server/projectarena_relay.py` (Python 3, no
other packages): run it on any machine with a public address, UDP and TCP on port 47780. Its options include
`--stats` (MY STATS), `--sounds` (the SOUND STUDIO's library: mono 48 kHz 16-bit WAV files named `Event__name.wav`),
`--crowd` (The Crowd's phones) and `--bans`; the protocol is described at the top of the file. A build gets its
default server from the environment variable `PROJECTARENA_SERVER=host:port`, or from a `servers.local.cfg` file with
that line (neither is in git); `Builds.cs` writes it into `Resources/servers_default.txt` at build time. Players can
also type a server in SETTINGS. Without one, the game plays on the LAN and against the bot.

## Building

```
Unity.exe -batchmode -quit -projectPath . -executeMethod ProjectArena.EditorTools.Builds.Quest -logFile -
Unity.exe -batchmode -quit -projectPath . -executeMethod ProjectArena.EditorTools.Builds.Pc -logFile -
```

- **Quest:** `Builds/Quest/ProjectArenaUnity.apk`, a development build signed with Unity's debug key. Install it with
  `adb install -r Builds/Quest/ProjectArenaUnity.apk`. For a release, sign with your own keystore (never commit it:
  `*.keystore` and `*.jks` are in `.gitignore`).
- **PC:** `Builds/PC/ProjectArenaUnity.exe`, for any OpenXR runtime (Meta Horizon Link, SteamVR). Without a headset
  (or started with `--spectate`) it opens the **spectator window** for streamers instead: the live matches with
  camera views, your own cameras, view layers for OBS, a webcam SELFIE and the controls on F1
  (`Runtime/DesktopSpectator.cs`).

## A store build (Meta Horizon Store)

The template has no store code. The native game adds two things to its store builds, and a store version of yours
will need the same; neither has been tried in Unity yet:

- **The ownership check** (Meta's requirement VRC.Quest.Security.1). With Meta's Platform SDK for Unity and your
  app's own App ID from the developer dashboard: at launch, initialize the platform (`Oculus.Platform.Core
  .AsyncInitialize`) and ask whether the player owns the game (`Entitlements.IsUserEntitledToApplication`). Meta asks
  for this within 10 seconds of starting. If the answer is no, the native game leaves any room, closes every page and
  shows a page saying the purchase couldn't be confirmed.
- **UPDATE NOW from the store.** The native game asks the store for the latest version
  (`Application.GetVersion`: the current and latest version codes, the latest name, the download size), downloads it
  (`Application.StartAppDownload`, with `Application.CheckAppDownloadProgress` for the progress), and installs it
  (`Application.InstallAppUpdateAndRelaunch`: the headset closes the game and opens the new version).

Keep both to store builds only (a scripting define, for instance), so builds from GitHub or SideQuest never check.
The store also asks you to justify each Android permission: the template asks for `RECORD_AUDIO` (voice chat, asked
when first needed) and the network permissions (`Editor/AndroidPermissions.cs`), and declares passthrough as an
optional feature.

## Tests

```
Unity.exe -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults Logs/results.xml -logFile Logs/tests.log
```

Add `-testFilter "ProjectArena.Tests.MenuTests;ProjectArena.Tests.GameTests"` for some of them. The tests cover the
rules (the native game's own checks, ported), the bot, rooms, the host handoff, cross-play with a native peer,
statistics, the menus, the avatar and builder, the effects, TRAINING, the SOUND STUDIO, FIELD OF VIEW,
PASSTHROUGH and the PC spectator. Tests with a server start a **private local relay** (the native repository's, on ports 47791-47804 of
this machine) and never touch the community server; they need Python and `PA_NATIVE_REPO`, and are skipped without
them. One long test (the bot against itself for 90 minutes) only runs when asked for (`Category("Long")`).

`Editor/SceneShots.cs` renders pictures of the arena, the menus, the avatars and more into `Logs/`:
`-executeMethod ProjectArena.EditorTools.SceneShots.<Name> -quit`.

## Staying compatible: the protocol's rules

To keep playing in the same rooms as Project Arena players:

- **Don't change a packet's layout.** `Net.cs` sends the native game's packets byte for byte (protocol version 10,
  `kVersion`). A new feature gets a new packet type, which older games ignore, or new fields at the end of a packet
  after the ones it has (as BALL LOOK-AHEAD added to the snapshot), which older games stop reading before.
- **Don't change the rules or the physics** in a version that joins Project Arena rooms (see *Changing the rules*).
- **Avatars travel as their option numbers.** Add new options after the existing ones and never renumber them: other
  games read your avatar by those numbers.
- **Say who you are.** Each game tells the server its version in its hello; keep the version string meaningful for
  your build.

## Help

[Discord](https://discord.gg/KuNkSbVHzh) · vrsportsfan@gmail.com
