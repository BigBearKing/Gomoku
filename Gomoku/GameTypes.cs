namespace Gomoku;

/// <summary>
/// Represents a player or empty cell on the board.
/// </summary>
public enum Player
{
    None = 0,
    Black = 1,
    White = 2
}

/// <summary>
/// Game mode: Player vs Player or Player vs AI.
/// </summary>
public enum GameMode
{
    PvP,
    PvAI
}

/// <summary>
/// AI difficulty levels for human-vs-AI mode.
/// </summary>
public enum Difficulty
{
    Beginner = 0,
    Easy = 1,
    Medium = 2,
    Hard = 3,
    Master = 4
}

/// <summary>
/// Records a single move on the board.
/// </summary>
public readonly struct Move
{
    public int Row { get; }
    public int Col { get; }
    public Player Player { get; }

    public Move(int row, int col, Player player)
    {
        Row = row;
        Col = col;
        Player = player;
    }

    public override string ToString() => $"{(char)('A' + Col)}{Row + 1}";
}
