using System.Collections.Generic;

namespace LiarsBatting.Core
{
    public enum GameResult { None, PlayerWin, AiWin }

    // Pure data: who owns which secret, how many LIE TOKENs are left, and the
    // history of both attack directions. No UnityEngine references on purpose --
    // this is the part a unit test (or later, a server) can exercise directly.
    public class GameState
    {
        public int[] PlayerSecret;
        public int[] AiSecret;

        public int PlayerLieTokens = 2;
        public int AiLieTokens = 2;

        public HeroId PlayerHero;
        public HeroId AiHero;

        // Remaining uses of each side's ACTIVE hero ability this game (see
        // HeroCatalog.Charges). Passive heroes (Hunter, DemonHunter) don't use this.
        public int PlayerAbilityCharges;
        public int AiAbilityCharges;

        // Warrior's "keep the turn" ability: set true when used, consumed (and
        // cleared) the next time a turn would otherwise pass to the other side.
        // The AI's own use of Warrior is decided in the same call as the turn
        // actually ending (AiOpponent.ShouldUseWarriorExtraTurn), so it needs no
        // equivalent persisted flag.
        public bool PlayerExtraTurnPending;

        // Which positions of each secret have been forced open by a wrong (or
        // caught) trust/challenge call -- or by Wizard's ability. Index-aligned
        // with PlayerSecret/AiSecret.
        public readonly bool[] PlayerRevealed = new bool[4];
        public readonly bool[] AiRevealed = new bool[4];

        // Turns where the player guessed AiSecret.
        public readonly List<TurnRecord> PlayerAttackHistory = new List<TurnRecord>();

        // Turns where the AI guessed PlayerSecret.
        public readonly List<TurnRecord> AiAttackHistory = new List<TurnRecord>();

        public GameResult Result = GameResult.None;
    }
}
