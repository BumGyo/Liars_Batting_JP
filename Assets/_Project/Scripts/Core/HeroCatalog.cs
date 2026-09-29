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
                Id = HeroId.Hunter, Name = "사냥꾼",
                AbilityName = "사냥의 거짓말",
                AbilityDescription = "정답(4스트라이크)까지도 거짓으로 알릴 수 있습니다.",
                Charges = -1, ConsumesTurn = false
            },
            new HeroInfo
            {
                Id = HeroId.Paladin, Name = "성기사",
                AbilityName = "신념의 회복",
                AbilityDescription = "LIE TOKEN을 1회 회복합니다.",
                Charges = 1, ConsumesTurn = false
            },
            new HeroInfo
            {
                Id = HeroId.Rogue, Name = "도적",
                AbilityName = "진실 간파",
                AbilityDescription = "게임당 2회, 상대 응답의 진실/거짓 여부를 확인합니다.",
                Charges = 2, ConsumesTurn = false
            },
            new HeroInfo
            {
                Id = HeroId.Priest, Name = "사제",
                AbilityName = "한 자리 심문",
                AbilityDescription = "게임당 2회, 4자리 전체 대신 원하는 한 자리 숫자를 직접 묻습니다. 턴을 소모합니다.",
                Charges = 2, ConsumesTurn = true
            },
            new HeroInfo
            {
                Id = HeroId.DemonHunter, Name = "악마사냥꾼",
                AbilityName = "봉인의 사슬",
                AbilityDescription = "상대의 비밀번호 자릿수 풀을 0~8로 제한합니다.",
                Charges = 0, ConsumesTurn = false
            },
            new HeroInfo
            {
                Id = HeroId.Warrior, Name = "전사",
                AbilityName = "연속 돌격",
                AbilityDescription = "게임당 1회, 이번 턴이 끝난 뒤에도 턴을 유지합니다.",
                Charges = 1, ConsumesTurn = false
            },
            new HeroInfo
            {
                Id = HeroId.Wizard, Name = "마법사",
                AbilityName = "천리안",
                AbilityDescription = "게임당 1회, 양쪽 비밀번호에서 무작위 한 자리씩 공개합니다. 턴을 소모하지 않습니다.",
                Charges = 1, ConsumesTurn = false
            },
        };

        public static HeroInfo Get(HeroId id) => Array.Find(All, h => h.Id == id);
    }
}
