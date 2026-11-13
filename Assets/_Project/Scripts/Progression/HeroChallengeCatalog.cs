using System;
using System.Collections.Generic;
using LiarsBatting.Core;

namespace LiarsBatting.Progression
{
    // Five mastery challenges per hero: two warm-ups (play, win) and three built around that
    // hero's ability. They only count in matches played AS that hero. "(累計)" ones add up across
    // matches; the others must happen within one match.
    public static class HeroChallengeCatalog
    {
        public const int PerHero = 5;

        private static readonly Dictionary<HeroId, List<ChallengeDef>> ByHero = Build();

        public static List<ChallengeDef> For(HeroId hero) => ByHero[hero];

        private static ChallengeDef C(HeroId hero, string key, string text, int target, Func<MatchStats, int> progress)
            => new ChallengeDef($"{hero}_{key}", text, target, progress);

        private static int B(bool value) => value ? 1 : 0;

        private static Dictionary<HeroId, List<ChallengeDef>> Build()
        {
            var map = new Dictionary<HeroId, List<ChallengeDef>>();
            foreach (var info in HeroCatalog.All)
            {
                var h = info.Id;
                var list = new List<ChallengeDef>
                {
                    C(h, "play3", $"{info.Name}で3試合プレイ (累計)", 3, s => 1),
                    C(h, "win3", $"{info.Name}で3勝 (累計)", 3, s => B(s.Won)),
                };
                list.AddRange(AbilityChallenges(h));
                map[h] = list;
            }
            return map;
        }

        private static IEnumerable<ChallengeDef> AbilityChallenges(HeroId h)
        {
            switch (h)
            {
                case HeroId.Hunter:
                    yield return C(h, "hide", "LIE TOKENで真の4ストライクを隠して勝利", 1, s => B(s.Won && s.HidTrueWin));
                    yield return C(h, "believed10", "自分の嘘を相手に信じさせる (累計)", 10, s => s.LiesBelieved);
                    yield return C(h, "perfect", "LIE TOKENを2つ使い切り一度も見破られずに勝利", 1,
                        s => B(s.Won && s.LiesTold >= 2 && s.LiesCaught == 0));
                    break;
                case HeroId.Paladin:
                    yield return C(h, "heal5", "信念の回復を使用 (累計)", 5, s => s.AbilityUses);
                    yield return C(h, "afterheal", "回復した後の嘘を相手に信じさせる", 1, s => B(s.LiesBelievedAfterHeal > 0));
                    yield return C(h, "lies3", "1試合で嘘を3回ついて勝利", 1, s => B(s.Won && s.LiesTold >= 3));
                    break;
                case HeroId.Rogue:
                    yield return C(h, "catch3", "真実看破で嘘を見破る (累計)", 3, s => s.RogueCatches);
                    yield return C(h, "doubt10", "ダウトで嘘を見破る (累計)", 10, s => s.ChallengesCorrect);
                    yield return C(h, "flawless", "ダウトを一度も外さずに勝利 (1回以上ダウト)", 1,
                        s => B(s.Won && s.Challenges >= 1 && s.ChallengesWrong == 0));
                    break;
                case HeroId.Priest:
                    yield return C(h, "query6", "一桁尋問を使用 (累計)", 6, s => s.AbilityUses);
                    yield return C(h, "yes3", "尋問で「その通りです」の返答を聞く (累計)", 3, s => s.PriestYes);
                    yield return C(h, "twice", "1試合で尋問を2回使って勝利", 1, s => B(s.Won && s.AbilityUses >= 2));
                    break;
                case HeroId.DemonHunter:
                    yield return C(h, "pure", "相手の数字を公開させず推理のみで勝利", 1,
                        s => B(s.Won && s.WonByGuess && s.OpponentDigitsRevealed == 0));
                    yield return C(h, "fast", "推測6回以内で的中させて勝利", 1, s => B(s.Won && s.WonByGuess && s.Guesses <= 6));
                    yield return C(h, "sealed", "自分の数字を1つも公開されずに勝利", 1, s => B(s.Won && s.MyDigitsRevealed == 0));
                    break;
                case HeroId.Warrior:
                    yield return C(h, "rush5", "連続突撃を使用 (累計)", 5, s => s.AbilityUses);
                    yield return C(h, "extrawin", "連続突撃で得た追加ターンに的中させて勝利", 1, s => B(s.WonOnExtraTurn));
                    yield return C(h, "rushwin3", "連続突撃を使用した試合で勝利 (累計)", 3, s => B(s.Won && s.AbilityUses > 0));
                    break;
                case HeroId.Wizard:
                    yield return C(h, "eye5", "千里眼を使用 (累計)", 5, s => s.AbilityUses);
                    yield return C(h, "eyewin3", "千里眼を使用した試合で勝利 (累計)", 3, s => B(s.Won && s.AbilityUses > 0));
                    yield return C(h, "quick", "千里眼使用後、推測3回以内で的中させて勝利", 1,
                        s => B(s.Won && s.WonByGuess && s.GuessesAtWizard >= 0 && s.Guesses - s.GuessesAtWizard <= 3));
                    break;
            }
        }
    }
}
