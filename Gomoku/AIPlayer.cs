namespace Gomoku;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// AI opponent for Gomoku with 5 difficulty levels.
/// Uses minimax search with alpha-beta pruning and pattern-based evaluation.
/// </summary>
public static class AIPlayer
{
    private static readonly Random _random = new();

    /// <summary>Search depth per difficulty level.</summary>
    private static readonly int[] DepthByDifficulty = { 0, 2, 4, 6, 8 };

    /// <summary>Random-move probability per difficulty (for variety).</summary>
    private static readonly double[] RandomChance = { 0.60, 0.25, 0.08, 0.0, 0.0 };

    // Pattern scores for evaluation
    private const int WinScore = 10_000_000;
    private const int OpenFourScore = 500_000;
    private const int HalfFourScore = 50_000;
    private const int OpenThreeScore = 50_000;
    private const int HalfThreeScore = 5_000;
    private const int OpenTwoScore = 5_000;
    private const int HalfTwoScore = 500;
    private const int OpenOneScore = 500;
    private const int HalfOneScore = 50;

    // Positional bonus: center weight > edge
    private static readonly int[,] PositionWeight = BuildPositionWeights();

    /// <summary>
    /// Returns the best move for the AI player at the given difficulty.
    /// </summary>
    public static (int Row, int Col) GetBestMove(GameBoard board, Difficulty difficulty)
    {
        var aiPlayer = board.CurrentPlayer;
        var candidates = GetCandidateMoves(board);

        if (candidates.Count == 0)
            return (7, 7); // first move at center

        int depth = DepthByDifficulty[(int)difficulty];

        // Random move for lower difficulties sometimes
        if (RandomChance[(int)difficulty] > 0 && _random.NextDouble() < RandomChance[(int)difficulty])
        {
            return candidates[_random.Next(candidates.Count)];
        }

        // For beginner (depth 0), just pick the best immediate move
        if (depth <= 1)
        {
            return GetBestImmediateMove(board, candidates, aiPlayer);
        }

        // Full minimax search
        return MinimaxSearch(board, candidates, aiPlayer, depth);
    }

    /// <summary>Best immediate move without lookahead.</summary>
    private static (int, int) GetBestImmediateMove(GameBoard board, List<(int, int)> candidates, Player aiPlayer)
    {
        var bestMove = candidates[0];
        int bestScore = int.MinValue;

        foreach (var (r, c) in candidates)
        {
            board.Board[r, c] = aiPlayer;

            // Check instant win
            bool isWin = IsWinningMove(board, r, c, aiPlayer);
            if (isWin)
            {
                board.Board[r, c] = Player.None;
                return (r, c);
            }

            int score = EvaluateBoard(board.Board, aiPlayer);
            board.Board[r, c] = Player.None;

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = (r, c);
            }
        }

        return bestMove;
    }

    /// <summary>Full minimax search with alpha-beta pruning.</summary>
    private static (int, int) MinimaxSearch(GameBoard board, List<(int, int)> candidates, Player aiPlayer, int depth)
    {
        var opponent = aiPlayer == Player.Black ? Player.White : Player.Black;
        var bestMove = candidates[0];
        int bestScore = int.MinValue;
        int alpha = int.MinValue + 1;
        int beta = int.MaxValue - 1;

        // Order moves for better pruning
        var ordered = OrderMoves(board, candidates, aiPlayer);

        foreach (var (r, c) in ordered)
        {
            board.Board[r, c] = aiPlayer;

            // Check instant win
            if (IsWinningMove(board, r, c, aiPlayer))
            {
                board.Board[r, c] = Player.None;
                return (r, c);
            }

            int score = Minimax(board, depth - 1, alpha, beta, false, aiPlayer, opponent);
            board.Board[r, c] = Player.None;

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = (r, c);
            }
            alpha = Math.Max(alpha, score);
        }

        return bestMove;
    }

    /// <summary>Recursive minimax with alpha-beta pruning.</summary>
    private static int Minimax(GameBoard board, int depth, int alpha, int beta, bool isMaximizing,
        Player aiPlayer, Player currentPlayer)
    {
        // Terminal check
        if (board.IsGameOver)
        {
            if (board.Winner == aiPlayer) return WinScore + depth;
            if (board.Winner != Player.None) return -WinScore - depth;
            return 0; // draw
        }

        if (depth == 0)
            return EvaluateBoard(board.Board, aiPlayer);

        var candidates = GetCandidateMoves(board);
        if (candidates.Count == 0)
            return 0;

        // Order moves for this depth too
        var ordered = OrderMoves(board, candidates, currentPlayer);

        if (isMaximizing)
        {
            int maxScore = int.MinValue;
            foreach (var (r, c) in ordered)
            {
                board.Board[r, c] = currentPlayer;
                int score;
                if (IsWinningMove(board, r, c, currentPlayer))
                {
                    score = (currentPlayer == aiPlayer) ? WinScore + depth : -WinScore - depth;
                }
                else
                {
                    board.IsGameOver = false;
                    board.Winner = Player.None;
                    score = Minimax(board, depth - 1, alpha, beta, false, aiPlayer,
                        currentPlayer == Player.Black ? Player.White : Player.Black);
                }
                board.Board[r, c] = Player.None;

                // Restore terminal state if was over
                if (board.IsGameOver)
                {
                    board.Board[r, c] = Player.None;
                }

                board.IsGameOver = false;
                board.Winner = Player.None;
                maxScore = Math.Max(maxScore, score);
                alpha = Math.Max(alpha, score);
                if (beta <= alpha) break;
            }
            return maxScore;
        }
        else
        {
            int minScore = int.MaxValue;
            foreach (var (r, c) in ordered)
            {
                board.Board[r, c] = currentPlayer;
                int score;
                if (IsWinningMove(board, r, c, currentPlayer))
                {
                    score = (currentPlayer == aiPlayer) ? WinScore + depth : -WinScore - depth;
                }
                else
                {
                    board.IsGameOver = false;
                    board.Winner = Player.None;
                    score = Minimax(board, depth - 1, alpha, beta, true, aiPlayer,
                        currentPlayer == Player.Black ? Player.White : Player.Black);
                }
                board.Board[r, c] = Player.None;
                board.IsGameOver = false;
                board.Winner = Player.None;
                minScore = Math.Min(minScore, score);
                beta = Math.Min(beta, score);
                if (beta <= alpha) break;
            }
            return minScore;
        }
    }

    /// <summary>
    /// Checks whether placing at (r,c) creates a winning line for the given player.
    /// The stone is assumed to already be placed on the board.
    /// </summary>
    private static bool IsWinningMove(GameBoard board, int row, int col, Player player)
    {
        (int dr, int dc)[] directions = { (0, 1), (1, 0), (1, 1), (1, -1) };
        int size = GameBoard.Size;
        int winLen = GameBoard.WinLength;

        foreach (var (dr, dc) in directions)
        {
            int count = 1;
            for (int i = 1; i < winLen; i++)
            {
                int r = row + i * dr, c = col + i * dc;
                if (r < 0 || r >= size || c < 0 || c >= size) break;
                if (board.Board[r, c] == player) count++; else break;
            }
            for (int i = 1; i < winLen; i++)
            {
                int r = row - i * dr, c = col - i * dc;
                if (r < 0 || r >= size || c < 0 || c >= size) break;
                if (board.Board[r, c] == player) count++; else break;
            }
            if (count >= winLen) return true;
        }
        return false;
    }

    /// <summary>
    /// Evaluates the board from the perspective of aiPlayer.
    /// Positive scores favor aiPlayer; negative scores favor the opponent.
    /// </summary>
    private static int EvaluateBoard(Player[,] board, Player aiPlayer)
    {
        var opponent = aiPlayer == Player.Black ? Player.White : Player.Black;
        int size = GameBoard.Size;
        int winLen = GameBoard.WinLength;
        int score = 0;

        // Evaluate every possible line of 5 cells
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                // Horizontal
                if (c + winLen <= size)
                    score += EvaluateLine(board, r, c, 0, 1, aiPlayer, opponent, winLen);
                // Vertical
                if (r + winLen <= size)
                    score += EvaluateLine(board, r, c, 1, 0, aiPlayer, opponent, winLen);
                // Diagonal
                if (r + winLen <= size && c + winLen <= size)
                    score += EvaluateLine(board, r, c, 1, 1, aiPlayer, opponent, winLen);
                // Anti-diagonal
                if (r + winLen <= size && c + 1 >= winLen)
                    score += EvaluateLine(board, r, c, 1, -1, aiPlayer, opponent, winLen);
            }
        }

        // Add positional bonuses
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (board[r, c] == aiPlayer)
                    score += PositionWeight[r, c];
                else if (board[r, c] == opponent)
                    score -= PositionWeight[r, c];
            }
        }

        return score;
    }

    /// <summary>Evaluates one 5-cell line.</summary>
    private static int EvaluateLine(Player[,] board, int startR, int startC, int dr, int dc,
        Player aiPlayer, Player opponent, int winLen)
    {
        int aiCount = 0, oppCount = 0;
        for (int i = 0; i < winLen; i++)
        {
            var p = board[startR + i * dr, startC + i * dc];
            if (p == aiPlayer) aiCount++;
            else if (p == opponent) oppCount++;
        }

        // Mixed line — no one can win here
        if (aiCount > 0 && oppCount > 0) return 0;

        if (aiCount > 0)
        {
            // Check openness
            bool openStart = IsOpen(board, startR - dr, startC - dc);
            bool openEnd = IsOpen(board, startR + winLen * dr, startC + winLen * dc);
            return ScorePattern(aiCount, openStart, openEnd);
        }
        else if (oppCount > 0)
        {
            bool openStart = IsOpen(board, startR - dr, startC - dc);
            bool openEnd = IsOpen(board, startR + winLen * dr, startC + winLen * dc);
            return -ScorePattern(oppCount, openStart, openEnd);
        }

        return 0;
    }

    /// <summary>Checks if the cell at (r,c) is open (empty and in bounds).</summary>
    private static bool IsOpen(Player[,] board, int r, int c)
    {
        if (r < 0 || r >= GameBoard.Size || c < 0 || c >= GameBoard.Size) return false;
        return board[r, c] == Player.None;
    }

    /// <summary>Returns a score for a pattern of consecutive stones.</summary>
    private static int ScorePattern(int count, bool openStart, bool openEnd)
    {
        bool bothOpen = openStart && openEnd;
        bool oneOpen = openStart || openEnd;

        return count switch
        {
            5 => WinScore,
            4 => bothOpen ? OpenFourScore : (oneOpen ? HalfFourScore : 0),
            3 => bothOpen ? OpenThreeScore : (oneOpen ? HalfThreeScore : 0),
            2 => bothOpen ? OpenTwoScore : (oneOpen ? HalfTwoScore : 0),
            1 => bothOpen ? OpenOneScore : (oneOpen ? HalfOneScore : 0),
            _ => 0
        };
    }

    /// <summary>Gets candidate moves: empty cells within 2 cells of any stone.</summary>
    private static List<(int, int)> GetCandidateMoves(GameBoard board)
    {
        var candidates = new HashSet<(int, int)>();
        bool hasStone = false;
        int size = GameBoard.Size;

        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (board.Board[r, c] != Player.None)
                {
                    hasStone = true;
                    for (int dr = -2; dr <= 2; dr++)
                    {
                        for (int dc = -2; dc <= 2; dc++)
                        {
                            int nr = r + dr, nc = c + dc;
                            if (nr >= 0 && nr < size && nc >= 0 && nc < size
                                && board.Board[nr, nc] == Player.None)
                            {
                                candidates.Add((nr, nc));
                            }
                        }
                    }
                }
            }
        }

        if (!hasStone)
            candidates.Add((7, 7));

        return candidates.ToList();
    }

    /// <summary>
    /// Orders candidate moves by an immediate-evaluation heuristic for better alpha-beta pruning.
    /// </summary>
    private static List<(int, int)> OrderMoves(GameBoard board, List<(int, int)> moves, Player player)
    {
        return moves
            .Select(m =>
            {
                board.Board[m.Item1, m.Item2] = player;
                int s = EvaluateBoard(board.Board, player);
                board.Board[m.Item1, m.Item2] = Player.None;
                return (Move: m, Score: s);
            })
            .OrderByDescending(x => x.Score)
            .Select(x => x.Move)
            .ToList();
    }

    /// <summary>Builds a positional weight map (center > corners).</summary>
    private static int[,] BuildPositionWeights()
    {
        int size = GameBoard.Size;
        var weights = new int[size, size];
        int center = size / 2;

        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                int dist = Math.Abs(r - center) + Math.Abs(c - center);
                weights[r, c] = Math.Max(0, (2 * center - dist) * 10);
            }
        }
        return weights;
    }
}
