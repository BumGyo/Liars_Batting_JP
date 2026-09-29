namespace LiarsBatting.Core
{
    // One resolved turn: the guess, the true judge result, and what was actually
    // reported to the guesser (may differ if a LIE TOKEN was used).
    public class TurnRecord
    {
        public readonly int[] Guess;
        public readonly JudgeResult TrueResult;
        public readonly JudgeResult ReportedResult;

        public bool WasLie => !TrueResult.Equals(ReportedResult);

        public TurnRecord(int[] guess, JudgeResult trueResult, JudgeResult reportedResult)
        {
            Guess = guess;
            TrueResult = trueResult;
            ReportedResult = reportedResult;
        }
    }
}
