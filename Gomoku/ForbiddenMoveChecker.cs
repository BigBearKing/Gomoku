namespace Gomoku;

using System.Text;

/// <summary>
/// Detects forbidden moves (禁手) for Black in Gomoku/Renju rules.
/// Only Black has forbidden moves to compensate for the first-move advantage.
/// </summary>
public static class ForbiddenMoveChecker
{
    private const int Size = GameBoard.Size;

    /// <summary>
    /// Checks whether placing a Black stone at (row, col) is a forbidden move.
    /// The board should NOT have the stone placed yet — this method temporarily places it.
    /// </summary>
    public static (bool IsForbidden, string Reason) CheckMove(Player[,] board, int row, int col)
    {
        // Only Black has forbidden moves
        board[row, col] = Player.Black;

        string reason = "";

        // 1. Check overline (长连) — 6 or more consecutive stones
        if (HasOverline(board, row, col))
        {
            reason = "禁手：长连（超过5子连珠）";
            board[row, col] = Player.None;
            return (true, reason);
        }

        // 2. Count four-patterns (四) — both 活四 and 冲四 count
        int fourCount = CountFours(board, row, col);
        if (fourCount >= 2)
        {
            reason = fourCount == 2 ? "禁手：双四" : $"禁手：{fourCount}四";
            board[row, col] = Player.None;
            return (true, reason);
        }

        // 3. Count open-three patterns (活三)
        int openThreeCount = CountOpenThrees(board, row, col);
        if (openThreeCount >= 2)
        {
            reason = openThreeCount == 2 ? "禁手：双活三" : $"禁手：{openThreeCount}活三";
            board[row, col] = Player.None;
            return (true, reason);
        }

        board[row, col] = Player.None;
        return (false, "");
    }

    /// <summary>
    /// Checks if the Black stone at (row, col) is part of an overline (6+).
    /// </summary>
    private static bool HasOverline(Player[,] board, int row, int col)
    {
        (int dr, int dc)[] dirs = { (0, 1), (1, 0), (1, 1), (1, -1) };
        foreach (var (dr, dc) in dirs)
        {
            int count = CountDirection(board, row, col, dr, dc);
            if (count >= 6) return true;
        }
        return false;
    }

    /// <summary>
    /// Counts how many "four" patterns (活四 or 冲四) the stone at (row, col) creates.
    /// </summary>
    private static int CountFours(Player[,] board, int row, int col)
    {
        (int dr, int dc)[] dirs = { (0, 1), (1, 0), (1, 1), (1, -1) };
        int count = 0;

        foreach (var (dr, dc) in dirs)
        {
            var (totalCount, openEnds) = AnalyzeDirection(board, row, col, dr, dc);
            // A "four" is: 4 consecutive, or if overline was already checked, count==4
            if (totalCount == 4 && openEnds >= 1)
                count++;
        }

        return count;
    }

    /// <summary>
    /// Counts how many open-three (活三) patterns the stone at (row, col) creates.
    /// Open three = 3 consecutive stones with both ends open.
    /// </summary>
    private static int CountOpenThrees(Player[,] board, int row, int col)
    {
        (int dr, int dc)[] dirs = { (0, 1), (1, 0), (1, 1), (1, -1) };
        int count = 0;

        foreach (var (dr, dc) in dirs)
        {
            var (totalCount, openEnds) = AnalyzeDirection(board, row, col, dr, dc);
            if (totalCount == 3 && openEnds == 2)
                count++;
        }

        return count;
    }

    /// <summary>
    /// Counts consecutive Black stones in both directions along (dr, dc) from (row, col),
    /// and counts how many ends are open (empty and in bounds).
    /// </summary>
    private static (int Count, int OpenEnds) AnalyzeDirection(Player[,] board, int row, int col, int dr, int dc)
    {
        int count = 1; // the stone itself

        // Forward direction
        int r = row + dr, c = col + dc;
        while (r >= 0 && r < Size && c >= 0 && c < Size && board[r, c] == Player.Black)
        {
            count++;
            r += dr;
            c += dc;
        }
        bool openEnd1 = r >= 0 && r < Size && c >= 0 && c < Size && board[r, c] == Player.None;

        // Backward direction
        r = row - dr;
        c = col - dc;
        while (r >= 0 && r < Size && c >= 0 && c < Size && board[r, c] == Player.Black)
        {
            count++;
            r -= dr;
            c -= dc;
        }
        bool openEnd2 = r >= 0 && r < Size && c >= 0 && c < Size && board[r, c] == Player.None;

        return (count, (openEnd1 ? 1 : 0) + (openEnd2 ? 1 : 0));
    }

    /// <summary>
    /// Counts consecutive Black stones in one direction from (row, col) along (dr, dc),
    /// excluding the stone at (row, col) itself.
    /// </summary>
    private static int CountDirection(Player[,] board, int row, int col, int dr, int dc)
    {
        // Count forward
        int count = 1; // the stone itself
        int r = row + dr, c = col + dc;
        while (r >= 0 && r < Size && c >= 0 && c < Size && board[r, c] == Player.Black)
        {
            count++;
            r += dr;
            c += dc;
        }

        // Count backward
        r = row - dr;
        c = col - dc;
        while (r >= 0 && r < Size && c >= 0 && c < Size && board[r, c] == Player.Black)
        {
            count++;
            r -= dr;
            c -= dc;
        }

        return count;
    }
}
