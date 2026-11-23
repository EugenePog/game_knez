using Knez.Sim.Defs;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim.Combat
{
    /// <summary>
    /// The rules of a single blow (battle-sim.md §11): who can be struck, from which side, and the chance (‰) it gets
    /// through. Integer permille throughout: a share of a shield truncates toward zero, everything is summed, the clamp
    /// comes last. Stateless; reads the battle's <see cref="CombatDef"/>.
    /// </summary>
    internal static class Blows
    {
        /// <summary>
        /// True when <paramref name="attacker"/> can strike <paramref name="defender"/> now: both alive, enemies, the
        /// defender's body within the attacker's weapon reach of his own (edge to edge), and within his strike arc.
        /// </summary>
        public static bool CanStrike(CombatDef rules, Fighter attacker, Fighter defender)
        {
            if (!attacker.IsAlive || !defender.IsAlive || attacker.SideId == defender.SideId)
            {
                return false;
            }

            FixVec2 to = defender.Position - attacker.Position;
            Fix limit = attacker.Radius + defender.Radius + attacker.Def.Kit.Weapon.Reach;
            if (FixMath.Abs(to.X) > limit || FixMath.Abs(to.Y) > limit || to.Length > limit)
            {
                return false;
            }

            if (to == FixVec2.Zero)
            {
                return true; // on the same spot no direction exists; like Sector, count it as in front
            }

            int off = attacker.Facing.DeltaTo(FixMath.Atan2(to.Y, to.X));
            return Within(off, rules.StrikeArcUnits);
        }

        /// <summary>Which side of <paramref name="defender"/> a blow from <paramref name="from"/> comes from.</summary>
        public static BlowSector Sector(CombatDef rules, Fighter defender, FixVec2 from)
        {
            FixVec2 to = from - defender.Position;
            if (to == FixVec2.Zero)
            {
                return BlowSector.Front;
            }

            int off = defender.Facing.DeltaTo(FixMath.Atan2(to.Y, to.X));
            if (Within(off, rules.FrontArcUnits))
            {
                return BlowSector.Front;
            }

            return Within(off, rules.RearArcUnits) ? BlowSector.Flank : BlowSector.Rear;
        }

        /// <summary>
        /// The chance (‰) a blow by <paramref name="attacker"/> at <paramref name="defender"/> gets through: the matrix
        /// (attacker's weapon × experience), minus the defender's shield (all of it from the front, a share from flank and
        /// rear), a shield wall's bonus from the front (with a shield), body armour, head cover, experience and type; plus
        /// the flank or rear bonus; minus the attacker's own wound penalty and, wavering, his morale penalty; within the limits.
        /// </summary>
        public static int Chance(BattleState state, Fighter attacker, Fighter defender, out BlowSector sector)
        {
            CombatDef rules = state.Scenario.Combat;
            FighterKit kit = defender.Def.Kit;
            sector = Sector(rules, defender, attacker.Position);
            int chance = rules.BaseHit(attacker.Def.Kit.Weapon, attacker.Def.Kit.Experience);
            int shield = kit.Shield.Protection;
            switch (sector)
            {
                case BlowSector.Front:
                    chance -= shield;
                    if (shield > 0 && InShieldWall(state, defender))
                    {
                        chance -= rules.ShieldWallBonus;
                    }

                    break;
                case BlowSector.Flank:
                    chance -= shield * rules.ShieldFromFlank / DefChecks.Whole;
                    chance += rules.FlankBonus;
                    break;
                default:
                    chance -= shield * rules.ShieldFromRear / DefChecks.Whole;
                    chance += rules.RearBonus;
                    break;
            }

            chance -= kit.BodyArmour.Protection + kit.HeadCover.Protection + kit.Experience.Defence + kit.Type.Defence;
            chance -= Wounds.HitPenalty(rules, attacker.Wound);
            if (attacker.MoraleState == Morale.MoraleState.Wavering)
            {
                chance -= state.Scenario.Morale.WaveringHitPenalty;
            }
            return chance < rules.MinHit ? rules.MinHit : chance > rules.MaxHit ? rules.MaxHit : chance;
        }

        private static bool InShieldWall(BattleState state, Fighter fighter)
        {
            Squad? squad = fighter.SquadId.IsNone ? null : state.FindSquad(fighter.SquadId);
            return squad != null && squad.Formation.ShieldWall;
        }

        private static bool Within(int off, int arc) => off <= arc && off >= -arc;
    }
}
