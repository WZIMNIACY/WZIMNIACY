using Godot;
using System;

public partial class EndGameScreen : Control
{
    [ExportGroup("General")] 
    [Export] public Label winnerTitle;
    [Export] public Label subTitle;

    [ExportGroup("Blue Team")]
    [Export] public Label blueVal1; 
    [Export] public ProgressBar blueBar1;
    [Export] public Label blueVal2; 
    [Export] public ProgressBar blueBar2;
    [Export] public Label blueVal3; 
    [Export] public ProgressBar blueBar3;
    [Export] public Label blueVal4; 

    [ExportGroup("Red Team")]
    [Export] public Label redVal1;
    [Export] public ProgressBar redBar1;
    [Export] public Label redVal2;
    [Export] public ProgressBar redBar2;
    [Export] public Label redVal3;
    [Export] public ProgressBar redBar3;
    
    [Export] public Label redVal4; 

    [ExportGroup("Summary")]
    [Export] public Label totalFoundLabel;
    [Export] public Label maxStreakLabel;

    [ExportGroup("Buttons & Navigation")]
    [Export] public Button lobbyButton;
    [Export] public Button menuButton;
    
    [Export(PropertyHint.File, "*.tscn")] public string lobbyScenePath;
    [Export(PropertyHint.File, "*.tscn")] public string menuScenePath;

    public override void _Ready()
    {
        base._Ready();
        Visible = false;

        if (lobbyButton != null) lobbyButton.Pressed += OnLobbyPressed;
        if (menuButton != null) menuButton.Pressed += OnMenuPressed;
    }

    public void ShowGameOver(int[] blueStats, int[] redStats)
    {
        Visible = true;
        ZIndex = 100;

        if (blueStats.Length == 4)
        {
            UpdateStatBlue(blueVal1, blueBar1, blueStats[0], redStats[0]);
            UpdateStatBlue(blueVal2, blueBar2, blueStats[1], redStats[1]);
            UpdateStatBlue(blueVal3, blueBar3, blueStats[2], redStats[2]);
            
            if (blueVal4 != null) blueVal4.Text = blueStats[3].ToString();
        }

        if (redStats.Length == 4)
        {
            UpdateStatRed(redVal1, redBar1, redStats[0], blueStats[0]);
            UpdateStatRed(redVal2, redBar2, redStats[1], blueStats[1]);
            UpdateStatRed(redVal3, redBar3, redStats[2], blueStats[2]);
            
            if (redVal4 != null) redVal4.Text = redStats[3].ToString();
        }

        int total = blueStats[0] + redStats[0];
        if (totalFoundLabel != null) totalFoundLabel.Text = total.ToString();

        int bestStreak = Math.Max(blueStats[3], redStats[3]);
        if (maxStreakLabel != null) maxStreakLabel.Text = bestStreak.ToString();
    }

    private void UpdateStatBlue(Label label, ProgressBar bar, int blueValue, int redValue)
    {
		int maxValue = blueValue + redValue;
        if (label != null) label.Text = blueValue.ToString();
        
        if (bar != null)
        {
            bar.MaxValue = maxValue;
            Tween tween = CreateTween();
            tween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(bar, "value", blueValue, 1.0f).From(0.0f);
        }
    }

	private void UpdateStatRed(Label label, ProgressBar bar, int redValue, int blueValue)
    {
		int maxValue = blueValue + redValue;
        if (label != null) label.Text = redValue.ToString();
        
        if (bar != null)
        {
            bar.MaxValue = maxValue;
            Tween tween = CreateTween();
            tween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(bar, "value", redValue, 1.0f).From(0.0f);
        }
    }

    private void OnLobbyPressed() => GetTree().ChangeSceneToFile(lobbyScenePath);
    private void OnMenuPressed() => GetTree().ChangeSceneToFile(menuScenePath);
}