using System;
using System.Collections.Generic;

namespace Gomoku;

/// <summary>
/// Manages the 15x15 Gomoku board state, win detection, and move history.
/// </summary>
public class GameBoard
{
    public const int Size = 15;
    public const int WinLength = 5;

    public Player[,] Board { get; private set; }

    private readonly Stack<Move> _moveHistory = new();
    private readonly Stack<Move> _redoStack = new();

    public Player CurrentPlayer { get; private set; } = Player.Black;

    /// <summary>True when the game has been won or drawn.</summary>
    public bool IsGameOver { get; internal set; }

    /// <summary>Player who won, or None.</summary>
    public Player Winner { get; internal set; } = Player.None;

    public IReadOnlyList<(int Row, int Col)>? WinningCells { get; private set; }

    public int MoveCount => _moveHistory.Count;

    public Move? LastMove => _moveHistory.Count > 0 ? _moveHistory.Peek() : null;

    /// <summary>All moves in chronological order (oldest first).</summary>
    public IEnumerable<Move> Moves
    {
        get
        {
            var list = _moveHistory.ToArray();
            Array.Reverse(list);
            return list;
        }
    }

    /// <summary>Whether Renju forbidden-move rules (禁手) are enforced for Black.</summary>
    public bool UseForbiddenRules { get; set; } = false;

    /// <summary>When a forbidden-move attempt is rejected, this contains the reason.</summary>
    public string? LastForbiddenReason { get; set; } = null;

    /// <summary>Event raised when a forbidden move is attempted.</summary>
    public event Action<string>? ForbiddenMoveAttempted;

    public GameBoard()
    {
        Board = new Player[Size, Size];
    }

    public GameBoard Clone()
    {
        var clone = new GameBoard
        {
            Board = (Player[,])Board.Clone(),
            CurrentPlayer = CurrentPlayer,
            IsGameOver = IsGameOver,
            Winner = Winner,
            WinningCells = WinningCells,
            UseForbiddenRules = UseForbiddenRules
        };
        return clone;
    }

    public Move? PlaceStone(int row, int col)
    {
        if (IsGameOver) return null;
        if (row < 0 || row >= Size || col < 0 || col >= Size) return null;
        if (Board[row, col] != Player.None) return null;

        // Check forbidden moves for Black when rules are enabled
        if (UseForbiddenRules && CurrentPlayer == Player.Black)
        {
            var (isForbidden, reason) = ForbiddenMoveChecker.CheckMove(Board, row, col);
            if (isForbidden)
            {
                LastForbiddenReason = reason;
                ForbiddenMoveAttempted?.Invoke(reason);
                return null;
            }
        }
        LastForbiddenReason = null;

        Board[row, col] = CurrentPlayer;
        var move = new Move(row, col, CurrentPlayer);
        _moveHistory.Push(move);
        _redoStack.Clear();

        var winCells = CheckWin(row, col, CurrentPlayer);
        if (winCells != null)
        {
            IsGameOver = true;
            Winner = CurrentPlayer;
            WinningCells = winCells;
        }
        else if (_moveHistory.Count == Size * Size)
        {
            IsGameOver = true;
            Winner = Player.None;
        }
        else
        {
            CurrentPlayer = CurrentPlayer == Player.Black ? Player.White : Player.Black;
        }

        return move;
    }

    public List<Move> Undo(int count = 1)
    {
        var undone = new List<Move>();
        for (int i = 0; i < count && _moveHistory.Count > 0; i++)
        {
            var move = _moveHistory.Pop();
            Board[move.Row, move.Col] = Player.None;
            _redoStack.Push(move);
            undone.Add(move);
        }

        if (undone.Count > 0)
        {
            IsGameOver = false;
            Winner = Player.None;
            WinningCells = null;
            CurrentPlayer = _moveHistory.Count > 0
                ? (_moveHistory.Peek().Player == Player.Black ? Player.White : Player.Black)
                : Player.Black;
        }

        return undone;
    }

    public Move? Redo()
    {
        if (_redoStack.Count == 0) return null;
        var move = _redoStack.Pop();
        return PlaceStone(move.Row, move.Col);
    }

    public bool CanRedo => _redoStack.Count > 0;

    public void Reset()
    {
        Array.Clear(Board, 0, Board.Length);
        _moveHistory.Clear();
        _redoStack.Clear();
        CurrentPlayer = Player.Black;
        IsGameOver = false;
        Winner = Player.None;
        WinningCells = null;
    }

    private List<(int, int)>? CheckWin(int row, int col, Player player)
    {
        (int dr, int dc)[] directions = { (0, 1), (1, 0), (1, 1), (1, -1) };

        foreach (var (dr, dc) in directions)
        {
            var cells = new List<(int, int)> { (row, col) };

            for (int i = 1; i < WinLength; i++)
            {
                int r = row + i * dr, c = col + i * dc;
                if (r < 0 || r >= Size || c < 0 || c >= Size) break;
                if (Board[r, c] == player) cells.Add((r, c));
                else break;
            }

            for (int i = 1; i < WinLength; i++)
            {
                int r = row - i * dr, c = col - i * dc;
                if (r < 0 || r >= Size || c < 0 || c >= Size) break;
                if (Board[r, c] == player) cells.Insert(0, (r, c));
                else break;
            }

            if (cells.Count >= WinLength)
                return cells;
        }

        return null;
    }
}
