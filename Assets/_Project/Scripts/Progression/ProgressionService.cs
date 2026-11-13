using System;
using System.Collections.Generic;
using System.Text;
using LiarsBatting.Core;

namespace LiarsBatting.Progression
{
    // What one finished match earned, for the result popup.
    public class MatchSettlement
    {
        public int XpGained;
        public int LevelBefore;
        public int LevelAfter;
        public readonly List<string> Completed = new List<string>();

        public HeroId Hero;
        public int HeroXpGained;
        public int HeroLevelBefore;
        public int HeroLevelAfter;
        public readonly List<string> HeroCompleted = new List<string>();

        public readonly List<RewardInfo> NewRewards = new List<RewardInfo>();

        public string ToSummary()
        {
            var sb = new StringBuilder($"+{XpGained} XP");
            if (LevelAfter > LevelBefore) sb.Append($"  ·  レベルアップ！ Lv.{LevelAfter}");

            string heroName = HeroCatalog.Get(Hero)?.Name ?? Hero.ToString();
            sb.Append($"\n{heroName} 熟練度 +{HeroXpGained} XP");
            if (HeroLevelAfter > HeroLevelBefore) sb.Append($"  ·  熟練度 Lv.{HeroLevelAfter}!");

            var completed = new List<string>(Completed);
            completed.AddRange(HeroCompleted);
            if (completed.Count > 0) sb.Append("\n達成: ").Append(string.Join(", ", completed));
            if (NewRewards.Count > 0)
            {
                var names = new List<string>();
                foreach (var r in NewRewards) names.Add(r.Name);
                sb.Append("\n新規報酬: ").Append(string.Join(", ", names));
            }
            return sb.ToString();
        }
    }

    // The rules that turn a finished match into XP, levels and challenge progress. Pure logic on
    // PlayerProgress; ProfileStore does the loading and saving around it.
    public static class ProgressionService
    {
        // Starts a fresh daily/weekly set when the period has changed since the last save.
        public static void RollPeriods(PlayerProgress p, DateTime utcNow)
        {
            int day = ChallengeCatalog.DayNumber(utcNow);
            if (p.DailyPeriod != day)
            {
                p.DailyPeriod = day;
                p.Daily.Clear();
                p.DailyDone.Clear();
            }

            int week = ChallengeCatalog.WeekNumber(utcNow);
            if (p.WeeklyPeriod != week)
            {
                p.WeeklyPeriod = week;
                p.Weekly.Clear();
                p.WeeklyDone.Clear();
                p.WeeklyWinHeroes.Clear();
            }
        }

        // False for a surrender before LevelTable.MinSurrenderSecondsForXp: such a match earns no XP
        // and advances no challenge (the win/loss record still counts it).
        public static bool EarnsProgress(MatchStats s) =>
            !(s.Surrendered && s.DurationSeconds < LevelTable.MinSurrenderSecondsForXp);

        public static MatchSettlement ApplyMatch(PlayerProgress p, MatchStats s, DateTime utcNow)
        {
            RollPeriods(p, utcNow);
            var result = new MatchSettlement { LevelBefore = p.Level };

            // ---- player level ----
            int xp = LevelTable.MatchXp(s.Online, s.Won);

            foreach (var def in ChallengeCatalog.DailyFor(p.DailyPeriod))
                if (Advance(p.Daily, p.DailyDone, def, def.Progress(s)))
                {
                    xp += LevelTable.DailyChallengeXp;
                    result.Completed.Add(def.Text);
                }

            if (s.Won)
            {
                string heroKey = s.Hero.ToString();
                if (!p.WeeklyWinHeroes.Contains(heroKey)) p.WeeklyWinHeroes.Add(heroKey);
            }
            foreach (var def in ChallengeCatalog.WeeklyFor(p.WeeklyPeriod))
            {
                bool done;
                if (def.Id == ChallengeCatalog.DistinctHeroWinsId)
                {
                    int had = p.Progress(p.Weekly, def.Id);
                    done = Advance(p.Weekly, p.WeeklyDone, def, p.WeeklyWinHeroes.Count - had);
                }
                else
                {
                    done = Advance(p.Weekly, p.WeeklyDone, def, def.Progress(s));
                }
                if (done)
                {
                    xp += LevelTable.WeeklyChallengeXp;
                    result.Completed.Add(def.Text);
                }
            }

            AddXp(p, xp);
            result.XpGained = xp;
            result.LevelAfter = p.Level;
            result.NewRewards.AddRange(RewardCatalog.UnlockedBetween(result.LevelBefore, result.LevelAfter));

            // ---- hero mastery (the hero I played) ----
            var hero = p.Hero(s.Hero);
            result.Hero = s.Hero;
            result.HeroLevelBefore = hero.Level;
            int heroXp = LevelTable.HeroMatchXp(s.Online, s.Won);
            foreach (var def in HeroChallengeCatalog.For(s.Hero))
                if (Advance(hero.Challenges, hero.Done, def, def.Progress(s)))
                {
                    heroXp += LevelTable.HeroChallengeXp;
                    result.HeroCompleted.Add(def.Text);
                }
            AddHeroXp(hero, heroXp);
            result.HeroXpGained = heroXp;
            result.HeroLevelAfter = hero.Level;
            result.NewRewards.AddRange(RewardCatalog.HeroUnlockedBetween(s.Hero, result.HeroLevelBefore, result.HeroLevelAfter));

            return result;
        }

        // Adds to a challenge; true only on the match that completes it.
        private static bool Advance(Dictionary<string, int> map, List<string> done, ChallengeDef def, int amount)
        {
            if (done.Contains(def.Id) || amount <= 0) return false;
            map.TryGetValue(def.Id, out int current);
            current = Math.Min(def.Target, current + amount);
            map[def.Id] = current;
            if (current < def.Target) return false;
            done.Add(def.Id);
            return true;
        }

        public static void AddXp(PlayerProgress p, int amount)
        {
            p.Xp += amount;
            while (p.Level < LevelTable.MaxPlayerLevel && p.Xp >= LevelTable.PlayerXpToNext(p.Level))
            {
                p.Xp -= LevelTable.PlayerXpToNext(p.Level);
                p.Level++;
            }
            if (p.Level >= LevelTable.MaxPlayerLevel) p.Xp = 0;
        }

        public static void AddHeroXp(HeroProgress h, int amount)
        {
            h.Xp += amount;
            while (h.Level < LevelTable.MaxHeroLevel && h.Xp >= LevelTable.HeroXpToNext(h.Level))
            {
                h.Xp -= LevelTable.HeroXpToNext(h.Level);
                h.Level++;
            }
            if (h.Level >= LevelTable.MaxHeroLevel) h.Xp = 0;
        }
    }
}
