using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;

namespace Gomoku;

public sealed partial class MainWindow : Window
{
    private const int BoardSize = 15;
    private const int CellSize = 36;
    private const int BoardMargin = 30;
    private const int StoneRadius = 15;

    private GameBoard _game = new();
    private GameMode _gameMode = GameMode.PvP;
    private Difficulty _difficulty = Difficulty.Medium;
    private int _blackWins;
    private int _whiteWins;
    private bool _isAiThinking;

    private readonly Dictionary<(int, int), Ellipse> _stoneElements = new();
    private Ellipse? _lastMoveIndicator;
    private readonly List<Ellipse> _winHighlightRings = new();

    private static readonly Color GridLineColor = Color.FromArgb(255, 139, 115, 85);
    private static readonly Color BoardBgColor = Color.FromArgb(255, 222, 184, 135);
    private static readonly Color StarPointColor = Color.FromArgb(255, 139, 115, 85);

    public MainWindow()
    {
        InitializeComponent();
        Activated += OnFirstActivated;
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            DrawBoard();
            BuildDifficultyStars();
            UpdateUiState();
        });
        DifficultyPanel.Loaded += (_, _) => BuildDifficultyStars();
    }


    private void DrawBoard()
    {
        BoardCanvas.Children.Clear();
        double gW = (BoardSize - 1) * CellSize;
        double gH = (BoardSize - 1) * CellSize;

        var bg = new Rectangle
        {
            Width = gW + BoardMargin * 2,
            Height = gH + BoardMargin * 2,
            Fill = new SolidColorBrush(BoardBgColor)
        };
        Canvas.SetLeft(bg, 0); Canvas.SetTop(bg, 0);
        BoardCanvas.Children.Add(bg);

        for (int i = 0; i < BoardSize; i++)
        {
            double o = BoardMargin + i * CellSize;
            BoardCanvas.Children.Add(new Line { X1 = BoardMargin, Y1 = o, X2 = BoardMargin + gW, Y2 = o, Stroke = new SolidColorBrush(GridLineColor), StrokeThickness = 1 });
            BoardCanvas.Children.Add(new Line { X1 = o, Y1 = BoardMargin, X2 = o, Y2 = BoardMargin + gH, Stroke = new SolidColorBrush(GridLineColor), StrokeThickness = 1 });
        }

        int[] sp = { 3, 7, 11 };
        foreach (int r in sp) foreach (int c in sp)
        {
            var star = new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(StarPointColor) };
            Canvas.SetLeft(star, BoardMargin + c * CellSize - 3);
            Canvas.SetTop(star, BoardMargin + r * CellSize - 3);
            BoardCanvas.Children.Add(star);
        }

        bg.PointerPressed += Board_PointerPressed;
    }

    private (int row, int col)? PixelToCell(double px, double py)
    {
        int col = (int)Math.Round((px - BoardMargin) / (double)CellSize);
        int row = (int)Math.Round((py - BoardMargin) / (double)CellSize);
        if (row < 0 || row >= BoardSize || col < 0 || col >= BoardSize) return null;
        double cx = BoardMargin + col * CellSize, cy = BoardMargin + row * CellSize;
        if (Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)) > CellSize * 0.45) return null;
        return (row, col);
    }

    private Ellipse DrawStone(int row, int col, Player player)
    {
        double cx = BoardMargin + col * CellSize, cy = BoardMargin + row * CellSize;
        var stone = new Ellipse { Width = StoneRadius * 2, Height = StoneRadius * 2 };
        if (player == Player.Black)
            stone.Fill = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.35, 0.35), Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5,
                GradientStops = { new GradientStop { Color = Color.FromArgb(255,80,80,80), Offset=0 }, new GradientStop { Color = Color.FromArgb(255,30,30,30), Offset=0.6 }, new GradientStop { Color = Color.FromArgb(255,5,5,5), Offset=1 } }
            };
        else
            stone.Fill = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.35, 0.35), Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5,
                GradientStops = { new GradientStop { Color = Color.FromArgb(255,255,255,255), Offset=0 }, new GradientStop { Color = Color.FromArgb(255,230,230,230), Offset=0.6 }, new GradientStop { Color = Color.FromArgb(255,180,180,180), Offset=1 } }
            };
        Canvas.SetLeft(stone, cx - StoneRadius); Canvas.SetTop(stone, cy - StoneRadius);
        AnimateStoneEnter(stone);
        BoardCanvas.Children.Add(stone);
        _stoneElements[(row, col)] = stone;
        return stone;
    }

    private void AnimateStoneEnter(Ellipse stone)
    {
        var st = new ScaleTransform { ScaleX = 0.3, ScaleY = 0.3, CenterX = StoneRadius, CenterY = StoneRadius };
        stone.RenderTransform = st;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double s = 0.3;
        timer.Tick += (_, _) => { s += 0.08; if (s >= 1) { s = 1; timer.Stop(); } st.ScaleX = s; st.ScaleY = s; };
        timer.Start();
    }

    private void ShowLastMoveIndicator(int row, int col)
    {
        RemoveLastMoveIndicator();
        double cx = BoardMargin + col * CellSize, cy = BoardMargin + row * CellSize;
        _lastMoveIndicator = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(_game.CurrentPlayer == Player.Black ? Colors.White : Colors.Black) };
        Canvas.SetLeft(_lastMoveIndicator, cx - 4); Canvas.SetTop(_lastMoveIndicator, cy - 4);
        BoardCanvas.Children.Add(_lastMoveIndicator);
    }

    private void RemoveLastMoveIndicator()
    {
        if (_lastMoveIndicator != null) { BoardCanvas.Children.Remove(_lastMoveIndicator); _lastMoveIndicator = null; }
    }

    private void HighlightWinningStones()
    {
        ClearWinHighlights();
        if (_game.WinningCells == null) return;
        foreach (var (r, c) in _game.WinningCells)
        {
            double cx = BoardMargin + c * CellSize, cy = BoardMargin + r * CellSize;
            var ring = new Ellipse { Width = (StoneRadius + 4) * 2, Height = (StoneRadius + 4) * 2, Stroke = new SolidColorBrush(Colors.Gold), StrokeThickness = 3 };
            Canvas.SetLeft(ring, cx - StoneRadius - 4); Canvas.SetTop(ring, cy - StoneRadius - 4);
            BoardCanvas.Children.Add(ring);
            _winHighlightRings.Add(ring);
        }
    }

    private void ClearWinHighlights()
    {
        foreach (var r in _winHighlightRings) BoardCanvas.Children.Remove(r);
        _winHighlightRings.Clear();
    }

    private void Board_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_game.IsGameOver || _isAiThinking) return;
        if (_gameMode == GameMode.PvAI && _game.CurrentPlayer == Player.White) return;
        var pt = e.GetCurrentPoint(BoardCanvas);
        var cell = PixelToCell(pt.Position.X, pt.Position.Y);
        if (cell == null) return;
        HandlePlayerMove(cell.Value.row, cell.Value.col);
    }

    private async void HandlePlayerMove(int row, int col)
    {
        var move = _game.PlaceStone(row, col);
        if (move == null) return;
        DrawStone(row, col, move.Value.Player);
        RemoveLastMoveIndicator();
        ShowLastMoveIndicator(row, col);
        AppendMoveHistory(move.Value);
        UpdateUiState();
        if (_game.IsGameOver) { HighlightWinningStones(); UpdateScore(); return; }
        if (_gameMode == GameMode.PvAI && _game.CurrentPlayer == Player.White)
            await DoAiMoveAsync();
    }

    private async Task DoAiMoveAsync()
    {
        _isAiThinking = true; UpdateUiState();
        var clone = _game.Clone(); var diff = _difficulty;
        var (r, c) = await Task.Run(() => AIPlayer.GetBestMove(clone, diff));
        var move = _game.PlaceStone(r, c);
        if (move == null) { _isAiThinking = false; UpdateUiState(); return; }
        DrawStone(r, c, move.Value.Player);
        RemoveLastMoveIndicator();
        ShowLastMoveIndicator(r, c);
        AppendMoveHistory(move.Value);
        _isAiThinking = false; UpdateUiState();
        if (_game.IsGameOver) { HighlightWinningStones(); UpdateScore(); }
    }

    private void UpdateUiState()
    {
        if (_game.IsGameOver)
            StatusText.Text = _game.Winner switch { Player.Black => "黑方获胜！", Player.White => "白方获胜！", _ => "平局！" };
        else if (_isAiThinking)
            StatusText.Text = "AI 思考中...";
        else
            StatusText.Text = _game.CurrentPlayer == Player.Black ? "黑方落子" : "白方落子";
        UndoButton.IsEnabled = !_isAiThinking && _game.MoveCount > 0;
        RedoButton.IsEnabled = !_isAiThinking && !_game.IsGameOver && _game.CanRedo;
        BlackScoreText.Text = $"{_blackWins} 胜";
        WhiteScoreText.Text = $"{_whiteWins} 胜";
    }

    private void UpdateScore()
    {
        if (_game.Winner == Player.Black) _blackWins++;
        else if (_game.Winner == Player.White) _whiteWins++;
    }

    private void AppendMoveHistory(Move move)
    {
        string icon = move.Player == Player.Black ? "●" : "○";
        MoveHistoryList.Items.Add($"{_game.MoveCount,2}. {icon} {(char)('A'+move.Col)}{move.Row+1}");
    }

    private void BuildDifficultyStars()
    {
        if (DifficultyStars == null) return;
        DifficultyStars.Children.Clear();
        int sc = (int)_difficulty + 1;
        for (int i = 0; i < 5; i++)
            DifficultyStars.Children.Add(new TextBlock
            {
                Text = i < sc ? "★" : "☆", FontSize = 20,
                Foreground = i < sc ? new SolidColorBrush(Color.FromArgb(255,255,200,30)) : new SolidColorBrush(Color.FromArgb(255,160,160,160))
            });
    }

    private void GameMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameModeSelector.SelectedItem is RadioButton rb && rb.Tag is string tag)
        {
            _gameMode = tag == "PvAI" ? GameMode.PvAI : GameMode.PvP;
            DifficultyPanel.Visibility = _gameMode == GameMode.PvAI ? Visibility.Visible : Visibility.Collapsed;
            ResetGame();
        }
    }

    private void Difficulty_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DifficultyCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _difficulty = tag switch { "Beginner" => Difficulty.Beginner, "Easy" => Difficulty.Easy, "Medium" => Difficulty.Medium, "Hard" => Difficulty.Hard, "Master" => Difficulty.Master, _ => Difficulty.Medium };
            BuildDifficultyStars();
            if (_gameMode == GameMode.PvAI && !_game.IsGameOver && _game.MoveCount > 0) ResetGame();
        }
    }

    private void NewGame_Click(object sender, RoutedEventArgs e) => ResetGame();

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_isAiThinking || _game.MoveCount == 0) return;
        int n = Math.Min(_gameMode == GameMode.PvAI ? 2 : 1, _game.MoveCount);
        var undone = _game.Undo(n);
        foreach (var m in undone)
            if (_stoneElements.TryGetValue((m.Row, m.Col), out var st)) { BoardCanvas.Children.Remove(st); _stoneElements.Remove((m.Row, m.Col)); }
        RemoveLastMoveIndicator(); ClearWinHighlights();
        var last = _game.LastMove;
        if (last != null) ShowLastMoveIndicator(last.Value.Row, last.Value.Col);
        for (int i = 0; i < undone.Count; i++) { if (MoveHistoryList.Items.Count > 0) MoveHistoryList.Items.RemoveAt(MoveHistoryList.Items.Count - 1); }
        UpdateUiState();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_isAiThinking || _game.IsGameOver || !_game.CanRedo) return;
        var m = _game.Redo(); if (m == null) return;
        DrawStone(m.Value.Row, m.Value.Col, m.Value.Player);
        RemoveLastMoveIndicator(); ShowLastMoveIndicator(m.Value.Row, m.Value.Col);
        AppendMoveHistory(m.Value); UpdateUiState();
    }

    private void ResetGame()
    {
        _game.Reset(); _isAiThinking = false;
        foreach (var st in _stoneElements.Values) BoardCanvas.Children.Remove(st);
        _stoneElements.Clear();
        RemoveLastMoveIndicator(); ClearWinHighlights();
        MoveHistoryList.Items.Clear(); UpdateUiState();
    }
}
