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
                Id = HeroId.Hunter, Name = "ハンター",
                AbilityName = "狩りの嘘",
                AbilityDescription = "正解（4ストライク）さえ嘘の結果として伝えられます。",
                Charges = -1, ConsumesTurn = false
            },
            new HeroInfo
            {
                Id = HeroId.Paladin, Name = "パラディン",
                AbilityName = "信念の回復",
                AbilityDescription = "嘘トークンを1つ回復します。",
                Charges = 1, ConsumesTurn = false
            },
            new HeroInfo
            {
                Id = HeroId.Rogue, Name = "ローグ",
                AbilityName = "真実看破",
                AbilityDescription = "1ゲームに2回、相手の返答が真実か嘘かを確認します。",
                Charges = 2, ConsumesTurn = false
            },
            new HeroInfo
            {
                Id = HeroId.Priest, Name = "プリースト",
                AbilityName = "一桁尋問",
                AbilityDescription = "1ゲームに2回、4桁全体ではなく指定した1桁の数字を直接尋ねます。ターンを消費します。",
                Charges = 2, ConsumesTurn = true
            },
            new HeroInfo
            {
                Id = HeroId.DemonHunter, Name = "デーモンハンター",
                AbilityName = "封印の鎖",
                AbilityDescription = "相手のパスワードに使える数字を0〜8に制限します。",
                Charges = 0, ConsumesTurn = false
            },
            new HeroInfo
            {
                Id = HeroId.Warrior, Name = "ウォリアー",
                AbilityName = "連続突撃",
                AbilityDescription = "1ゲームに1回、このターンの終了後もターンを維持します。",
                Charges = 1, ConsumesTurn = false
            },
            new HeroInfo
            {
                Id = HeroId.Wizard, Name = "ウィザード",
                AbilityName = "千里眼",
                AbilityDescription = "1ゲームに1回、両方のパスワードからランダムに1桁ずつ公開します。ターンを消費しません。",
                Charges = 1, ConsumesTurn = false
            },
        };

        public static HeroInfo Get(HeroId id) => Array.Find(All, h => h.Id == id);
    }
}
