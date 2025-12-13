using Godot;
using System;

public class TeamGameStats
{
    public int Found { get; set; }    
    public int Neutral { get; set; }  
    public int Opponent { get; set; } 
    public int Streak { get; set; }  
}

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

    private EOSManager eosManager;

    public override void _Ready()
    {
        base._Ready();

        if (lobbyButton != null) lobbyButton.Pressed += OnLobbyPressed;
        if (menuButton != null) menuButton.Pressed += OnMenuPressed;
    }

    public void ShowGameOver(TeamGameStats blueStats, TeamGameStats redStats)
    {
        Visible = true;
        ZIndex = 100;

        UpdateStat(blueVal1, blueBar1, blueStats.Found, redStats.Found);
        UpdateStat(redVal1, redBar1, redStats.Found, blueStats.Found);

        UpdateStat(blueVal2, blueBar2, blueStats.Neutral, redStats.Neutral);
        UpdateStat(redVal2, redBar2, redStats.Neutral, blueStats.Neutral);

        UpdateStat(blueVal3, blueBar3, blueStats.Opponent, redStats.Opponent);
        UpdateStat(redVal3, redBar3, redStats.Opponent, blueStats.Opponent);
            
        if (blueVal4 != null) blueVal4.Text = blueStats.Streak.ToString();
        if (redVal4 != null) redVal4.Text = redStats.Streak.ToString();

        int total = blueStats.Found + redStats.Found;
        if (totalFoundLabel != null) totalFoundLabel.Text = total.ToString();

        int bestStreak = Math.Max(blueStats.Streak, redStats.Streak);
        if (maxStreakLabel != null) maxStreakLabel.Text = bestStreak.ToString();
    }

    private void UpdateStat(Label label, ProgressBar bar, int mainValue, int otherValue)
    {
        int maxValue = mainValue + otherValue;
        
        if (maxValue == 0) maxValue = 1; 

        if (label != null) label.Text = mainValue.ToString();
        
        if (bar != null)
        {
            bar.MaxValue = maxValue;
            
            Tween tween = CreateTween();
            tween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(bar, "value", mainValue, 1.0f).From(0.0f);
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
    private void OnMenuPressed()
    {
        GD.Print("MenuButton pressed");

        eosManager = GetNode<EOSManager>("/root/EOSManager");

        if (eosManager != null && !string.IsNullOrEmpty(eosManager.currentLobbyId))
        {
            GD.Print("🚪 Leaving lobby before returning to menu...");
            eosManager.LeaveLobby();
        }

        GetTree().ChangeSceneToFile(menuScenePath);
    }
}