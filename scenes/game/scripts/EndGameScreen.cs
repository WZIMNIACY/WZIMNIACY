using Godot;
using System;
using System.Text.Json;
using Epic.OnlineServices;

public partial class EndGameScreen : Control
{
    public sealed class EndGamePayload
    {
        public MainGame.Team Winner { get; set; }
        public TeamGameStats BlueStats { get; set; }
        public TeamGameStats RedStats { get; set; }
    }

    public class TeamGameStats
    {
        public int Found { get; set; }
        public int Neutral { get; set; }
        public int Opponent { get; set; }
        public int Streak { get; set; }
    }

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
    private MainGame mainGame;

    public override void _Ready()
    {
        base._Ready();

        eosManager = GetNodeOrNull<EOSManager>("/root/EOSManager");
        mainGame = GetTree().CurrentScene as MainGame;

        if (lobbyButton != null) lobbyButton.Pressed += OnLobbyPressed;
        if (menuButton != null) menuButton.Pressed += OnMenuPressed;

        CallDeferred(nameof(SubscribeToNetwork));
    }

    public override void _ExitTree()
    {
        if (mainGame != null && mainGame.P2PNet != null)
        {
            mainGame.P2PNet.PacketHandlers -= HandlePackets;
        }
        base._ExitTree();
    }

    private void SubscribeToNetwork()
    {
        if (mainGame != null && mainGame.P2PNet != null)
        {
            mainGame.P2PNet.PacketHandlers += HandlePackets;
        }
    }

    public void TriggerGameOver(MainGame.Team winner)
    {
        if (mainGame == null) return;

        int maxBlue = (mainGame.StartingTeam == MainGame.Team.Blue) ? 9 : 8;
        int maxRed = (mainGame.StartingTeam == MainGame.Team.Red) ? 9 : 8;

        int foundBlue = maxBlue - mainGame.PointsBlue;
        int foundRed = maxRed - mainGame.PointsRed;

        TeamGameStats blueStats = new TeamGameStats
        {
            Found = foundBlue,
            Neutral = mainGame.BlueNeutralFound,
            Opponent = mainGame.BlueOpponentFound,
            Streak = mainGame.BlueMaxStreak
        };

        TeamGameStats redStats = new TeamGameStats
        {
            Found = foundRed,
            Neutral = mainGame.RedNeutralFound,
            Opponent = mainGame.RedOpponentFound,
            Streak = mainGame.RedMaxStreak
        };

        if (mainGame.isHost && mainGame.P2PNet != null)
        {
            var payload = new EndGamePayload
            {
                Winner = winner,
                BlueStats = blueStats,
                RedStats = redStats
            };
            
            mainGame.P2PNet.SendRpcToAllClients("game_ended", payload);
            GD.Print("[EndGameScreen] Stats calculated and RPC sent.");
        }

        ShowGameOver(blueStats, redStats, winner);
    }

    private bool HandlePackets(P2PNetworkManager.NetMessage packet, ProductUserId fromPeer)
    {
        if (packet.type != "game_ended") return false;
        
        if (mainGame != null && mainGame.isHost) return false;

        try
        {
            var data = packet.payload.Deserialize<EndGamePayload>();

            GD.Print($"[EndGameScreen] Received Game Over! Winner: {data.Winner}");

            ShowGameOver(data.BlueStats, data.RedStats, data.Winner);
            return true;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[EndGameScreen] Error parsing game_ended: {e.Message}");
            return true;
        }
    }

    public void ShowGameOver(TeamGameStats blueStats, TeamGameStats redStats, MainGame.Team winner)
    {
        Visible = true;
        ZIndex = 100;

        if (winnerTitle != null)
        {
            if (winner == MainGame.Team.Blue)
            {
                winnerTitle.Text = "NIEBIESCY WYGRYWAJĄ!";
                winnerTitle.Modulate = new Color("5AD2C8");
            }
            else
            {
                winnerTitle.Text = "CZERWONI WYGRYWAJĄ!";
                winnerTitle.Modulate = new Color("E65050");
            }
        }

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
    
    private void OnLobbyPressed() => GetTree().ChangeSceneToFile(lobbyScenePath);
    private void OnMenuPressed()
    {
        GD.Print("MenuButton pressed");

        if (eosManager != null && !string.IsNullOrEmpty(eosManager.currentLobbyId))
        {
            GD.Print("🚪 Leaving lobby before returning to menu...");
            eosManager.LeaveLobby();
        }

        GetTree().ChangeSceneToFile(menuScenePath);
    }
}