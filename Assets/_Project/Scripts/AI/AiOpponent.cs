using System;
using System.Collections.Generic;
using LiarsBatting.Core;

namespace LiarsBatting.AI
{
    // Rule-based AI: no ML needed. It keeps the set of secrets still consistent
    // with everything it has been told about its own guesses, and picks guesses
    // that narrow that set down fastest (classic Mastermind-style minimax).
    public class AiOpponent
    {
        private List<int[]> _candidates;
        private readonly Random _rng = new Random();

        // Digits forced open by a trust/challenge call -- unlike a reported strike/
        // ball count these are certain, never lies, so any full candidate-pool reset
        // must re-apply them too instead of forgetting them.
        private readonly int[] _revealedDigits = new int[4];
        private readonly bool[] _revealedMask = new bool[4];
        private bool _guessingPoolExcludes9;

        // Above this size, a full minimax scan (candidates x candidates) is skipped
        // in favor of a random pick, so the AI never stalls the UI on its first move.
        private const int MinimaxCandidateCap = 200;

        public AiOpponent()
        {
            _candidates = GenerateAllSecrets();
        }

        public int RemainingCandidateCount => _candidates.Count;

        public static List<int[]> GenerateAllSecrets()
        {
            var result = new List<int[]>(5040);
            for (int a = 0; a <= 9; a++)
            for (int b = 0; b <= 9; b++)
            {
                if (b == a) continue;
                for (int c = 0; c <= 9; c++)
                {
                    if (c == a || c == b) continue;
                    for (int d = 0; d <= 9; d++)
                    {
                        if (d == a || d == b || d == c) continue;
                        result.Add(new[] { a, b, c, d });
                    }
                }
            }
            return result;
        }

        // exclude9: true when the PLAYER is DemonHunter, restricting the AI's own
        // secret pool to 0~8 (this only ever affects the AI's OWN secret -- the
        // guessing candidate pool below is a separate restriction, see
        // RestrictGuessingPoolExclude9, since DemonHunter restricts the OPPONENT).
        public int[] PickRandomSecret(bool exclude9 = false)
        {
            if (!exclude9) return CloneRandom();
            var pool = _candidates.FindAll(c => Array.IndexOf(c, 9) < 0);
            return CloneFrom(pool);
        }

        private int[] CloneRandom() => CloneFrom(_candidates);

        private int[] CloneFrom(List<int[]> pool)
        {
            var src = pool[_rng.Next(pool.Count)];
            return new[] { src[0], src[1], src[2], src[3] };
        }

        // Call once, right after construction, if the AI itself is DemonHunter --
        // the PLAYER's secret is guaranteed to exclude 9, so every future guess
        // (and every future reset) can safely rule out any candidate containing it.
        public void RestrictGuessingPoolExclude9()
        {
            _guessingPoolExcludes9 = true;
            _candidates = FreshCandidates();
        }

        // Call after the AI's OWN guess gets a (possibly false) result back, so its
        // remaining candidate set only contains secrets consistent with that answer.
        public void NarrowByOwnGuess(int[] guess, JudgeResult reportedResult)
        {
            var next = _candidates.FindAll(c => Judge.Evaluate(c, guess).Equals(reportedResult));
            // If the opponent lied enough to make the candidate set empty, the AI's
            // model of the world is broken -- reset rather than crash. This trades
            // some AI strength for robustness, which is the right call for a first
            // playable build.
            _candidates = next.Count > 0 ? next : FreshCandidates();
        }

        // Call when a trust/challenge call forces one of the opponent's digits into
        // the open. Unlike a reported strike/ball count this is certain, never a
        // lie, so it's remembered separately and reapplied even through a reset.
        public void NarrowByRevealedDigit(int index, int digit)
        {
            _revealedMask[index] = true;
            _revealedDigits[index] = digit;
            var next = _candidates.FindAll(c => c[index] == digit);
            _candidates = next.Count > 0 ? next : FreshCandidates();
        }

        private List<int[]> FreshCandidates()
        {
            var all = GenerateAllSecrets();
            if (_guessingPoolExcludes9) all = all.FindAll(c => Array.IndexOf(c, 9) < 0);
            for (int i = 0; i < 4; i++)
                if (_revealedMask[i]) all = all.FindAll(c => c[i] == _revealedDigits[i]);
            return all;
        }

        public int[] NextGuess()
        {
            if (_candidates.Count == 1) return _candidates[0];
            if (_candidates.Count > MinimaxCandidateCap) return CloneRandom();

            int[] best = null;
            int bestWorstCase = int.MaxValue;
            foreach (var guess in _candidates)
            {
                var bucketSizes = new Dictionary<int, int>();
                foreach (var secret in _candidates)
                {
                    var r = Judge.Evaluate(secret, guess);
                    int key = r.Strike * 10 + r.Ball;
                    bucketSizes.TryGetValue(key, out int count);
                    bucketSizes[key] = count + 1;
                }

                int worst = 0;
                foreach (var kv in bucketSizes)
                    if (kv.Value > worst) worst = kv.Value;

                if (worst < bestWorstCase)
                {
                    bestWorstCase = worst;
                    best = guess;
                }
            }
            return best;
        }

        // Defender-side bluff decision: the AI's own secret is being guessed, and it
        // must decide whether to lie about the true result before reporting it.
        // canLieAboutWin is Hunter's passive: everyone else must report a true
        // 4-strike honestly no matter how many tokens they have left.
        public bool ShouldLieAsDefender(JudgeResult trueResult, int lieTokensLeft, bool canLieAboutWin = false)
        {
            if (lieTokensLeft <= 0) return false;
            if (trueResult.IsWin) return canLieAboutWin && _rng.NextDouble() < 0.7;
            if (trueResult.Strike >= 2) return _rng.NextDouble() < 0.5;
            return _rng.NextDouble() < 0.15;
        }

        // Paladin's passive-use heuristic: heal as soon as it would actually help.
        public bool ShouldUsePaladinHeal(int lieTokensLeft, int charges)
            => charges > 0 && lieTokensLeft == 0;

        // Returns a plausible-but-false (strike, ball) pair, never a fake win.
        public JudgeResult FabricateResult(JudgeResult trueResult)
        {
            var options = Judge.PlausibleFakeResults(trueResult);
            return options[_rng.Next(options.Count)];
        }

        // Attacker-side call: should the AI treat this reported result as a lie
        // worth calling out, based on how few of its remaining candidates for the
        // opponent's secret would actually produce it? A result only a handful of
        // candidates could produce is the tell -- a real defender's answer is
        // always consistent with at least one true secret, but a fabricated one
        // often isn't consistent with much of anything the AI has narrowed down to.
        public bool ShouldChallengeAsAttacker(int[] guess, JudgeResult reportedResult)
        {
            if (_candidates.Count == 0) return false;
            int consistent = 0;
            foreach (var c in _candidates)
                if (Judge.Evaluate(c, guess).Equals(reportedResult)) consistent++;
            double ratio = (double)consistent / _candidates.Count;

            if (ratio < 0.05) return _rng.NextDouble() < 0.6;
            if (ratio < 0.2) return _rng.NextDouble() < 0.25;
            return _rng.NextDouble() < 0.05; // small baseline chance even when it looks plausible
        }

        // Picks a still-hidden digit position to open when the AI must reveal one.
        // Returns -1 if every position is already revealed. Also doubles as
        // "pick an open position" for Priest's query and Wizard's free reveal --
        // same operation, different callers.
        public int PickRevealIndex(bool[] revealed)
        {
            var open = new List<int>();
            for (int i = 0; i < revealed.Length; i++)
                if (!revealed[i]) open.Add(i);
            return open.Count > 0 ? open[_rng.Next(open.Count)] : -1;
        }

        // Rogue: purely informational and risk-free, so use it whenever available.
        public bool ShouldUseRogueDetect(int charges) => charges > 0;

        // Priest: worth a charge when there's still real uncertainty to resolve.
        public bool ShouldUsePriestQuery(int charges) => charges > 0 && _candidates.Count > 30 && _rng.NextDouble() < 0.35;

        // Priest asks "is position X digit Y?" -- picks a still-hidden position
        // and the digit most common among remaining candidates there, so it's an
        // informed guess rather than a random shot in the dark. Returns
        // (-1, -1) if every position is already revealed.
        public (int position, int digit) PickPriestQuery(bool[] revealed)
        {
            int position = PickRevealIndex(revealed);
            if (position < 0) return (-1, -1);

            var counts = new int[10];
            foreach (var c in _candidates) counts[c[position]]++;
            int bestDigit = 0;
            for (int d = 1; d <= 9; d++)
                if (counts[d] > counts[bestDigit]) bestDigit = d;
            return (position, bestDigit);
        }

        // Warrior: worth burning the once-per-game charge when a second guess in
        // a row is likely to close the game out.
        public bool ShouldUseWarriorExtraTurn(int charges) => charges > 0 && _candidates.Count <= 5;

        // Wizard: a free, no-downside peek -- use it as soon as it's available.
        public bool ShouldUseWizardReveal(int charges) => charges > 0;
    }
}
