using Godot;
using System;
using System.Text.Json;
using Epic.OnlineServices;

/// <summary>
/// Defines the possible reasons for the game to end.
/// </summary>
public enum EndGameReason
{
    /// <summary>All agent cards of a team were found.</summary>
    AllCardsFound,
    /// <summary>The assassin card was picked.</summary>
    AssassinPicked
}

/// <summary>
/// Manages the End Game screen, displaying results, statistics, and navigation options.
/// Handles network synchronization of game results.
/// </summary>
public partial class EndGameScreen : Control
{
    /// <summary>
    /// Data structure for sending game end results over the network.
    /// </summary>
    public sealed class EndGamePayload
    {
        /// <summary>The winning team.</summary>
        public MainGame.Team Winner { get; set; }
        /// <summary>The reason for the game ending.</summary>
        public EndGameReason Reason { get; set; }
        /// <summary>Statistics for the Blue team.</summary>
        public TeamGameStats BlueStats { get; set; }
        /// <summary>Statistics for the Red team.</summary>
        public TeamGameStats RedStats { get; set; }
    }

    /// <summary>
    /// Encapsulates game statistics for a single team.
    /// </summary>
    public class TeamGameStats
    {
        /// <summary>Number of own agent cards found.</summary>
        public int Found { get; set; }
        /// <summary>Number of neutral/bystander cards found.</summary>
        public int Neutral { get; set; }
        /// <summary>Number of opponent's agent cards found.</summary>
        public int Opponent { get; set; }
        /// <summary>Highest winning streak achieved.</summary>
        public int Streak { get; set; }
    }

    [ExportGroup("General")]
    /// <summary>Label displaying the winner title text.</summary>
    [Export] public Label winnerTitle;
    /// <summary>Label displaying the game end reason.</summary>
    [Export] public Label subTitle;

    [ExportGroup("Blue Team")]
    /// <summary>Label for Blue team's first statistic (Found).</summary>
    [Export] public Label blueVal1;
    /// <summary>Progress bar for Blue team's first statistic.</summary>
    [Export] public ProgressBar blueBar1;
    /// <summary>Label for Blue team's second statistic (Neutral).</summary>
    [Export] public Label blueVal2;
    /// <summary>Progress bar for Blue team's second statistic.</summary>
    [Export] public ProgressBar blueBar2;
    /// <summary>Label for Blue team's third statistic (Opponent).</summary>
    [Export] public Label blueVal3;
    /// <summary>Progress bar for Blue team's third statistic.</summary>
    [Export] public ProgressBar blueBar3;
    /// <summary>Label for Blue team's fourth statistic (Streak).</summary>
    [Export] public Label blueVal4;

    [ExportGroup("Red Team")]
    /// <summary>Label for Red team's first statistic (Found).</summary>
    [Export] public Label redVal1;
    /// <summary>Progress bar for Red team's first statistic.</summary>
    [Export] public ProgressBar redBar1;
    /// <summary>Label for Red team's second statistic (Neutral).</summary>
    [Export] public Label redVal2;
    /// <summary>Progress bar for Red team's second statistic.</summary>
    [Export] public ProgressBar redBar2;
    /// <summary>Label for Red team's third statistic (Opponent).</summary>
    [Export] public Label redVal3;
    /// <summary>Progress bar for Red team's third statistic.</summary>
    [Export] public ProgressBar redBar3;

    /// <summary>Label for Red team's fourth statistic (Streak).</summary>
    [Export] public Label redVal4;

    [ExportGroup("Summary")]
    /// <summary>Label displaying total cards found by both teams.</summary>
    [Export] public Label totalFoundLabel;
    /// <summary>Label displaying the highest streak achieved in the game.</summary>
    [Export] public Label maxStreakLabel;

    [ExportGroup("Buttons & Navigation")]
    /// <summary>Button to return to the lobby.</summary>
    [Export] public Button lobbyButton;
    /// <summary>Button to return to the main menu.</summary>
    [Export] public Button menuButton;

    /// <summary>Scene path for the lobby scene.</summary>
    [Export(PropertyHint.File, "*.tscn")] public string lobbyScenePath;
    /// <summary>Scene path for the main menu scene.</summary>
    [Export(PropertyHint.File, "*.tscn")] public string menuScenePath;

    /// <summary>Reference to the EOS Manager for networking/lobby logic.</summary>
    private EOSManager eosManager;
    /// <summary>Reference to the MainGame controller.</summary>
    private MainGame mainGame;

    // Buffer for deferred UI call (CallDeferred can't pass custom types via Variant)
    /// <summary>Buffer for deferred Blue team stats (used for thread safety).</summary>
    private TeamGameStats pendingBlueStats;
    /// <summary>Buffer for deferred Red team stats (used for thread safety).</summary>
    private TeamGameStats pendingRedStats;
    /// <summary>Buffer for deferred winner (used for thread safety).</summary>
    private MainGame.Team pendingWinner;
    /// <summary>Buffer for deferred end reason (used for thread safety).</summary>
    private EndGameReason pendingReason;

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

    /// <summary>
    /// Subscribes to the P2P network packet handlers.
    /// </summary>
    private void SubscribeToNetwork()
    {
        if (mainGame != null && mainGame.P2PNet != null)
        {
            mainGame.P2PNet.PacketHandlers += HandlePackets;
        }
    }

    /// <summary>
    /// Triggers the game over state, calculating stats and notifying clients if host.
    /// </summary>
    /// <param name="winner">The winning team.</param>
    /// <param name="reason">The reason for the game ending.</param>
    public void TriggerGameOver(MainGame.Team winner, EndGameReason reason)
    {
        if (mainGame == null) return;

        int maxBlue = (mainGame.StartingTeam == MainGame.Team.Blue) ? 9 : 8;
        int maxRed = (mainGame.StartingTeam == MainGame.Team.Red) ? 9 : 8;

        int totalBlueRevealed = maxBlue - mainGame.PointsBlue;
        int totalRedRevealed = maxRed - mainGame.PointsRed;

        int blueFoundOwn = totalBlueRevealed - mainGame.RedOpponentFound;
        
        int redFoundOwn = totalRedRevealed - mainGame.BlueOpponentFound;

        TeamGameStats blueStats = new TeamGameStats
        {
            Found = blueFoundOwn,
            Neutral = mainGame.BlueNeutralFound,
            Opponent = mainGame.BlueOpponentFound,
            Streak = mainGame.BlueMaxStreak
        };

        TeamGameStats redStats = new TeamGameStats
        {
            Found = redFoundOwn,
            Neutral = mainGame.RedNeutralFound,
            Opponent = mainGame.RedOpponentFound,
            Streak = mainGame.RedMaxStreak
        };

        if (mainGame.isHost && mainGame.P2PNet != null)
        {
            var payload = new EndGamePayload
            {
                Winner = winner,
                Reason = reason,
                BlueStats = blueStats,
                RedStats = redStats
            };

            mainGame.P2PNet.SendRpcToAllClients("game_ended", payload);
            GD.Print("[EndGameScreen] Stats calculated and RPC sent.");
        }

        ShowGameOverWithDelay(blueStats, redStats, winner, reason);
    }

    /// <summary>
    /// Initiates the Game Over UI display with a delay.
    /// </summary>
    /// <param name="blueStats">Included statistics for blue team.</param>
    /// <param name="redStats">Included statistics for red team.</param>
    /// <param name="winner">The winning team.</param>
    /// <param name="reason">The reason for game over.</param>
    private async void ShowGameOverWithDelay(TeamGameStats blueStats, TeamGameStats redStats, MainGame.Team winner, EndGameReason reason)
    {
        // stały delay wg kontraktu
        await ToSignal(GetTree().CreateTimer(2.5f), SceneTreeTimer.SignalName.Timeout);

        // Jeśli scena już zniknęła, nie dotykamy UI
        if (!IsInsideTree()) return;

        // Zapis do bufora (CallDeferred nie przenosi custom typów przez Variant)
        pendingBlueStats = blueStats;
        pendingRedStats = redStats;
        pendingWinner = winner;
        pendingReason = reason;

       /// Wywołanie bez argumentów -> thread-safe + bez Variant problemu
       CallDeferred(nameof(ShowGameOverDeferred));
    }

    /// <summary>
    /// Deferred callback to show the Game Over screen on the main thread.
    /// </summary>
    private void ShowGameOverDeferred()
    {
        ShowGameOver(pendingBlueStats, pendingRedStats, pendingWinner, pendingReason);
    }

    /// <summary>
    /// Handles network packets related to game ending.
    /// </summary>
    /// <param name="packet">The received packet.</param>
    /// <param name="fromPeer">The sender's ID.</param>
    /// <returns>True if packet was handled, false otherwise.</returns>
    private bool HandlePackets(P2PNetworkManager.NetMessage packet, ProductUserId fromPeer)
    {
        if (packet.type != "game_ended") return false;

        if (mainGame != null && mainGame.isHost) return false;

        try
        {
            var data = packet.payload.Deserialize<EndGamePayload>();

            GD.Print($"[EndGameScreen] Received Game Over! Winner: {data.Winner}");

            ShowGameOverWithDelay(data.BlueStats, data.RedStats, data.Winner, data.Reason);
            return true;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[EndGameScreen] Error parsing game_ended: {e.Message}");
            return true;
        }
    }

    /// <summary>
    /// Displays the Game Over screen and fills the UI with statistics.
    /// </summary>
    /// <param name="blueStats">Statistics for blue team.</param>
    /// <param name="redStats">Statistics for red team.</param>
    /// <param name="winner">The winning team.</param>
    /// <param name="reason">The reason for game over.</param>
    public void ShowGameOver(TeamGameStats blueStats, TeamGameStats redStats, MainGame.Team winner, EndGameReason reason)
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

        if (subTitle != null)
        {
            subTitle.Text = reason switch
            {
                EndGameReason.AllCardsFound => "Powód: odnaleziono wszystkie karty drużyny zwycięskiej.",
                EndGameReason.AssassinPicked => "Powód: trafiono zabójcę (assassin).",
                _ => "Powód: nieznany."
            };
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

    /// <summary>
    /// Updates a single statistic row (Label + ProgressBar).
    /// </summary>
    /// <param name="label">The label to display the value.</param>
    /// <param name="bar">The progress bar.</param>
    /// <param name="mainValue">The value of the statistic.</param>
    /// <param name="otherValue">The opponent's value (for max scale).</param>
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

    /// <summary>
    /// Handles the "Return to Lobby" button press.
    /// </summary>
    private void OnLobbyPressed()
    {
        if (eosManager != null && eosManager.isLobbyOwner)
        {
            eosManager.UnlockLobby();
            eosManager.ResetGameSession();
            GD.Print("Game session reseted and lobby unlocked by host");
        }

        GetTree().ChangeSceneToFile(lobbyScenePath);
    }

    /// <summary>
    /// Handles the "Return to Menu" button press.
    /// </summary>
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