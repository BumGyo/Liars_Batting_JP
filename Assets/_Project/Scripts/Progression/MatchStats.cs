using LiarsBatting.Core;

namespace LiarsBatting.Progression
{
    // What I did in one match, counted from my side only. GameBootstrap bumps these as the match
    // plays out (AI and online flows alike) and hands the finished object to ProgressionService.
    public class MatchStats
    {
        public HeroId Hero;
        public bool Online;
        public bool Won;
        public bool WonByGuess;          // the win came from guessing the secret (not reveals/surrender)
        public bool Surrendered;         // I pressed 降伏
        public float DurationSeconds;    // match clock at the end

        public int Guesses;              // full 4-digit guesses I submitted
        public int LiesTold;             // LIE TOKENs I spent on a report
        public int LiesBelieved;         // ...that the opponent trusted
        public int LiesCaught;           // ...that the opponent challenged (caught)
        public int Challenges;           // times I challenged the opponent's report
        public int ChallengesCorrect;    // ...and it really was a lie
        public int ChallengesWrong;      // ...and it was the truth
        public int AbilityUses;          // active hero ability uses (always my one hero's ability)
        public int MyDigitsRevealed;     // my digits open at the end
        public int OpponentDigitsRevealed;

        // hero-specific
        public bool HidTrueWin;              // Hunter: lied about a real 4-strike
        public bool PaladinHealed;           // Paladin: healed a LIE TOKEN this match
        public int LiesBelievedAfterHeal;    // Paladin: lies believed after the heal
        public int RogueCatches;             // Rogue: insight revealed a lie
        public int PriestYes;                // Priest: "その通りです" answers
        public bool OnExtraTurn;             // Warrior: the current attack turn is a kept (extra) turn
        public bool WonOnExtraTurn;          // Warrior: the winning guess came on an extra turn
        public int GuessesAtWizard = -1;     // Wizard: my guess count when Clairvoyance was used

        public void LieBelieved()
        {
            LiesBelieved++;
            if (PaladinHealed) LiesBelievedAfterHeal++;
        }
    }
}
