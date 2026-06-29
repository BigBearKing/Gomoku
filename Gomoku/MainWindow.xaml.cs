using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Linq;
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
    private Player _humanPlayer = Player.Black;
    private int _blackWins;
    private int _whiteWins;
    private bool _isAiThinking;
    private string _currentPageName = "GomokuPage";

    private bool _isSidebarCollapsed;
    private const double SidebarExpandedWidth = 200;
    private const double SidebarCollapsedWidth = 52;
    private const double SidebarAnimationStep = 12;
    private DispatcherQueueTimer? _sidebarAnimTimer;

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

                        // Subscribe to forbidden move event
                        _game.ForbiddenMoveAttempted += reason =>
                        {
                            _ = DispatcherQueue.TryEnqueue(() =>
                            {
                                StatusText.Text = $"{reason}";
                            });
                        };

                        // Select 
            VisualStateManager.GoToState(NavGomoku, "Active", false);
        });
        DifficultyPanel.Loaded += (_, _) => BuildDifficultyStars();
    }

    /// <summary>
    /// Toggles sidebar between collapsed (icon-only) and expanded (icon+label) modes.
    /// </summary>
    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;

        double targetWidth = _isSidebarCollapsed ? SidebarCollapsedWidth : SidebarExpandedWidth;

        // Toggle labels and alignment immediately
        SidebarTitleArea.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;

        // Center hamburger button when collapsed, left-align when expanded
        SidebarToggle.HorizontalAlignment = _isSidebarCollapsed
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Left;

        foreach (var (btn, label) in new (Button, TextBlock)[]
        {
            (NavHome, NavHomeLabel),
            (NavGomoku, NavGomokuLabel),
            (NavGo, NavGoLabel),
            (NavChess, NavChessLabel),
            (NavSettings, NavSettingsLabel)
        })
        {
            label.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            btn.Width = _isSidebarCollapsed ? 44 : 176;
            btn.Padding = _isSidebarCollapsed ? new Thickness(0) : new Thickness(16, 0, 16, 0);
            btn.HorizontalContentAlignment = _isSidebarCollapsed
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left;
        }

        // Animate width with a simple timer
        _sidebarAnimTimer?.Stop();
        _sidebarAnimTimer = DispatcherQueue.CreateTimer();
        _sidebarAnimTimer.Interval = TimeSpan.FromMilliseconds(10);

        _sidebarAnimTimer.Tick += (_, _) =>
        {
            double current = SidebarPanel.Width;
            double step = SidebarAnimationStep;
            double next;

            if (_isSidebarCollapsed)
            {
                // Shrinking
                next = Math.Max(targetWidth, current - step);
            }
            else
            {
                // Expanding
                next = Math.Min(targetWidth, current + step);
            }

            SidebarPanel.Width = next;

            if (Math.Abs(next - targetWidth) < 0.5)
            {
                SidebarPanel.Width = targetWidth;
                _sidebarAnimTimer.Stop();
            }
        };

        _sidebarAnimTimer.Start();
    }

    /// <summary>
    /// Sidebar navigation: switches the visible content panel.
    /// </summary>
    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string pageName) return;
        if (pageName == _currentPageName) return;

        // Reset all nav buttons to normal state
        foreach (var navBtn in new Button[] { NavHome, NavGomoku, NavGo, NavChess, NavSettings })
            VisualStateManager.GoToState(navBtn, "Normal", false);

        // Highlight the active one
        VisualStateManager.GoToState(btn, "Active", false);

        // Switch visible page
        _currentPageName = pageName;
        HomePage.Visibility = pageName == "HomePage" ? Visibility.Visible : Visibility.Collapsed;
        GomokuPage.Visibility = pageName == "GomokuPage" ? Visibility.Visible : Visibility.Collapsed;
        GoPage.Visibility = pageName == "GoPage" ? Visibility.Visible : Visibility.Collapsed;
        ChessPage.Visibility = pageName == "ChessPage" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = pageName == "SettingsPage" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DrawBoard()
    {
        BoardCanvas.Children.Clear();
        _stoneElements.Clear();

        int totalSize = (BoardSize - 1) * CellSize;
        int offset = BoardMargin;

        // Draw grid lines
        for (int i = 0; i < BoardSize; i++)
        {
            // Horizontal
            var hLine = new Line
            {
                X1 = offset,
                Y1 = offset + i * CellSize,
                X2 = offset + totalSize,
                Y2 = offset + i * CellSize,
                Stroke = new SolidColorBrush(GridLineColor),
                StrokeThickness = 1
            };
            BoardCanvas.Children.Add(hLine);

            // Vertical
            var vLine = new Line
            {
                X1 = offset + i * CellSize,
                Y1 = offset,
                X2 = offset + i * CellSize,
                Y2 = offset + totalSize,
                Stroke = new SolidColorBrush(GridLineColor),
                StrokeThickness = 1
            };
            BoardCanvas.Children.Add(vLine);
        }

        // Draw star points (天元 and 星位)
        var starPoints = new (int, int)[] { (3, 3), (3, 11), (7, 7), (11, 3), (11, 11) };
        foreach (var (r, c) in starPoints)
        {
            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = new SolidColorBrush(StarPointColor)
            };
            Canvas.SetLeft(dot, offset + c * CellSize - 3);
            Canvas.SetTop(dot, offset + r * CellSize - 3);
            BoardCanvas.Children.Add(dot);
        }

        // Draw existing stones
        for (int r = 0; r < BoardSize; r++)
        {
            for (int c = 0; c < BoardSize; c++)
            {
                if (_game.Board[r, c] != Player.None)
                    AddStoneElement(r, c, _game.Board[r, c]);
            }
        }
    }

    private void AddStoneElement(int row, int col, Player player)
    {
        int offset = BoardMargin;
        var stone = new Ellipse
        {
            Width = StoneRadius * 2,
            Height = StoneRadius * 2,
            Stroke = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
            StrokeThickness = 0.5
        };

        if (player == Player.Black)
        {
            stone.Fill = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.35, 0.35),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(255, 80, 80, 80), Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(255, 10, 10, 10), Offset = 1 }
                }
            };
        }
        else
        {
            stone.Fill = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.35, 0.35),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(255, 255, 255, 255), Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(255, 200, 200, 200), Offset = 1 }
                }
            };
        }

        Canvas.SetLeft(stone, offset + col * CellSize - StoneRadius);
        Canvas.SetTop(stone, offset + row * CellSize - StoneRadius);
        BoardCanvas.Children.Add(stone);
        _stoneElements[(row, col)] = stone;
    }

    private void BuildDifficultyStars()
    {
        if (DifficultyStars == null) return;
        DifficultyStars.Children.Clear();
        int starCount = (int)_difficulty + 1;
        for (int i = 0; i < 5; i++)
        {
            DifficultyStars.Children.Add(new TextBlock
            {
                Text = i < starCount ? "★" : "☆",
                FontSize = 18,
                Foreground = i < starCount
                    ? new SolidColorBrush(Color.FromArgb(255, 255, 200, 30))
                    : new SolidColorBrush(Color.FromArgb(255, 160, 160, 160))
            });
        }
    }

    private void UpdateUiState()
    {
        // Update scores
        BlackScoreText.Text = $"{_blackWins} 胜";
        WhiteScoreText.Text = $"{_whiteWins} 胜";

        // Update status
        if (_game.IsGameOver)
        {
            if (_game.Winner == Player.Black)
                StatusText.Text = "黑方获胜！";
            else if (_game.Winner == Player.White)
                StatusText.Text = "白方获胜！";
            else
                StatusText.Text = "平局！";
        }
        else
        {
            StatusText.Text = _game.CurrentPlayer == Player.Black ? "黑方落子" : "白方落子";
        }

        // Update button states
        UndoButton.IsEnabled = _game.MoveCount > 0 && !_isAiThinking;
        RedoButton.IsEnabled = _game.CanRedo && !_isAiThinking;
        NewGameButton.IsEnabled = !_isAiThinking;
    }

    private void GameMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameModeSelector.SelectedItem is RadioButton rb && rb.Tag is string tag)
        {
            _gameMode = tag == "PvAI" ? GameMode.PvAI : GameMode.PvP;
            DifficultyPanel.Visibility = _gameMode == GameMode.PvAI
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void Difficulty_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DifficultyCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
        {
            _difficulty = tag switch
            {
                "Beginner" => Difficulty.Beginner,
                "Easy" => Difficulty.Easy,
                "Medium" => Difficulty.Medium,
                "Hard" => Difficulty.Hard,
                "Master" => Difficulty.Master,
                _ => Difficulty.Medium
            };
            BuildDifficultyStars();
        }
    }

    /// <summary>Handles the player color selection in PvAI mode.</summary>
    private void ColorSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ColorSelector.SelectedItem is RadioButton rb && rb.Tag is string tag)
        {
            _humanPlayer = tag == "Black" ? Player.Black : Player.White;
        }
    }

    /// <summary>Handles the forbidden rules toggle switch.</summary>
    private void ForbiddenRulesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _game.UseForbiddenRules = ForbiddenRulesToggle.IsOn;
        StatusText.Text = _game.UseForbiddenRules
            ? "禁手规则已开启"
            : "禁手规则已关闭";
        // Reset status after 2 seconds
        _ = Task.Delay(2000).ContinueWith(_ =>
            DispatcherQueue.TryEnqueue(() => UpdateUiState()));
    }

    private async void NewGame_Click(object sender, RoutedEventArgs e)
    {
        _game.Reset();
        _game.UseForbiddenRules = ForbiddenRulesToggle.IsOn;
        BoardCanvas.Children.Clear();
        _stoneElements.Clear();
        _lastMoveIndicator = null;
        _winHighlightRings.Clear();
        DrawBoard();
        UpdateUiState();
        MoveHistoryList.Items.Clear();

        // If PvAI and human plays White, AI makes the first move as Black
        if (_gameMode == GameMode.PvAI && _humanPlayer == Player.White)
        {
            _isAiThinking = true;
            UpdateUiState();
            _ = MakeAiMoveAsync();
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_game.MoveCount == 0) return;

        if (_gameMode == GameMode.PvAI)
        {
            _game.Undo(2); // Undo both AI move + player move
        }
        else
        {
            _game.Undo(1); // Undo one move in PvP
        }

        RedrawAllStones();
        UpdateUiState();
        UpdateMoveHistory();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        var move = _game.Redo();
        if (move != null)
        {
            RedrawAllStones();
            UpdateUiState();
            UpdateMoveHistory();
        }
    }

    private void RedrawAllStones()
    {
        BoardCanvas.Children.Clear();
        _stoneElements.Clear();
        _lastMoveIndicator = null;
        _winHighlightRings.Clear();
        DrawBoard();
    }

    private void UpdateMoveHistory()
    {
        MoveHistoryList.Items.Clear();
        var moves = _game.Moves.ToList();
        for (int i = moves.Count - 1; i >= 0; i--)
        {
            var move = moves[i];
            int moveNumber = i + 1;
            string color = move.Player == Player.Black ? "\u25CF" : "\u25CB";
            MoveHistoryList.Items.Add($"{moveNumber}. {color} {move}");
        }
    }

    // Board click handler for placing stones (PointerPressed for zero-delay response)
    private void BoardCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_isAiThinking || _game.IsGameOver) return;

        // In PvAI mode, only allow clicks when it's the human's turn
        if (_gameMode == GameMode.PvAI && _game.CurrentPlayer != _humanPlayer) return;

        var point = e.GetCurrentPoint(BoardCanvas).Position;
        int offset = BoardMargin;
        int col = (int)Math.Round((point.X - offset) / CellSize);
        int row = (int)Math.Round((point.Y - offset) / CellSize);

        if (row < 0 || row >= BoardSize || col < 0 || col >= BoardSize) return;
        if (_game.Board[row, col] != Player.None) return;

        e.Handled = true;

        // Place player's stone
        var move = _game.PlaceStone(row, col);
        if (move == null) return;

        AddStoneElement(row, col, move.Value.Player);
        UpdateMoveHistory();

        if (_game.IsGameOver)
        {
            UpdateUiState();
            HighlightWinningCells();
            UpdateScore();
            return;
        }

        UpdateUiState();

        // AI move in PvAI mode
        if (_gameMode == GameMode.PvAI && !_game.IsGameOver)
        {
            _isAiThinking = true;
            UpdateUiState();
            _ = MakeAiMoveAsync();
        }
    }

    private async Task MakeAiMoveAsync()
    {
        // Small delay so the UI updates before AI computation
        await Task.Delay(100);

        // Clone the board and run AI computation on a background thread
        // to keep the UI responsive during search
        var boardClone = _game.Clone();
        var (aiRow, aiCol) = await Task.Run(() => AIPlayer.GetBestMove(boardClone, _difficulty));

        var aiMove = _game.PlaceStone(aiRow, aiCol);

        // Continuation runs on UI thread automatically via captured SynchronizationContext
        if (aiMove != null)
        {
            AddStoneElement(aiRow, aiCol, aiMove.Value.Player);
            UpdateMoveHistory();

            if (_game.IsGameOver)
            {
                HighlightWinningCells();
                UpdateScore();
            }
        }

        _isAiThinking = false;
        UpdateUiState();
    }

    private void HighlightWinningCells()
    {
        if (_game.WinningCells == null) return;

        int offset = BoardMargin;
        foreach (var (r, c) in _game.WinningCells)
        {
            var ring = new Ellipse
            {
                Width = StoneRadius * 2 + 4,
                Height = StoneRadius * 2 + 4,
                Stroke = new SolidColorBrush(Colors.Gold),
                StrokeThickness = 3,
                Fill = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))
            };
            Canvas.SetLeft(ring, offset + c * CellSize - StoneRadius - 2);
            Canvas.SetTop(ring, offset + r * CellSize - StoneRadius - 2);
            BoardCanvas.Children.Add(ring);
            _winHighlightRings.Add(ring);
        }
    }

    private void UpdateScore()
    {
        if (_game.Winner == Player.Black) _blackWins++;
        else if (_game.Winner == Player.White) _whiteWins++;
        UpdateUiState();
    }
}
