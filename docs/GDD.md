# BLOOD FOR BLOOD
### Game Design Doc — v0.2

**Lead Game Dev:** Adriel Ramirez

---

## What This Is

**Blood for Blood** is a 4v1 asymmetrical multiplayer horror game. One player is the Killer, hunting three to four Survivors across a map. Unlike most games in the genre (Dead by Daylight, Texas Chain Saw Massacre), Survivors aren't purely evasive — they can fight back, stagger the Killer, and ultimately kill them if they play well and coordinate. The core fantasy: the hunted can become the hunter, but it has to be earned, not free.

---

## Core Loop

Survivors spawn scattered, scavenge weapons and tools, and work objectives. The Killer hunts using a unique power per character. When a chase starts, Survivors can run, hide, or fight. Landing hits during a fight builds a Stagger Meter on the Killer; a full meter opens a Finisher Window where coordinated Survivors can attempt to kill the Killer outright. Survivors win by completing objectives and escaping, or by killing the Killer. The Killer wins by downing/killing enough Survivors first.

---

## Roles

**Survivors (3-4 players)** — Stamina pool fuels sprinting, vaulting, and fighting. Start unarmed, scavenge weapons (durability-limited) and utility items (medkits, flashbangs, noisemakers).

**Killer (1 player)** — Faster than a sprinting Survivor, no stamina cost, but has a Terror Radius that telegraphs proximity. One unique power per Killer character. Has a Stagger Meter instead of a health bar.

---

## Signature Mechanic: Stagger & Finisher

- Survivors land hits by parrying Killer attack windups — skill-based, not a free hit.
- A full Stagger Meter drops the Killer into a vulnerable state for a few seconds.
- During Stagger, 2+ nearby Survivors can trigger a Finisher — a short coordinated sequence.
- The Killer is never one-shot: killing them takes multiple Stagger/Finisher cycles across a match, plus a rare item required for the final Finisher.
- This keeps a Killer kill rare and match-defining instead of routine, so the Killer role stays scary and fun to play even when losing.

---

## Objectives

- **Standard objective** — safe, steady progress toward the escape win condition.
- **Aggressive objective** — faster progress, but draws you into Killer proximity and feeds the Stagger meter. Gives chase-loving Survivors a way to actively engage instead of just evading.

---

## Progression & Cosmetics

- Seasonal battle pass, cosmetics only — skins, finisher animations, weapon skins. No power-affecting unlocks.
- Per-character mastery tracks unlock cosmetic flair, not power.
- Target pacing: a full pass completable in ~40-60 hours per season.
- No expiring currency, no aggressive FOMO — long-term goodwill over short-term pressure.

---

## Tech Stack (optimized for AI-assisted development)

The priority here is picking tools with the largest training corpus, best documentation, and text-based (not node-graph) workflows, since that's where AI coding assistance is strongest and most reliable.

| Layer | Choice | Why |
|---|---|---|
| Engine | **Unity (C#)** | C# is one of the most AI-fluent languages; Unity has massive documentation and community code AI models are trained on. Avoids Unreal Blueprints, which are visual node graphs AI tools can't read/write well. |
| Networking | **Netcode for GameObjects** (or Mirror as fallback) | Official Unity stack, well-documented, text-based C# — AI can generate and debug netcode directly instead of fighting a black-box graph editor. |
| Backend/matchmaking | **Unity Gaming Services (Relay + Lobby)** or **PlayFab** | Both have clear C#/REST APIs an AI assistant can call correctly without guesswork. |
| Animation | **Mixamo + Unity Animator** | Fast to source and rig, minimal custom tooling needed early. |
| Version control | **Git + GitHub** | Lets an AI coding assistant (Claude Code, Copilot, Cursor) work directly against the repo, read history, and make targeted commits. |

---

## MVP Scope

- 1 map, 1 Killer with 1 power, 3 Survivors
- 2 weapon types
- Core chase → stagger → finisher loop working and fun
- No progression or cosmetics yet — validate the core fantasy first
