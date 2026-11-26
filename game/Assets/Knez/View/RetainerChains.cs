#nullable enable
using System.Collections.Generic;
using Knez.Sim;
using Knez.Sim.Units;

namespace Knez.Game.View
{
    /// <summary>
    /// Which of the player's Retainers are chained to their squad (asked for by the user 2026-11-26): a Retainer in a squad
    /// is chained by default — a click or a drag on him means his squad, and the squad's orders move him with it — until
    /// the player unchains him (the HUD's button or U); then he takes his own orders (drag, right-click, X). Joining a
    /// squad chains him again. View-side only: the sim knows nothing of it. Plain C#, keyed by id.
    /// </summary>
    public sealed class RetainerChains
    {
        private readonly HashSet<int> _unchained = new HashSet<int>();

        /// <summary>Whether <paramref name="fighter"/> is a Retainer in a squad who follows it (clicks on him mean the squad).</summary>
        public bool IsChained(Fighter fighter) =>
            fighter.IsRetainer && !fighter.SquadId.IsNone && !_unchained.Contains(fighter.Id.Value);

        /// <summary>Whether he takes his own orders: a Retainer standing alone, or one unchained in his squad.</summary>
        public bool OnHisOwn(Fighter fighter) => fighter.IsRetainer && !IsChained(fighter);

        /// <summary>Lets <paramref name="retainer"/> take his own orders while he stays in his squad.</summary>
        public void Unchain(EntityId retainer) => _unchained.Add(retainer.Value);

        /// <summary>Chains <paramref name="retainer"/> to his squad again.</summary>
        public void Chain(EntityId retainer) => _unchained.Remove(retainer.Value);

        /// <summary>
        /// Call every frame: a Retainer standing alone or out of the fight forgets being unchained, so the next squad he
        /// joins has him chained again.
        /// </summary>
        public void Forget(BattleState state)
        {
            _unchained.RemoveWhere(id =>
            {
                Fighter? man = state.FindFighter(new EntityId(id));
                return man == null || !man.IsAlive || man.SquadId.IsNone;
            });
        }

        /// <summary>The living Retainers chained to <paramref name="squad"/>, by id.</summary>
        public List<Fighter> ChainedTo(BattleState state, EntityId squad)
        {
            var chained = new List<Fighter>();
            foreach (Fighter fighter in state.Fighters)
            {
                if (fighter.IsAlive && fighter.SquadId == squad && IsChained(fighter))
                {
                    chained.Add(fighter);
                }
            }

            return chained;
        }
    }
}
