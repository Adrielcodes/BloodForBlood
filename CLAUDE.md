# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Unity multiplayer game "Blood for Blood". Unity 6000.6.0f1 (6.6 LTS), Universal Render Pipeline, new Input System (exclusive — see Gotchas), Netcode for GameObjects for multiplayer.

Full design doc: `docs/GDD.md`. Key facts that shape architecture:
- **4v1 asymmetrical horror**: one Killer, 3-4 Survivors — these are fundamentally different roles (movement, abilities, win conditions), not the same player class with a skin swap. See "Player" below for the current role-split implementation (movement/role-assignment only — no combat yet).
- **Stagger & Finisher** is the signature mechanic: Survivors parry Killer attacks (skill-based, not free hits) to fill a Stagger Meter; a full meter opens a timed vulnerable window where 2+ nearby Survivors can trigger a Finisher sequence. Killing the Killer takes multiple Stagger/Finisher cycles plus a rare item — never a single cycle.
- MVP target: 1 map, 1 Killer with 1 power, 3 Survivors, 2 weapon types, the chase → stagger → finisher loop working end-to-end. No progression/cosmetics yet.

## Commands

Unity projects are primarily driven through the Editor GUI, not a CLI build/test pipeline. There is no lint/test command set up yet (no `.asmdef` or test assemblies exist in the project).

- **Open the project**: via Unity Hub, or from the command line:
  `"C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe" -projectPath "D:\Coding Projects\BloodForBlood"`
- **Regenerate network scaffolding**: in the Editor menu, `Blood For Blood > Setup Network Scaffolding` (implemented in `Assets/Editor/NetworkScaffoldSetup.cs`). Safe to re-run: creates the `Ground` plane and `NetworkManager` only if missing, always regenerates `Assets/Prefabs/Survivor.prefab` / `Killer.prefab` and `Assets/Prefabs/NetworkPrefabsList.asset` in place (GUIDs/references preserved), and (re-)configures the `NetworkManager`'s prefab list, `ConnectionApproval`, and `RoleAssignmentManager` even if a `NetworkManager` already existed from a prior run.
- **Multi-client local testing**: uses ParrelSync (`com.veriorpies.parrelsync`, installed via git URL, not the Unity Registry). `ParrelSync > Clones Manager` in the Editor menu creates a linked sibling project folder for running a second Editor instance without a full build.
  - **ParrelSync clones symlink `Assets`/`Packages`/`ProjectSettings` with the primary project** — scene/prefab/script changes are shared automatically. Only run `Blood For Blood > Setup Network Scaffolding` in **one** Editor window (the primary). Running it in both is a race: a clone that hasn't yet recompiled the latest scripts can execute a stale cached version of the tool, and two Editor processes independently mutating the same shared scene file can produce duplicate/inconsistent state (this caused the `NetworkPrefabsList` duplicate-registration bug above). The clone will pick up scaffold changes on its own once it regains focus/recompiles.

## Git / asset handling

- Standard Unity `.gitignore` and `.gitattributes` (from the `github/gitignore` and `gitattributes/gitattributes` templates). `Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/` are ignored and regenerate locally — never commit them.
- Git LFS is active (`git-lfs` installed, confirmed working — see `.gitattributes` for the full list of LFS-tracked binary extensions: models, audio, `.unitypackage`, etc.). Anyone cloning this repo needs `git-lfs` installed before checkout.
- `.meta` files must always be committed alongside the asset they describe — Unity uses them to track stable GUIDs across renames/moves.

## Architecture

### Networking model

- Client-server topology (not NGO's distributed-authority mode). `NetworkManager` lives in `Assets/Scenes/SampleScene.unity`, using `UnityTransport` hardcoded to `127.0.0.1:7777` — this is loopback-only for local dev; swapping in Unity Relay/Lobby (or a real server address) is a prerequisite for any non-LAN play.
- **Ownership is deliberately client-authoritative for transforms**, which is a departure from NGO's default. `Unity.Netcode.Components.NetworkTransform` is server-authoritative out of the box; this project uses `Assets/Scripts/Network/OwnerNetworkTransform.cs`, which overrides `OnIsServerAuthoritative()` to return `false` so the owning client drives its own position/rotation. Without this override, only the host's own player would visibly move — non-host clients would have their local movement overwritten every network tick by the stale server-authoritative state. New networked components should default to this same owner-authoritative pattern for anything the local player directly controls (movement, aiming), but should default back to server-authoritative for anything gameplay-critical/anti-cheat-sensitive (health, damage, hit resolution) — that split hasn't been implemented yet since combat doesn't exist.
- `NetworkBootstrapUI.cs` (on the `NetworkManager` GameObject) is a bare-bones `OnGUI` Host/Client/Server button panel for local dev testing only — not shipped UI.
- **Role assignment** (`Assets/Scripts/Network/RoleAssignmentManager.cs`, also on the `NetworkManager` GameObject) uses `NetworkManager.ConnectionApprovalCallback` (requires `NetworkConfig.ConnectionApproval = true`, set by the scaffold script). The host is routed through the same callback as a synthesized approval request, so "first approved connection = Killer, every subsequent one = Survivor" needs no host special-casing. The Killer prefab is selected per-client via `ConnectionApprovalResponse.PlayerPrefabHash` (obtained from `new NetworkPrefab { Prefab = killerPrefab }.SourcePrefabGlobalObjectIdHash` — `NetworkObject.GlobalObjectIdHash` itself is `internal`, this is the supported public route to it); Survivor is the implicit fallback via `NetworkConfig.PlayerPrefab`. **Do not create an explicit `NetworkPrefabsList` asset for these** — NGO auto-registers every `NetworkObject`-bearing prefab into the project-wide `Assets/DefaultNetworkPrefabs.asset` (`IsDefault: true`), which is already auto-wired into every `NetworkManager`. Adding a second list containing the same prefabs causes a hard "duplicate GlobalObjectIdHash" error at `NetworkManager.Awake()` (hit and fixed once already — see git history around the role-system commit). No lobby/cap logic yet — a 5th+ connecting client still gets a Survivor prefab with no rejection.

### Player

- Shared movement lives in the abstract base `Assets/Scripts/Player/NetworkedCharacterMotor.cs` (`CharacterController` + gravity + WASD-via-`Keyboard.current` + facing rotation — the owner-gate and input handling are unchanged from the original scaffold). Role-specific behavior is a single `protected abstract float GetCurrentMoveSpeed(bool hasMoveInput, bool sprintHeld)` hook, plus a server-written `NetworkVariable<PlayerRole> Role` (`Assets/Scripts/Player/PlayerRole.cs`) for other systems to query a player's role network-wide.
- `Assets/Scripts/Player/SurvivorController.cs`: stamina-gated sprint (`Shift` + moving + `Stamina.Value > 0` → `sprintSpeed`, draining stamina; otherwise `walkSpeed`, regenerating). `Stamina` is an owner-written `NetworkVariable<float>` (matches the owner-authoritative convention — only the owner's `Update()` ever runs, per the base class's `IsOwner` gate).
- `Assets/Scripts/Player/KillerController.cs`: flat `killerMoveSpeed` (tuned above Survivor's `sprintSpeed`), no stamina.
- Prefabs: `Assets/Prefabs/Survivor.prefab` / `Killer.prefab`, same root structure (`CharacterController` + `NetworkObject` + `OwnerNetworkTransform` + role controller + child `Visual` capsule) — Killer's `Visual` is scaled ~1.2x as a free, no-art visual differentiator for dev testing.

## Gotchas

- **Active Input Handling is set to "Input System Package (New)" only** (`activeInputHandler: 1` in `ProjectSettings/ProjectSettings.asset`) — the legacy `UnityEngine.Input` API (e.g. `Input.GetAxis`) will throw at runtime. Always use `UnityEngine.InputSystem` APIs.
- Editing scene/prefab files by hand (raw YAML) while the Editor has the project open risks desyncing from the Editor's in-memory state or getting silently overwritten. Prefer adding new script files (safe — Unity hot-compiles these) plus an Editor menu command using Unity's own APIs (`PrefabUtility`, `EditorSceneManager`, etc.) to construct/modify scene and prefab content, following the pattern in `NetworkScaffoldSetup.cs`.
