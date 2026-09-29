using System;
using System.Collections.Generic;

namespace LiarsBatting.Core
{
    public readonly struct JudgeResult : IEquatable<JudgeResult>
    {
        public readonly int Strike;
        public readonly int Ball;

        public JudgeResult(int strike, int ball)
        {
            Strike = strike;
            Ball = ball;
        }

        public bool IsOut => Strike == 0 && Ball == 0;
        public bool IsWin => Strike == 4;

        public bool Equals(JudgeResult other) => Strike == other.Strike && Ball == other.Ball;
        public override bool Equals(object obj) => obj is JudgeResult other && Equals(other);
        public override int GetHashCode() => Strike * 10 + Ball;
        public override string ToString() => IsOut ? "OUT" : $"{Strike}S {Ball}B";
    }

    public static class Judge
    {
        public const int Digits = 4;

        public static JudgeResult Evaluate(int[] secret, int[] guess)
        {
            int strike = 0, ball = 0;
            for (int i = 0; i < Digits; i++)
            {
                if (guess[i] == secret[i]) strike++;
                else if (Array.IndexOf(secret, guess[i]) >= 0) ball++;
            }
            return new JudgeResult(strike, ball);
        }

        public static bool IsValidSecret(int[] digits)
        {
            if (digits == null || digits.Length != Digits) return false;
            for (int i = 0; i < Digits; i++)
            {
                if (digits[i] < 0 || digits[i] > 9) return false;
                for (int j = i + 1; j < Digits; j++)
                    if (digits[i] == digits[j]) return false;
            }
            return true;
        }

        // Every (strike, ball) pair a defender could plausibly claim instead of
        // trueResult when spending a LIE TOKEN. Excludes a fake win and excludes
        // 3S1B, which is impossible with 4 unique digits and would give the bluff
        // away immediately.
        public static List<JudgeResult> PlausibleFakeResults(JudgeResult trueResult)
        {
            var options = new List<JudgeResult>();
            for (int s = 0; s <= 3; s++)
            for (int b = 0; b <= 4 - s; b++)
            {
                if (s == 3 && b == 1) continue;
                var candidate = new JudgeResult(s, b);
                if (!candidate.Equals(trueResult)) options.Add(candidate);
            }
            return options;
        }
    }
}
