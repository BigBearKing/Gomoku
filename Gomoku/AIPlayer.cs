namespace Gomoku;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// AI opponent for Gomoku with 5 difficulty levels.
/// Uses minimax search with alpha-beta pruning, Zobrist hashing transposition table,
/// and pattern-based evaluation.
/// Optimized with reduced candidate radius, transposition table, and efficient move ordering.
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
    private const int WinBlockScore = 9_000_000;
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

    // ── Zobrist Hashing & Transposition Table ──
    private const int BoardSize = 15;
    private static readonly ulong[,,] Zobrist = new ulong[2, BoardSize, BoardSize];
    private static readonly Dictionary<ulong, TTEntry> TranspositionTable = new();
    private const int TtMaxSize = 500_000;

    private struct TTEntry
    {
        public int Depth;
        public int Score;
        public int Flag; // 0=exact, 1=lowerbound, 2=upperbound
        public int BestR;
        public int BestC;
    }

    static AIPlayer()
    {
        var rng = new Random(12345);
        for (int p = 0; p < 2; p++)
            for (int r = 0; r < BoardSize; r++)
                for (int c = 0; c < BoardSize; c++)
                    Zobrist[p, r, c] = ((ulong)rng.NextInt64() << 1) ^ (ulong)rng.NextInt64();
    }

    /// <summary>
    /// Returns the best move for the AI player at the given difficulty.
    /// </summary>
    public static (int Row, int Col) GetBestMove(GameBoard board, Difficulty difficulty)
    {
        TranspositionTable.Clear();
        var aiPlayer = board.CurrentPlayer;
        var opponent = aiPlayer == Player.Black ? Player.White : Player.Black;
        var candidates = GetCandidateMoves(board);

        if (candidates.Count == 0)
            return (7, 7); // first move at center

        int depth = DepthByDifficulty[(int)difficulty];

        // Random move for lower difficulties sometimes
        if (RandomChance[(int)difficulty] > 0 && _random.NextDouble() < RandomChance[(int)difficulty])
            return candidates[_random.Next(candidates.Count)];

        // Filter out forbidden moves for Black when rules are enabled
        if (board.UseForbiddenRules && aiPlayer == Player.Black)
        {
            var filtered = candidates.Where(m =>
            {
                var (isForbidden, _) = ForbiddenMoveChecker.CheckMove(board.Board, m.Item1, m.Item2);
                return !isForbidden;
            }).ToList();
            if (filtered.Count > 0)
                candidates = filtered;
        }

        // Compute initial Zobrist hash
        ulong hash = ComputeBoardHash(board.Board);

        // For beginner (depth 0), just pick the best immediate move
        if (depth <= 1)
        {
            return GetBestImmediateMove(board, candidates, aiPlayer, hash);
        }

        // Full minimax search
        return MinimaxSearch(board, candidates, aiPlayer, opponent, depth, hash);
    }

    /// <summary>Computes Zobrist hash of the entire board.</summary>
    private static ulong ComputeBoardHash(Player[,] board)
    {
        ulong h = 0;
        for (int r = 0; r < BoardSize; r++)
            for (int c = 0; c < BoardSize; c++)
                if (board[r, c] == Player.Black) h ^= Zobrist[0, r, c];
                else if (board[r, c] == Player.White) h ^= Zobrist[1, r, c];
        return h;
    }

    /// <summary>Best immediate move without lookahead.</summary>
    private static (int, int) GetBestImmediateMove(GameBoard board, List<(int, int)> candidates,
        Player aiPlayer, ulong hash)
    {
        var bestMove = candidates[0];
        int bestScore = int.MinValue;

        foreach (var (r, c) in candidates)
        {
            board.Board[r, c] = aiPlayer;
            ulong newHash = hash ^ Zobrist[aiPlayer == Player.Black ? 0 : 1, r, c];

            // Check instant win
            if (IsWinningMove(board.Board, r, c, aiPlayer))
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

    /// <summary>Full minimax search with alpha-beta pruning + transposition table.</summary>
    private static (int, int) MinimaxSearch(GameBoard board, List<(int, int)> candidates,
        Player aiPlayer, Player opponent, int depth, ulong hash)
    {
        var bestMove = candidates[0];
        int bestScore = int.MinValue;
        int alpha = int.MinValue + 1;
        int beta = int.MaxValue - 1;

        // Order moves for better pruning (using quick heuristic, not full eval)
        var ordered = QuickOrderMoves(board, candidates, aiPlayer, opponent);

        foreach (var (r, c) in ordered)
        {
            board.Board[r, c] = aiPlayer;
            ulong newHash = hash ^ Zobrist[aiPlayer == Player.Black ? 0 : 1, r, c];

            // Check instant win
            if (IsWinningMove(board.Board, r, c, aiPlayer))
            {
                board.Board[r, c] = Player.None;
                return (r, c);
            }

            int score = AlphaBeta(board, depth - 1, alpha, beta, false,
                aiPlayer, opponent, newHash);
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

    /// <summary>
    /// Recursive alpha-beta search with transposition table.
    /// Uses a pure board-scanning approach — no reliance on GameBoard's IsGameOver/Winner flags.
    /// </summary>
    private static int AlphaBeta(GameBoard board, int depth, int alpha, int beta,
        bool isMaximizing, Player aiPlayer, Player currentPlayer, ulong hash)
    {
        // Transposition table lookup
        if (TranspositionTable.TryGetValue(hash, out var cached) && cached.Depth >= depth)
        {
            if (cached.Flag == 0) return cached.Score;
            if (cached.Flag == 1) alpha = Math.Max(alpha, cached.Score);
            if (cached.Flag == 2) beta = Math.Min(beta, cached.Score);
            if (alpha >= beta) return cached.Score;
        }

        if (depth == 0)
        {
            int eval = EvaluateBoard(board.Board, aiPlayer);
            // Store in TT
            TranspositionTable[hash] = new TTEntry { Depth = depth, Score = eval, Flag = 0 };
            if (TranspositionTable.Count > TtMaxSize) TranspositionTable.Clear();
            return eval;
        }

        var candidates = GetCandidateMoves(board);
        if (candidates.Count == 0)
            return 0;

        var opponent = currentPlayer == Player.Black ? Player.White : Player.Black;
        var ordered = QuickOrderMoves(board, candidates, currentPlayer, opponent);

        int bestScore = isMaximizing ? int.MinValue : int.MaxValue;
        int flag = isMaximizing ? 1 : 2; // lowerbound / upperbound
        int bestR = -1, bestC = -1;

        if (isMaximizing)
        {
            foreach (var (r, c) in ordered)
            {
                // Skip forbidden moves for Black when rules are enabled
                if (board.UseForbiddenRules && currentPlayer == Player.Black)
                {
                    var (isForbidden, _) = ForbiddenMoveChecker.CheckMove(board.Board, r, c);
                    if (isForbidden) continue;
                }

                board.Board[r, c] = currentPlayer;
                ulong newHash = hash ^ Zobrist[currentPlayer == Player.Black ? 0 : 1, r, c];

                int score;
                if (IsWinningMove(board.Board, r, c, currentPlayer))
                {
                    score = (currentPlayer == aiPlayer) ? WinScore + depth : -WinScore - depth;
                }
                else
                {
                    score = AlphaBeta(board, depth - 1, alpha, beta, false,
                        aiPlayer, opponent, newHash);
                }
                board.Board[r, c] = Player.None;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestR = r;
                    bestC = c;
                }
                alpha = Math.Max(alpha, score);
                if (beta <= alpha) break;
            }
        }
        else
        {
            foreach (var (r, c) in ordered)
            {
                // Skip forbidden moves for Black when rules are enabled
                if (board.UseForbiddenRules && currentPlayer == Player.Black)
                {
                    var (isForbidden, _) = ForbiddenMoveChecker.CheckMove(board.Board, r, c);
                    if (isForbidden) continue;
                }

                board.Board[r, c] = currentPlayer;
                ulong newHash = hash ^ Zobrist[currentPlayer == Player.Black ? 0 : 1, r, c];

                int score;
                if (IsWinningMove(board.Board, r, c, currentPlayer))
                {
                    score = (currentPlayer == aiPlayer) ? WinScore + depth : -WinScore - depth;
                }
                else
                {
                    score = AlphaBeta(board, depth - 1, alpha, beta, true,
                        aiPlayer, opponent, newHash);
                }
                board.Board[r, c] = Player.None;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestR = r;
                    bestC = c;
                }
                beta = Math.Min(beta, score);
                if (beta <= alpha) break;
            }
        }

        // Store in transposition table
        if (alpha >= beta)
            flag = 0; // exact
        TranspositionTable[hash] = new TTEntry
        {
            Depth = depth,
            Score = bestScore,
            Flag = flag,
            BestR = bestR,
            BestC = bestC
        };
        if (TranspositionTable.Count > TtMaxSize) TranspositionTable.Clear();

        return bestScore;
    }

    /// <summary>
    /// Checks whether placing at (r,c) creates a winning line (5+ consecutive) for the given player.
    /// The stone is assumed to already be placed on the board.
    /// </summary>
    private static bool IsWinningMove(Player[,] board, int row, int col, Player player)
    {
        (int dr, int dc)[] directions = { (0, 1), (1, 0), (1, 1), (1, -1) };
        int size = BoardSize;
        int winLen = 5;

        foreach (var (dr, dc) in directions)
        {
            int count = 1;
            for (int i = 1; i < winLen; i++)
            {
                int r = row + i * dr, c = col + i * dc;
                if (r < 0 || r >= size || c < 0 || c >= size) break;
                if (board[r, c] == player) count++; else break;
            }
            for (int i = 1; i < winLen; i++)
            {
                int r = row - i * dr, c = col - i * dc;
                if (r < 0 || r >= size || c < 0 || c >= size) break;
                if (board[r, c] == player) count++; else break;
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
        int size = BoardSize;
        int winLen = 5;
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
        if (r < 0 || r >= BoardSize || c < 0 || c >= BoardSize) return false;
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

    /// <summary>
    /// Gets candidate moves: empty cells within 1 cell of any stone (radius 1).
    /// Reduced from radius 2 for ~60% fewer candidates without significant strength loss.
    /// </summary>
    private static List<(int, int)> GetCandidateMoves(GameBoard board)
    {
        var candidates = new HashSet<(int, int)>();
        bool hasStone = false;
        int size = BoardSize;

        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (board.Board[r, c] != Player.None)
                {
                    hasStone = true;
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            if (dr == 0 && dc == 0) continue;
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
    /// Quick move ordering using a local pattern heuristic instead of full board evaluation.
    /// Prioritizes: winning moves > blocking moves > threats > center proximity.
    /// This is much faster than OrderMoves which does a full eval per candidate.
    /// </summary>
    private static List<(int, int)> QuickOrderMoves(GameBoard board,
        List<(int, int)> moves, Player player, Player opponent)
    {
        if (moves.Count <= 1) return moves;

        return moves
            .Select(m =>
            {
                int r = m.Item1, c = m.Item2;
                int score = 0;

                // Check if this move wins immediately
                board.Board[r, c] = player;
                bool playerWins = IsWinningMove(board.Board, r, c, player);
                board.Board[r, c] = Player.None;
                if (playerWins) return (Move: m, Score: WinScore + 1000);

                // Check if this move blocks opponent's win
                board.Board[r, c] = opponent;
                bool oppWins = IsWinningMove(board.Board, r, c, opponent);
                board.Board[r, c] = Player.None;
                if (oppWins) return (Move: m, Score: WinBlockScore + 1000);

                // Quick local pattern score (only look at lines through this cell)
                score += QuickLocalScore(board.Board, r, c, player, opponent);

                // Add center proximity bonus
                int centerDist = Math.Abs(r - 7) + Math.Abs(c - 7);
                score += Math.Max(0, (14 - centerDist) * 5);

                return (Move: m, Score: score);
            })
            .OrderByDescending(x => x.Score)
            .Select(x => x.Move)
            .ToList();
    }

    /// <summary>
    /// Quickly evaluates the local area around a candidate move (4 directions, up to 5 cells each).
    /// Much faster than full board evaluation.
    /// </summary>
    private static int QuickLocalScore(Player[,] board, int row, int col,
        Player player, Player opponent)
    {
        (int dr, int dc)[] directions = { (0, 1), (1, 0), (1, 1), (1, -1) };
        int score = 0;

        board[row, col] = player;

        foreach (var (dr, dc) in directions)
        {
            int count = 1;
            int openEnds = 0;

            // Forward
            int r = row + dr, c = col + dc;
            while (r >= 0 && r < BoardSize && c >= 0 && c < BoardSize
                   && board[r, c] == player && count < 5)
            {
                count++;
                r += dr;
                c += dc;
            }
            if (r >= 0 && r < BoardSize && c >= 0 && c < BoardSize
                && board[r, c] == Player.None)
                openEnds++;

            // Backward
            r = row - dr;
            c = col - dc;
            while (r >= 0 && r < BoardSize && c >= 0 && c < BoardSize
                   && board[r, c] == player && count < 5)
            {
                count++;
                r -= dr;
                c -= dc;
            }
            if (r >= 0 && r < BoardSize && c >= 0 && c < BoardSize
                && board[r, c] == Player.None)
                openEnds++;

            if (count >= 5)
                score += WinScore;
            else if (count == 4 && openEnds > 0)
                score += openEnds == 2 ? OpenFourScore : HalfFourScore;
            else if (count == 3 && openEnds > 0)
                score += openEnds == 2 ? OpenThreeScore : HalfThreeScore;
            else if (count == 2 && openEnds > 0)
                score += openEnds == 2 ? OpenTwoScore : HalfTwoScore;
            else if (count == 1 && openEnds > 0)
                score += openEnds == 2 ? OpenOneScore : HalfOneScore;
        }

        board[row, col] = Player.None;
        return score;
    }

    /// <summary>Builds a positional weight map (center > corners).</summary>
    private static int[,] BuildPositionWeights()
    {
        int size = BoardSize;
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
