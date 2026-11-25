---
name: unit-design
description: Design the fighters of Game_knez — roles, kits (weapon, shield, armour, helmet) and experience levels as independent axes (D-022), Retainers (дружинники — the named heroes) with traits, leadership and abilities, militia, enemy kinds and their behaviour (militia, the ruler's Retainers, bandits, the sorcerer's followers), recruitment sources, cost and upkeep, counters and the roster per phase. Use for "what fighters do we need", "how is a spearman different", "what does a Retainer do", enemy roster or recruitment questions. Builds on game-design; battle numbers are measured with the battle-analyst agent.
---

# Unit design

Follow `game-design` for boundaries, workflow and the doc template. System docs: `docs/design/units.md` (experience
and kit), `docs/design/heroes.md` (Retainers: traits, abilities, leadership, recruitment), `docs/design/enemies.md`
(enemy behaviour), `docs/design/balance.md`. Decisions so far: D-018 (militia), D-019 (Retainers), D-022 (no fighter
types: experience and kit apart), D-023/D-025 (abilities, six traits), D-024 (enemy behaviour).

## Ground truth: what the sim models today

Read these before proposing — a design the sim can't express is a sim feature request, not a content change:

- `content/defs/bodies.json` — bodies only (one, `man`). There are no fighter types (D-022, ADR-0005, M3 step 9):
  a scenario's squad lists its men as groups of `count` + `experience` + named `kit` — battle-sim.md §7. Design in
  those terms.
- `content/defs/combat.json` — weapons (reach, windup, recovery, wound shares, `penetration`: axe 300, spear 200, seax
  100, sword 0; `defence`: the spear's 30), shields, body armour, head cover, experience levels (incl. starting
  morale), the seven named `kits` (each with a round shield), hit chances, position (front/flank/rear).
- `content/defs/morale.json`, `content/defs/formations.json`.
- `docs/battle-sim.md` §11 combat (no hit points: a blow that gets through wounds — Light / Heavy / Down),
  §12 morale (and the planned standoff), §13 Retainers (planned, incl. the duel), §14 enemy AI (planned).

Each def file's `description` says which numbers are agreed and which are placeholders.

## The two layers of a unit

| Layer | Fields | Where it lives |
|---|---|---|
| Battle | kit, experience, body (speeds), formation fit, morale; for Retainers trait, leadership, ability | content defs (sim) |
| Campaign | where recruited, hire cost, upkeep, healing time, a Retainer's traits on independent tasks, culture flavour | design docs (campaign MVP) |

Design both, mark which ships when. In phase 1 only the battle layer exists.

## Method

1. **Roles before kits.** Define what jobs a 2–3-squad force needs (hold a line, break a line, protect a flank,
   chase routers, screen, deal with a Retainer). A kit exists only if it fills a role in a way that changes the
   player's orders — which formation, where to place it, when to commit it. Experience is a separate axis: how well a
   man fights with whatever he carries (D-022). A squad may mix kits.
2. **Roster matrix** (one row per kit; experience as columns or a note — not one row per kit × experience pair):

   | Kit | Role | Weapon / shield / armour / helm | Who carries it (militia, Retainer) | Strong vs | Weak vs | Best formation | Order it wants | Cost / upkeep | Phase |
   |---|---|---|---|---|---|---|---|---|---|

3. **Counters.** A matchup matrix (kit × kit → expected edge and why: reach, armour, penetration, morale, flank). No
   kit is best at everything; position and morale must matter more than the stat line (morale is the central system).
4. **Readability.** At pause in the 2D view the player must tell kits and experience apart and predict the outcome
   roughly. Few distinct kits (phase 1: 3–5 for the player side) beat many similar ones.
5. **Retainers.** A Retainer is a fighter plus a trait, passive leadership (stronger with experience) and one active
   ability from the trait (D-023, D-025). Each ability: what decision it creates, its cooldown in seconds, whom it
   affects, what the player sees, and that it works in the standoff too. Leadership should make *which squad* a
   Retainer stands in a real choice. Personal story hooks go to the relationships/narrative docs.
6. **Enemies.** Behaviour comes from two layers (D-024): *who it is* (militia defends its homes and breaks early; the
   ruler's Retainers hold formation, pursue and challenge; bandits ambush for loot and leave when it's taken) and *the
   leader's trait*. Later: the sorcerer's followers carry Fear (morale), fanatics hold longer; the prince's men are
   disciplined and well armed.
7. **Culture flavour.** The warband mixes Slavic, Norse, Baltic and Finnic people. Name types by role and kit, not by
   people; culture shows in names, kit looks and small traits, never as "people X is worse". Fictional names only.
8. **Value scale.** Give every kit and experience level a rough *power* and *cost* so economy and balance share one
   scale (e.g. a new militiaman with axe and shield = 1, an experienced man in a padded coat = 2, a professional
   Retainer in mail = 4). `balance-design` checks that power matches the battle results.

## Handoff

A kit or experience change that fits the defs → a handoff table (file, id, fields, values) for a coding session.
Something that needs a new mechanic (ranged weapons, mounted, throwing, a new ability) → a short sim feature request
listing the rule, the state it needs and the spec section it extends; the user decides when it enters the roadmap.
