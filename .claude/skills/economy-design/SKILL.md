---
name: economy-design
description: Design the campaign economy of Game_knez — resources (silver, goods, supplies, influence), tribute and protection ("крыша") over settlements, полюдье, own businesses, the prince's tribute and rights, warband upkeep, recruitment and healing costs, seasonal flows, sinks and faucets, feedback loops, early-game pressure. Use for any question about money, resources, income, costs, prices or how the economy creates missions. Builds on game-design; numbers are checked with the economy-modeler agent.
---

# Economy design

Follow `game-design` for boundaries, workflow and the doc template. This playbook adds the economy method.
System docs: `docs/design/economy.md` (rules, loops), `docs/design/resources.md` (one sheet per resource).

## What is already decided (concept v2 → "Экономика")

Protection, not city-building (Legal Crime model): tribute from others' settlements first, then a few very profitable
own businesses. Settlements are AI-run (population, main trade, wealth, attitude); the player never builds inside them.
Statuses: independent / protected / subjugated / under someone else's protection. Ways in: show of force, service,
treaty (gifts, marriage, sworn brotherhood), conquest. Protection obligation — failing to help costs respect with all
tributaries. Полюдье = collection round with ambush/dispute/refusal risk. Own businesses: portage/ferry toll, trading
post, smithy, saltworks, pier — need a site, an investment and a specialist Retainer. Prince's rights to market and toll.
Costs: warband upkeep, the prince's share, rivals. Resources: silver, goods (fur, salt, iron), supplies, influence
(respect or fear). Out of scope: deep economic simulation, Stronghold-style economy, captives beyond ransom/hire.
Re-read the live doc in case this changed.

## Method

1. **Resource sheet** for each resource before any numbers:

   | Field | Question |
   |---|---|
   | Faucets | Where does it come from, how predictable? |
   | Sinks | What consumes it? Is there always something worth spending on? |
   | Storage | Does it cap, spoil, get stolen, lose value? Hoarding must have a cost or a reason not to. |
   | Conversion | Can it be traded into other resources, at what loss? Free conversion = one resource in disguise. |
   | Visibility | Where the player sees it; what the forecast shows. |
   | Decision | Which choice does it drive? No choice → merge or cut. |

   Budget: **≤ 4 resource kinds** the player tracks; goods may be one bucket with flavour types.

2. **Time unit.** The season is the economic tick (tribute "once a season"). Write down what happens each season, at
   which moment (collection, upkeep, events, the prince's demand) and how long an expedition takes in seasons.

3. **Flow map.** Faucets → stocks → sinks, per season, with who controls each arrow (player, AI settlement, prince,
   random event). Draw it for discussion.

4. **Loops.** Name every loop and its brake:
   - growth: more tributaries → more silver → bigger warband → more tributaries. Brakes: protection obligations
     spread the warband thin, the prince grows suspicious, rivals react, squeezed settlements revolt or leave;
   - squeeze: high tribute → discontent → revolt; low tribute → no fear → refusals;
   - death spiral: lost battle → wounded/dead → can't protect → lose tributaries → can't pay upkeep. Needs a way out
     (loan from the prince with strings, selling a business, a risky expedition, a retainer leaving instead of all).

5. **Pressure curve for act I.** The tribute deadline is the early timer. Define the target: what a competent player
   has at the deadline (e.g. "pays on time if 2 of 3 major choices go well; one failure means a hard choice, not a
   game over"). Missing the deadline must be a story branch (debt, service to the prince, lost face), not a loss screen.

6. **Battle link.** Each economic system lists the battles it generates (ambush on полюдье, defending a tributary,
   bandits on the portage, rival raid) and what battle outcomes cost: recruits, healing time by wound level
   (Light / Heavy / Down), kit replacement, lost fame. Keep the price of a fighter consistent with `unit-design`.

7. **Strategy archetypes.** Model at least: the *protector* (many tributaries, low rate), the *extortionist* (few,
   squeezed hard), the *trader* (own businesses, prince's rights), the *raider* (loot, enemies everywhere). Healthy:
   each viable with a different risk profile, none dominant, each with a visible failure mode. Ask the
   `economy-modeler` agent to simulate them season by season with explicit assumptions.

8. **Degenerate-strategy checklist:** hoarding with no cost; farming a repeatable battle for loot; never leaving
   home; squeezing tributaries with no consequence; buy-low/sell-high loops between settlements; an upkeep that makes
   a smaller warband strictly better; an income so steady that events stop mattering; one business that pays for
   everything.

## Numbers

Use round, readable numbers the player can reason about at pause (tribute 30 silver, not 27.4). Every number in the
doc carries a status (proposal / accepted D-NNN) and a reason. Historical prices (dirhams, fur, salt) are flavour and
proportion hints only — gameplay wins. Ranges first ("upkeep is 20–30 % of a protector's income"), exact values once
the loops are agreed.

## Phase

The economy ships in phase 3 (campaign EA). In phase 1 nothing here is implemented; the only phase-1 contact is the
unit cost/value scale shared with `unit-design`.
