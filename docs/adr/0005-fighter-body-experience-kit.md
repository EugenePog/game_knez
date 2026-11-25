# 0005. A fighter is a body, an experience level and a kit

Status: Accepted (2026-11-25, built in M3 step 9)
Date: 2026-11-25

## Context
The game design decided (D-022, 2026-11-24) that experience and weapon are two independent axes of a fighter:
experience buffs the weapon he fights with and makes him harder to hit; the weapon sets attack speed, reach and
penetration; one squad may carry different weapons. Militia men each have their own experience, morale and kit (D-018),
and professional and elite exist only for Retainers (D-019).

The content built in M3 step 1 does the opposite. A fighter def (`fighters.json`: `warrior`, `spearman`, `levy`,
`veteran`) bundles a body, a kit, an experience level and a *warrior type* — a third axis whose only real use is the
spearman's +30 ‰ defence. A squad spawns `count` copies of one fighter def, so a squad can't mix kits, and every new
combination of kit and experience needs a new type. Scenarios, goldens and the session files Unity writes all name
these types, so the format gets harder to change with every scenario written.

## Decision
- A fighter is built from three independent defs: a **body** (`BodyDef`, renamed from `FighterDef`, in
  `content/defs/bodies.json`: radius, speeds, turn rate), an **experience level** (`ExperienceDef`, unchanged) and a
  **kit** (`KitDef`, new, `combat.json` `kits[]`: weapon, shield, body armour, head cover — all four required).
- Kits are named by everything they hold (`axe-shield`, `sword-shield-mail` — the shield in every name, so none reads
  as shieldless), not by who carries them. Seven in phase 1 (battle-sim.md §7).
- `warriorTypes` and `defaultKit` are removed. The spearman's defence becomes the spear's own `defence`; weapons gain
  `penetration`. `mounted` comes back with riders, as a body or a weapon, whichever riders need.
- A scenario squad names one `body` and lists its men in groups, `men[]: { count, experience, kit }`; slots are
  filled in that order, front rank first. Retainers are listed apart as named people (battle-sim.md §13).
- `Fighter` holds `Body`, `Experience` and `Kit`; the state hash names all three by id.
- Today's four fighter types convert 1:1 (battle-sim.md §7), so with penetration 0 every battle measures as the
  reference pinned in M3 step 7 before anything is retuned. (Checked when built: every content scenario, 60 seeds, a
  minute each, identical man by man; then penetration — axe 300, spear 200, seax 100, sword 0 — re-pinned the four
  battles where axes or spears meet armour.)

The full field list is battle-sim.md §7, "A fighter is a body, an experience level and a kit".

## Consequences
- Mixed squads and militia with uneven kit (D-018) need no new defs; balance work tunes kits, weapons and experience
  separately (D-026: every balance number in content).
- Every scenario, the content-trace goldens, the `ContentGoldenTests` fixture and code-built test scenarios change in
  one step. Golden traces must be re-recorded, after checking that the converted content reproduces the old traces
  with the hash change left out.
- Session files written before step 9 no longer load. They are debugging aids, not saves; the step says so in its
  commit.
- The content reader gains a kit catalogue and per-group validation (an unknown kit or experience is an error located
  at `squads[i].men[j].kit`).
- The frames format keeps version 1: the static fighter table's `def` holds the body id, and `experience` and `kit`
  are added keys.

## Alternatives considered
- **Keep fighter types, add more of them** (`militia-spear-new`, `militia-spear-experienced`, …): no code change, but
  the catalogue multiplies with every kit × experience pair and still can't express "this man has a helm, that one
  doesn't" — the thing D-018 asks for.
- **Per-man fields in the scenario** (each man listed with weapon, shield, armour, helm and experience): the most
  flexible, but a 16-man battle becomes 16 near-identical entries and nothing names a loadout for balance tables.
  Named kits plus groups give the same freedom with short files.
- **Keep the name `FighterDef` for the body:** less churn, but "fighter def" reads as "fighter type" — exactly the
  confusion D-022 removes — and every scenario is rewritten in this step anyway.
