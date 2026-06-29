using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;

namespace Gomoku;

public sealed partial class SplashWindow : Window
{
    private GameMode _selectedMode = GameMode.PvP;
    private Difficulty _selectedDifficulty = Difficulty.Medium;

    public SplashWindow()
    {
        InitializeComponent();
        Activated += OnFirstActivated;
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;
        BuildStarDisplay();
    }

    private void ModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModeSelector.SelectedItem is RadioButton rb && rb.Tag is string tag)
        {
            _selectedMode = tag == "PvAI" ? GameMode.PvAI : GameMode.PvP;
            DifficultySection.Visibility = _selectedMode == GameMode.PvAI
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void BuildStarDisplay()
    {
        StarRow.Children.Clear();
        int starCount = (int)_selectedDifficulty + 1;
        for (int i = 0; i < 5; i++)
        {
            StarRow.Children.Add(new TextBlock
            {
                Text = i < starCount ? "★" : "☆",
                FontSize = 20,
                Foreground = i < starCount
                    ? new SolidColorBrush(Color.FromArgb(255, 255, 200, 30))
                    : new SolidColorBrush(Color.FromArgb(255, 160, 160, 160))
            });
        }
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        // Read difficulty from combo
        if (DifficultyCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _selectedDifficulty = tag switch
            {
                "Beginner" => Difficulty.Beginner,
                "Easy" => Difficulty.Easy,
                "Medium" => Difficulty.Medium,
                "Hard" => Difficulty.Hard,
                "Master" => Difficulty.Master,
                _ => Difficulty.Medium
            };
        }

        // Create and show the main game window
        var mainWindow = new MainWindow();
        mainWindow.Activate();

        // Close the splash window
        Close();
    }
}
