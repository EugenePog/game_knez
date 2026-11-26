using System;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable movement rules from content (<c>content/defs/movement.json</c>, battle-sim.md §7 and §10): the numbers
    /// that decide how squads march, crowd, pass allies and regroup. Shared by the whole battle and fingerprinted.
    /// Fighter sizes and speeds live in <see cref="BodyDef"/>, formation shapes and ally squeezing in
    /// <see cref="FormationDef"/>. Numerical constants that only keep the maths exact stay in code
    /// (<c>Movement/MovementRules.cs</c>).
    /// </summary>
    public sealed class MovementDef
    {
        /// <summary>Creates the rules; throws <see cref="ArgumentException"/> on values outside their sanity bounds.
        /// Times are seconds, rounded to the nearest whole tick (0.05 s), at least one.</summary>
        public MovementDef(Fix pivotTolerance, Fix slotCatchUpTime, Fix personalSpace, Fix separationTime, Fix holdingReach,
            Fix formedUpReach, int collisionPasses, Fix allyPartingDepth, int pathBudget, Fix regroupLead,
            Fix regroupMaxWait, Fix regroupCost, Fix reformPace, Fix stepBackPace, Fix withdrawClearance, Fix closeInRange,
            Fix turnLagTime, Fix advanceRange, Fix pursuitTime, Fix pursuitRange)
        {
            PivotTolerance = DefChecks.Positive(pivotTolerance, 90, "°", nameof(pivotTolerance));
            SlotCatchUpTicks = DefChecks.Ticks(slotCatchUpTime, 5, nameof(slotCatchUpTime));
            PersonalSpace = DefChecks.PositiveMetres(personalSpace, 1, nameof(personalSpace));
            SeparationTicks = DefChecks.Ticks(separationTime, 5, nameof(separationTime));
            HoldingReach = DefChecks.PositiveMetres(holdingReach, 10, nameof(holdingReach));
            FormedUpReach = DefChecks.PositiveMetres(formedUpReach, 2, nameof(formedUpReach));
            CollisionPasses = DefChecks.Range(collisionPasses, 1, 20, nameof(collisionPasses));
            AllyPartingDepth = DefChecks.PositiveMetres(allyPartingDepth, 2, nameof(allyPartingDepth));
            PathBudget = DefChecks.Range(pathBudget, 100, 1000000, nameof(pathBudget));
            RegroupLead = DefChecks.NonNegative(regroupLead, 20, "m", nameof(regroupLead));
            RegroupMaxWaitTicks = DefChecks.Ticks(regroupMaxWait, 60, nameof(regroupMaxWait));
            RegroupCost = DefChecks.NonNegative(regroupCost, 20, string.Empty, nameof(regroupCost));
            ReformPace = DefChecks.Fraction(reformPace, nameof(reformPace));
            StepBackPace = DefChecks.Fraction(stepBackPace, nameof(stepBackPace));
            WithdrawClearance = DefChecks.NonNegative(withdrawClearance, 20, "m", nameof(withdrawClearance));
            CloseInRange = DefChecks.NonNegative(closeInRange, 20, "m", nameof(closeInRange));
            if (turnLagTime < Fix.Zero || turnLagTime > Fix.FromInt(20))
            {
                throw new ArgumentException($"turnLagTime must be 0 (off) or 0.05..20 s (got {turnLagTime}).");
            }

            TurnLagTicks = turnLagTime == Fix.Zero ? 0 : DefChecks.Ticks(turnLagTime, 20, nameof(turnLagTime));
            AdvanceRange = DefChecks.PositiveMetres(advanceRange, 100, nameof(advanceRange));
            PursuitTicks = DefChecks.Ticks(pursuitTime, 120, nameof(pursuitTime));
            PursuitRange = DefChecks.PositiveMetres(pursuitRange, 250, nameof(pursuitRange));
        }

        /// <summary>
        /// The values in <c>content/defs/movement.json</c> when this was written (2026-11-21), rounded exactly as the
        /// content reader rounds decimals (a test keeps them equal). Content always brings its own file; these serve tests
        /// and scenarios built in code.
        /// </summary>
        public static readonly MovementDef Standard = new MovementDef(
            pivotTolerance: Fix.FromInt(30),
            slotCatchUpTime: Fix.Ratio(1, 2),
            personalSpace: Decimal(1, 10),
            separationTime: Fix.Ratio(1, 4),
            holdingReach: Fix.Ratio(3, 2),
            formedUpReach: Decimal(3, 10),
            collisionPasses: 6,
            allyPartingDepth: Fix.Ratio(1, 4),
            pathBudget: 4000,
            regroupLead: Fix.One,
            regroupMaxWait: Fix.FromInt(5),
            regroupCost: Fix.One,
            reformPace: Fix.Ratio(1, 2),
            stepBackPace: Fix.Ratio(1, 2),
            withdrawClearance: Fix.FromInt(3),
            closeInRange: Fix.FromInt(3),
            turnLagTime: Fix.FromInt(4),
            advanceRange: Fix.FromInt(15),
            pursuitTime: Fix.FromInt(10),
            pursuitRange: Fix.FromInt(30));

        /// <summary>A squad turns in place until its target is within this many degrees of its front, then marches.</summary>
        public Fix PivotTolerance { get; }

        /// <summary><see cref="PivotTolerance"/> in <see cref="Angle"/> units.</summary>
        public int PivotToleranceUnits => (int)(PivotTolerance.Raw * Angle.UnitsPerTurn / 360 / Fix.OneRaw);

        /// <summary>Time constant for closing the lag to the slot: each tick a fighter closes 1/this of it (ticks).</summary>
        public int SlotCatchUpTicks { get; }

        /// <summary>Gap beyond touching that men keep from each other and from walls, metres.</summary>
        public Fix PersonalSpace { get; }

        /// <summary>Time constant for easing out of someone's personal space (ticks).</summary>
        public int SeparationTicks { get; }

        /// <summary>A man without orders within this of his slot holds his ground (weighs his formation's hold weight).</summary>
        public Fix HoldingReach { get; }

        /// <summary>A move order ends when every member stands within this of his slot (formed up).</summary>
        public Fix FormedUpReach { get; }

        /// <summary>Most collision relaxation passes per tick.</summary>
        public int CollisionPasses { get; }

        /// <summary>Allies of different squads overlapping deeper than this part gently (a little per pass) instead of at
        /// once, so a squad passing through allied ranks isn't flung apart when its order changes.</summary>
        public Fix AllyPartingDepth { get; }

        /// <summary>Route search nodes a tick may spend before waiting squads wait for the next tick.</summary>
        public int PathBudget { get; }

        /// <summary>How far before a gap narrower than its frontage a squad stops to regroup into fewer files, metres.</summary>
        public Fix RegroupLead { get; }

        /// <summary>The longest a squad waits for its men to regroup before it marches on anyway (ticks).</summary>
        public int RegroupMaxWaitTicks { get; }

        /// <summary>
        /// Route cost of regrouping, per metre the squad grows deeper: a narrowed squad is longer, and its rear waits that
        /// much longer to go through. 1 = exactly that waiting; 0 = regrouping is free.
        /// </summary>
        public Fix RegroupCost { get; }

        /// <summary>A positive decimal fraction rounded to the nearest raw unit, as content numbers are read.</summary>
        private static Fix Decimal(long numerator, long denominator) =>
            Fix.FromRaw(((numerator * Fix.OneRaw * 2) + denominator) / (2 * denominator));

        /// <summary>A squad changing formation on the move marches at this fraction of its pace until formed up (decided
        /// 2026-11-21: re-form on the move, at half pace).</summary>
        public Fix ReformPace { get; }

        /// <summary>A squad stepping back without turning moves at this fraction of its pace: onto a target just behind it
        /// (decided 2026-11-21: the front lands on the ordered point), and out of melee (decided 2026-11-22).</summary>
        public Fix StepBackPace { get; }

        /// <summary>A squad stepping back out of melee keeps facing the enemy until no enemy is within each man's weapon
        /// reach plus this, metres; then it turns and marches (decided 2026-11-22: 3 m).</summary>
        public Fix WithdrawClearance { get; }

        /// <summary>In a squad standing in melee, a man not yet fighting steps toward the nearest enemy whose body is within
        /// this of his own (edge to edge, metres) until he reaches him; 0 = only the men who touch fight (asked for
        /// 2026-11-23; 3 m is a proposal).</summary>
        public Fix CloseInRange { get; }

        /// <summary>
        /// Soft wheel (asked for 2026-11-23): men are not carried round with their slots when the squad turns; each
        /// walks after his slot and the lag the turn left closes over this many ticks (to about 5 %); 0 = carried
        /// rigidly, as before. 4 s is a proposal from the 3–5 s asked for.
        /// </summary>
        public int TurnLagTicks { get; }

        /// <summary>Stances (M4 step 1, decided 2026-11-26: 15 m): a squad in Advance or Pursue with no order goes at the
        /// nearest enemy squad whose man is within this of one of its own (edge to edge, metres).</summary>
        public Fix AdvanceRange { get; }

        /// <summary>Pursuit (M4 step 1, decided 2026-11-26: 10 s): a chase lasts at most this many ticks.</summary>
        public int PursuitTicks { get; }

        /// <summary>Pursuit (M4 step 1, decided 2026-11-26: 30 m): men chase only within this of where the chase began.</summary>
        public Fix PursuitRange { get; }

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(PivotTolerance.Raw);
            hash.Add(SlotCatchUpTicks);
            hash.Add(PersonalSpace.Raw);
            hash.Add(SeparationTicks);
            hash.Add(HoldingReach.Raw);
            hash.Add(FormedUpReach.Raw);
            hash.Add(CollisionPasses);
            hash.Add(AllyPartingDepth.Raw);
            hash.Add(PathBudget);
            hash.Add(RegroupLead.Raw);
            hash.Add(RegroupMaxWaitTicks);
            hash.Add(RegroupCost.Raw);
            hash.Add(ReformPace.Raw);
            hash.Add(StepBackPace.Raw);
            hash.Add(WithdrawClearance.Raw);
            hash.Add(CloseInRange.Raw);
            hash.Add(TurnLagTicks);
            hash.Add(AdvanceRange.Raw);
            hash.Add(PursuitTicks);
            hash.Add(PursuitRange.Raw);
        }
    }
}
