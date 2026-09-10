# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Unity multiplayer game "Blood for Blood". Unity 6000.6.0f1 (6.6 LTS), Universal Render Pipeline, new Input System (exclusive — see Gotchas), Netcode for GameObjects for multiplayer.

Full design doc: `docs/GDD.md`. Key facts that shape architecture:
- **4v1 asymmetrical horror**: one Killer, 3-4 Survivors — these are fundamentally different roles (movement, abilities, win conditions), not the same player class with a skin swap. The current `NetworkedPlayerController`/`Player.prefab` is a placeholder shared-movement scaffold from initial netcode setup, not a real Survivor or Killer controller.
- **Stagger & Finisher** is the signature mechanic: Survivors parry Killer attacks (skill-based, not free hits) to fill a Stagger Meter; a full meter opens a timed vulnerable window where 2+ nearby Survivors can trigger a Finisher sequence. Killing the Killer takes multiple Stagger/Finisher cycles plus a rare item — never a single cycle.
- MVP target: 1 map, 1 Killer with 1 power, 3 Survivors, 2 weapon types, the chase → stagger → finisher loop working end-to-end. No progression/cosmetics yet.

## Commands

Unity projects are primarily driven through the Editor GUI, not a CLI build/test pipeline. There is no lint/test command set up yet (no `.asmdef` or test assemblies exist in the project).

- **Open the project**: via Unity Hub, or from the command line:
  `"C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe" -projectPath "D:\Coding Projects\BloodForBlood"`
- **Regenerate network scaffolding**: in the Editor menu, `Blood For Blood > Setup Network Scaffolding` (implemented in `Assets/Editor/NetworkScaffoldSetup.cs`). Adds a `Ground` plane and `NetworkManager` to the active scene if not already present, and always regenerates `Assets/Prefabs/Player.prefab` at the same path (safe to re-run — overwrites the prefab in place, preserving its GUID/references).
- **Multi-client local testing**: uses ParrelSync (`com.veriorpies.parrelsync`, installed via git URL, not the Unity Registry). `ParrelSync > Clones Manager` in the Editor menu creates a linked sibling project folder for running a second Editor instance without a full build.

## Git / asset handling

- Standard Unity `.gitignore` and `.gitattributes` (from the `github/gitignore` and `gitattributes/gitattributes` templates). `Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/` are ignored and regenerate locally — never commit them.
- Git LFS is active (`git-lfs` installed, confirmed working — see `.gitattributes` for the full list of LFS-tracked binary extensions: models, audio, `.unitypackage`, etc.). Anyone cloning this repo needs `git-lfs` installed before checkout.
- `.meta` files must always be committed alongside the asset they describe — Unity uses them to track stable GUIDs across renames/moves.

## Architecture

### Networking model

- Client-server topology (not NGO's distributed-authority mode). `NetworkManager` lives in `Assets/Scenes/SampleScene.unity`, using `UnityTransport` hardcoded to `127.0.0.1:7777` — this is loopback-only for local dev; swapping in Unity Relay/Lobby (or a real server address) is a prerequisite for any non-LAN play.
- **Ownership is deliberately client-authoritative for transforms**, which is a departure from NGO's default. `Unity.Netcode.Components.NetworkTransform` is server-authoritative out of the box; this project uses `Assets/Scripts/Network/OwnerNetworkTransform.cs`, which overrides `OnIsServerAuthoritative()` to return `false` so the owning client drives its own position/rotation. Without this override, only the host's own player would visibly move — non-host clients would have their local movement overwritten every network tick by the stale server-authoritative state. New networked components should default to this same owner-authoritative pattern for anything the local player directly controls (movement, aiming), but should default back to server-authoritative for anything gameplay-critical/anti-cheat-sensitive (health, damage, hit resolution) — that split hasn't been implemented yet since combat doesn't exist.
- `NetworkBootstrapUI.cs` (on the `NetworkManager` GameObject) is a bare-bones `OnGUI` Host/Client/Server button panel for local dev testing only — not shipped UI.

### Player

- `Assets/Prefabs/Player.prefab`: `CharacterController` + `NetworkObject` + `OwnerNetworkTransform` + `NetworkedPlayerController`, plus a child `Visual` capsule (placeholder mesh, no real art yet).
- `NetworkedPlayerController.cs` gates all input handling behind `IsOwner` and reads movement via the new Input System's `Keyboard.current` (WASD), not the legacy `Input` class.

## Gotchas

- **Active Input Handling is set to "Input System Package (New)" only** (`activeInputHandler: 1` in `ProjectSettings/ProjectSettings.asset`) — the legacy `UnityEngine.Input` API (e.g. `Input.GetAxis`) will throw at runtime. Always use `UnityEngine.InputSystem` APIs.
- Editing scene/prefab files by hand (raw YAML) while the Editor has the project open risks desyncing from the Editor's in-memory state or getting silently overwritten. Prefer adding new script files (safe — Unity hot-compiles these) plus an Editor menu command using Unity's own APIs (`PrefabUtility`, `EditorSceneManager`, etc.) to construct/modify scene and prefab content, following the pattern in `NetworkScaffoldSetup.cs`.
