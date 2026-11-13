using System;

namespace LiarsBatting.Core
{
    // Static metadata only -- what each hero's ability is called, how many
    // charges it starts with (-1 = passive, always on; 0 = passive, no button;
    // N = an active ability usable N times per game), and whether using it
    // consumes the player's turn. The actual rule effects live in GameState /
    // AiOpponent / the turn-flow code, not here.
    public class HeroInfo
    {
        public HeroId Id;
        public string Name;
        public string AbilityName;
        public string AbilityDescription;
        public int Charges;
        public bool ConsumesTurn;
    }

    public static class HeroCatalog
    {
        public static readonly HeroInfo[] All =
        {
            new HeroInfo
            {
                Id = HeroId.Hunter, Name = "虚言手",
                AbilityName = "狩りの嘘",
                AbilityDescription = "正解（4ストライク）であっても偽りの結果を伝えることができます。",
                Charges = -1, ConsumesTurn = false
            },
            new HeroInfo
            {
                Id = HeroId.Paladin, Name = "白回復",
                AbilityName = "信念の回復",
                AbilityDescription = "LIE TOKENを1つ回復します。",
                Charges = 1, ConsumesTurn = false
            },
            new HeroInfo
            {
                Id = HeroId.Rogue, Name = "真選",
                AbilityName = "真実看破",
                AbilityDescription = "1試合につき1回、相手の回答が真実か嘘かを確認します。",
                Charges = 1, ConsumesTurn = false
            },
            new HeroInfo
            {
                Id = HeroId.Priest, Name = "一桁",
                AbilityName = "一桁尋問",
                AbilityDescription = "1試合につき2回、4桁すべてではなく指定した1桁の数字を直接質問します。ターンを消費します。",
                Charges = 2, ConsumesTurn = true
            },
            new HeroInfo
            {
                Id = HeroId.DemonHunter, Name = "九除外",
                AbilityName = "封印の鎖",
                AbilityDescription = "相手の暗証番号の数字候補を0〜8に制限します。",
                Charges = 0, ConsumesTurn = false
            },
            new HeroInfo
            {
                Id = HeroId.Warrior, Name = "連続鉄",
                AbilityName = "連続突撃",
                AbilityDescription = "1試合につき1回、このターン終了後も続けて自分のターンを維持します。",
                Charges = 1, ConsumesTurn = false
            },
            new HeroInfo
            {
                Id = HeroId.Wizard, Name = "任無作",
                AbilityName = "千里眼",
                AbilityDescription = "1試合につき1回、お互いの暗証番号からランダムに1桁ずつ公開します。ターンを消費しません。",
                Charges = 1, ConsumesTurn = false
            },
        };

        public static HeroInfo Get(HeroId id) => Array.Find(All, h => h.Id == id);
    }
}
