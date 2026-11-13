using System;
using LiarsBatting.Core;

namespace LiarsBatting.Progression
{
    // What players/{nickname} holds about one player, as the game uses it. Pure data (no
    // UnityEngine), like Core/: ProfileStore reads it from and writes it to Firestore.
    // Wins/Losses count matches against people only -- AI matches never touch them.
    public class PlayerProfile
    {
        public string Nickname;
        public HeroId ProfileIcon = HeroId.Hunter;   // the portrait shown as this player's picture
        public int Wins;
        public int Losses;
        public PlayerProgress Progress = new PlayerProgress();

        public int HumanGames => Wins + Losses;

        public int WinRatePercent => HumanGames == 0 ? 0 : (int)Math.Round(100.0 * Wins / HumanGames);

        public string RecordText => $"{Wins}勝 {Losses}敗";
    }
}
