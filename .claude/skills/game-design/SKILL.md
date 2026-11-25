---
name: game-design
description: Game-design work on Game_knez without writing code — developing the concept, any game system (economy, resources, fighters and kits, Retainers (heroes), morale rules, relationships, events, global map, campaign, progression, balance targets), design documents in docs/design/, the design decision log and syncing the live concept doc. Use whenever the user wants to think through, decide, write down or review how part of the game works, or asks "how should X work", "what resources/units do we need", "is this balanced". Specialised playbooks economy-design, unit-design and balance-design build on this one.
---

# Game design

Design sessions decide **how the game works for the player** and write it down. They don't change code or data.

## Boundaries of a design session

- **Write only:** `docs/design/**`, the concept snapshot `docs/concept/concept-v2.md` (only when refreshing it from the
  live doc), memory files. Throwaway models and research notes go to `artifacts/design/` (gitignored).
- **Never edit** `sim/`, `game/`, `content/`, `tools/`, `.github/`. A number that should change in `content/defs/*.json`
  becomes a *change proposal* in the design doc; a coding session applies it (it changes golden hashes).
- **Running** the battle CLI to look at the current game is fine — through the `battle-analyst` agent or the
  `battle-run` playbook. That is observation, not coding.
- **Designing beyond phase 1 is allowed** (economy, campaign, map): it is concept work. It is *not* a licence to build —
  AGENTS.md phase gating still holds. Every design doc says in which phase it gets implemented.

## Sources of truth (read before deciding)

1. Live concept: Claude Docs «Game_knez — Концепция v2» (https://claude.ai/artifact/5L55AzFUYe6c72fx1Gwvfj); read it
   with the docs tools (load the `anthropic-skills:docs` skill first). Snapshot: `docs/concept/concept-v2.md`.
2. Design docs: `docs/design/README.md` (pillars, index, status), one file per system, `docs/design/decisions.md`
   (decision log), `docs/design/open-questions.md` (open questions backlog).
3. What the battle actually does today: `docs/battle-sim.md` (§7 defs, §11 combat, §12 morale, §13 Retainers, §14 enemy AI — parts marked Planned are contracts, not yet built),
   `content/defs/*.json` (their `description` fields explain each number), `docs/roadmap.md`.
4. Glossary of terms → code names: AGENTS.md.

If the snapshot and the live doc disagree, the live doc wins; offer to refresh the snapshot.

## Who decides

**The user decides; you propose.** Numbers that define feel, new mechanics, cuts and scope are the user's call.
For every open point give 2–3 real options with trade-offs and *one recommendation with a reason*. Don't write a
proposal into a doc as if it were decided. Ask questions in small batches (≤ 3), most consequential first.

## Workflow

1. **Frame.** One paragraph: what player problem the system solves, which pillar it serves, which phase it ships in.
   If it serves no pillar, say so — it may be a cut.
2. **Read** the sources above for that system and its neighbours. List what is already decided (quote it).
3. **Explore.** Options with trade-offs; references (how Legal Crime, Bannerlord, JA2, Kingmaker, Norland, Battle
   Brothers… solved it) — the `design-researcher` agent for anything you aren't sure of; history for flavour.
4. **Decide with the user.** Record each decision in `docs/design/decisions.md` (format there).
5. **Write** the system doc from the template below; update the index in `docs/design/README.md`.
6. **Check.** Run the `design-critic` agent on any new or substantially changed doc; for numbers, the
   `economy-modeler` (economy, progression) or `battle-analyst` (battle) agent. Bring findings to the user; fix what
   they accept.
7. **Propagate.** If the decision changes the concept, propose the exact edit to the live concept doc and make it only
   on the user's yes, then refresh the snapshot. If it creates work for the current milestone, write a short
   *handoff* section (what changes in which def/spec section) — a coding session picks it up. Out-of-phase systems
   get no handoff.

## Design principles (from the concept; apply them as tests)

- **Pillars** are listed in `docs/design/README.md`. A mechanic that weakens one needs a strong reason.
- **Decisions, not counters.** Every resource, stat or building must change a player decision. If nothing would be
  decided differently without it, cut it. Homestead rule: 5–7 consequential decisions per return, not 30 buildings.
- **Consequences over bookkeeping.** Prefer events and lasting map changes to meters the player tops up.
- **Combat feeds the world, the world feeds combat.** Each economy/relationship system should generate battles or
  change how battles go (morale, losses, who is in the squad), and each battle result should matter outside it.
- **Pressure with an answer.** Every negative event or squeeze has at least one player response (Norland lesson).
- **Readable in a paused 2D view.** A battle rule the player can't see or predict at pause is a bad rule.
- **Solo-dev budget.** Prefer systems that reuse existing sim state and content; note content cost (how many events,
  units, maps the design needs).
- **Setting rules** (AGENTS.md): fictional names in Old Norse / Baltic / Slavic style, no modern ethnonyms or symbols,
  every people with respect, no real magic — the sorcerer works through belief and fear (morale).

## System doc template (`docs/design/<system>.md`, in Russian)

```markdown
# <Система>

- Статус: черновик | на обсуждении | принято | отложено
- Фаза реализации: 1 (схватка) | 2 (3D-схватка) | 3 (кампания EA) | позже
- Столпы: <какие столпы обслуживает>
- Связи: концепция §<…>, <другие доки>, battle-sim §<…>

## Зачем
Какую проблему игрока решает, какое чувство создаёт.

## Решения игрока
| Решение | Когда / как часто | Цена и риск | Что меняет |
|---|---|---|---|

## Правила
Кратко, языком игры, без кода.

## Числа
| Параметр | Значение | Статус (предложение / принято D-NNN) | Почему |
|---|---|---|---|

## Связи с другими системами
## Риски и вырожденные стратегии
## Стоимость производства (контент, события, ИИ)
## Открытые вопросы
## Передача в разработку (только для текущей фазы)
| Файл / раздел | Что меняется | Решение |
|---|---|---|
```

## Visuals

Flows, loops and matrices are easier to agree on as pictures: a quick inline diagram (show_widget) for discussion; a
published artifact only if the user wants to share it. Keep the doc's text self-sufficient — diagrams illustrate it.

## Language

Talk in the user's language (Russian or English, as they write). Design docs in Russian; code names from the AGENTS.md
glossary in backticks. A new term that will become code gets a proposed English name in the doc.
