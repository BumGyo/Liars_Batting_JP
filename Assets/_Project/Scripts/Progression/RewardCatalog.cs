using System;
using System.Collections.Generic;
using LiarsBatting.Core;

namespace LiarsBatting.Progression
{
    public enum RewardKind { Title, Frame, Background, CardSkin, HeroSkin, HeroEffect }

    // One unlockable cosmetic. Level 1 = available from the start (the defaults).
    // Hero == null: unlocked by the player level. Hero set: unlocked by that hero's mastery level.
    // Tint is a hex colour ("RRGGBB") used by the placeholder look until real art exists:
    // a background/card/portrait tint, or a portrait-frame colour. A sprite at
    // Resources/Cosmetics/<Id> replaces the placeholder where the game supports it (portrait skins).
    public class RewardInfo
    {
        public string Id;
        public RewardKind Kind;
        public string Name;
        public int Level;
        public string Tint;
        public HeroId? Hero;

        public RewardInfo(string id, RewardKind kind, string name, int level, string tint = null, HeroId? hero = null)
        {
            Id = id; Kind = kind; Name = name; Level = level; Tint = tint; Hero = hero;
        }
    }

    public static class RewardCatalog
    {
        public const string NoTitle = "title_none";
        public const string NoFrame = "frame_none";
        public const string DefaultBackground = "bg_default";
        public const string DefaultCards = "card_default";

        // Player-level rewards, in level order. Edit names/levels/tints here.
        public static readonly RewardInfo[] Player =
        {
            new RewardInfo(NoTitle, RewardKind.Title, "称号なし", 1),
            new RewardInfo(NoFrame, RewardKind.Frame, "デフォルトフレーム", 1, "D6A046"),
            new RewardInfo(DefaultBackground, RewardKind.Background, "デフォルト地下室", 1, "FFFFFF"),
            new RewardInfo(DefaultCards, RewardKind.CardSkin, "デフォルトカード", 1, "FFFFFF"),

            new RewardInfo("title_rookie", RewardKind.Title, "初心者の嘘つき", 2),
            new RewardInfo("frame_bronze", RewardKind.Frame, "ブロンズフレーム", 3, "B07A45"),
            new RewardInfo("bg_crimson", RewardKind.Background, "深紅のカジノ", 5, "FFB4AA"),
            new RewardInfo("card_ebony", RewardKind.CardSkin, "エボニーカード", 7, "B9B2C4"),
            new RewardInfo("title_pokerface", RewardKind.Title, "ポーカーフェイス", 10),
            new RewardInfo("frame_silver", RewardKind.Frame, "シルバーフレーム", 10, "D3D7DE"),
            new RewardInfo("bg_teal", RewardKind.Background, "深夜のティール", 15, "A6E0DC"),
            new RewardInfo("card_gold", RewardKind.CardSkin, "黄金のカード", 20, "FFE08A"),
            new RewardInfo("title_tazza", RewardKind.Title, "勝負師", 20),
            new RewardInfo("frame_gold", RewardKind.Frame, "ゴールドフレーム", 25, "F2C94C"),
            new RewardInfo("title_legend", RewardKind.Title, "伝説の大ぼら吹き", 30),
            new RewardInfo("bg_vip", RewardKind.Background, "黄金のVIPルーム", 30, "FFE2A8"),
        };

        // ---- hero mastery rewards (same shape for every hero) ----
        public const int HeroTitleLevel = 3;
        public const int HeroSkin1Level = 5;
        public const int HeroEffectLevel = 7;
        public const int HeroMasterLevel = 10;

        // (Lv.3 title, Lv.10 master title) per hero. Edit names here.
        private static readonly Dictionary<HeroId, (string first, string master)> HeroTitles =
            new Dictionary<HeroId, (string, string)>
            {
                { HeroId.Hunter, ("狩人の舌", "嘘狩りの大家") },
                { HeroId.Paladin, ("不屈の信念", "不屈の聖騎士") },
                { HeroId.Rogue, ("真実を見通す瞳", "嘘の天敵") },
                { HeroId.Priest, ("執拗な審問官", "一桁の達人") },
                { HeroId.DemonHunter, ("封印者", "鎖の支配者") },
                { HeroId.Warrior, ("止まらぬ突撃", "連続突撃の化身") },
                { HeroId.Wizard, ("見抜く視線", "千里眼の賢者") },
            };

        public static string SkinId(HeroId hero, int n) => $"{hero}_skin{n}";
        public static string EffectId(HeroId hero, bool on) => $"{hero}_fx{(on ? 1 : 0)}";

        public static readonly RewardInfo[] Hero = BuildHeroRewards();

        private static RewardInfo[] BuildHeroRewards()
        {
            var list = new List<RewardInfo>();
            foreach (var info in HeroCatalog.All)
            {
                var h = info.Id;
                var titles = HeroTitles[h];
                list.Add(new RewardInfo(SkinId(h, 0), RewardKind.HeroSkin, "デフォルト肖像画", 1, "FFFFFF", h));
                list.Add(new RewardInfo(EffectId(h, false), RewardKind.HeroEffect, "エフェクトなし", 1, null, h));
                list.Add(new RewardInfo($"{h}_title1", RewardKind.Title, titles.first, HeroTitleLevel, null, h));
                list.Add(new RewardInfo(SkinId(h, 1), RewardKind.HeroSkin, "高級肖像画", HeroSkin1Level, "FFF4E2", h));
                list.Add(new RewardInfo(EffectId(h, true), RewardKind.HeroEffect, "アニメーション肖像画", HeroEffectLevel, null, h));
                list.Add(new RewardInfo($"{h}_title2", RewardKind.Title, titles.master, HeroMasterLevel, null, h));
                list.Add(new RewardInfo(SkinId(h, 2), RewardKind.HeroSkin, "黄金の肖像画", HeroMasterLevel, "FFE27A", h));
            }
            return list.ToArray();
        }

        public static RewardInfo Find(string id) =>
            Array.Find(Player, r => r.Id == id) ?? Array.Find(Hero, r => r.Id == id);

        // Player-level rewards of one kind (the カスタマイズ tab).
        public static List<RewardInfo> OfKind(RewardKind kind) => new List<RewardInfo>(Array.FindAll(Player, r => r.Kind == kind));

        // One hero's rewards of one kind (the ヒーロー熟練度 tab).
        public static List<RewardInfo> ForHero(HeroId hero, RewardKind kind) =>
            new List<RewardInfo>(Array.FindAll(Hero, r => r.Hero == hero && r.Kind == kind));

        public static List<RewardInfo> ForHero(HeroId hero) => new List<RewardInfo>(Array.FindAll(Hero, r => r.Hero == hero));

        // Player rewards reached by going from levelBefore to levelAfter.
        public static List<RewardInfo> UnlockedBetween(int levelBefore, int levelAfter) =>
            new List<RewardInfo>(Array.FindAll(Player, r => r.Level > levelBefore && r.Level <= levelAfter));

        public static List<RewardInfo> HeroUnlockedBetween(HeroId hero, int levelBefore, int levelAfter) =>
            new List<RewardInfo>(Array.FindAll(Hero, r => r.Hero == hero && r.Level > levelBefore && r.Level <= levelAfter));

        // The equipped title's text, or "" when none is equipped.
        public static string TitleText(string titleId)
        {
            var r = Find(titleId);
            return r == null || r.Id == NoTitle ? "" : r.Name;
        }
    }
}
