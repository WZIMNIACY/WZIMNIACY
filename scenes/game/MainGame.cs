using Godot;
using System;
using AI;

public partial class MainGame : Control
{
    [Signal] public delegate void GameReadyEventHandler();
    [Signal] public delegate void NewTurnStartEventHandler();

    [Export] public EndGameScreen endGameScreen;
    [Export] Panel menuPanel;
    [Export] ScoreContainer scoreContainerBlue;
    [Export] ScoreContainer scoreContainerRed;
    [Export] PlayerListContainer teamListBlue;
    [Export] PlayerListContainer teamListRed;
    [Export] public RightPanel gameRightPanel;
    [Export] public CaptainInput gameInputPanel;
    [Export] Label turnLabel;
    [Export] Control settingsScene;
    [Export] Control helpScene;
    [Export] CardManager cardManager;

    private EOSManager eosManager;

    private ILLM llm;

    // Określa czy lokalny gracz jest hostem (właścicielem lobby EOS) - wartość ustawiana dynamicznie na podstawie EOSManager.IsLobbyOwner
    public bool isHost = false;

    private Team playerTeam;

    private int pointsBlue;
    public int PointsBlue
    {
        get => pointsBlue;
    }
    private int pointsRed;
    public int PointsRed
    {
        get => pointsRed;
    }

    private int blueNeutralFound = 0;
    private int redNeutralFound = 0;
    private int blueOpponentFound = 0;
    private int redOpponentFound = 0;

    private int currentStreak = 0;
    private int blueMaxStreak = 0;
    private int redMaxStreak = 0;

    public enum Team
    {
        Blue,
        Red,
        None
    }

    private int turnCounter = 1;
    Team startingTeam;
    public Team StartingTeam
    {
        get => startingTeam;
    }
    Team currentTurn;


    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        base._Ready();

        eosManager = GetNode<EOSManager>("/root/EOSManager");

        // Ensureing popups are hidden at start
        menuPanel.Visible = false;
        settingsScene.Visible = false;
        helpScene.Visible = false;

        // Ustalanie czy lokalny gracz jest hostem na podstawie właściciela lobby EOS
        isHost = eosManager != null && eosManager.isLobbyOwner;

        // (opcjonalnie) log kontrolny
        // Log diagnostyczny: potwierdzenie roli host/klient po wejściu do sceny gry
        GD.Print($"[MainGame] isHost={isHost} localPUID={eosManager?.localProductUserIdString}");

        if (isHost)
        {
            // Host losuje drużynę rozpoczynającą grę - na razie losowanie
            startingTeam = (Team)Random.Shared.Next(0, 2);
            GD.Print("Starting team (HOST): " + startingTeam.ToString());

            // TODO: w przyszłości wyślij startingTeam do klientów (P2P / lobby attributes)
        }
        else
        {
            // Tymczasowe zachowanie klienta:
            // - do czasu pełnej synchronizacji z hostem
            // - zapobiega nieustawionemu stanowi gry
            // TODO: zastąpić odbiorem startingTeam
            startingTeam = Team.Blue;
            GD.Print("Starting team (CLIENT TEMP): " + startingTeam.ToString());
        }

        // Assing initianl points and turn
        if (startingTeam == Team.Blue)
        {
            currentTurn = Team.Blue;
            pointsBlue = 9;
            pointsRed = 8;
            SetTurnBlue();
        }
        else
        {
            currentTurn = Team.Red;
            pointsBlue = 8;
            pointsRed = 9;
            SetTurnRed();
        }
        UpdatePointsDisplay();
        UpdateTurnDisplay();

        NewTurnStart += OnNewTurnStart;

        if (gameInputPanel != null)
        {
            gameInputPanel.HintGiven += OnCaptainHintReceived;
        }
        else
        {
            GD.PrintErr("Error");
        }

        string userID = eosManager.localProductUserIdString;
        EOSManager.Team team = eosManager.GetTeamForUser(userID);
        playerTeam = (team == EOSManager.Team.Blue) ? Team.Blue : Team.Red;
        if(playerTeam == startingTeam)
        {
            gameRightPanel.EnableSkipButton();
        }
        else
        {
            gameRightPanel.DisableSkipButton();
        }

        if (eosManager.currentAIType == EOSManager.AIType.LocalLLM)
        {
            llm = new LocalLLM();
        }
        else
        {
            var apiKey = eosManager.GetAPIKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new Exception("Got into game without settings API key");
            }

            llm = new DeepSeekLLM(apiKey);
        }

        EmitSignal(SignalName.GameReady);
        EmitSignal(SignalName.NewTurnStart);
    }

    private void StartCaptainPhase()
    {
        if(gameInputPanel != null)
        {
            gameInputPanel.SetupTurn(currentTurn == Team.Blue);
        }
    }

    private void OnCaptainHintReceived(string word, int number)
    {
        GD.Print($"{word} [{number}]");
        if (gameRightPanel != null)
        {
            gameRightPanel.UpdateHintDisplay(word, number, currentTurn == Team.Blue);
        }
    }

    public void OnSkipTurnPressed()
    {
        GD.Print("Koniec tury");

        UpdateMaxStreak();

        TurnChange();
    }

    private async void OnNewTurnStart()
    {
        GD.Print($"Początek tury {(currentTurn == Team.Blue ? "BLUE" : "RED")}");

        currentStreak = 0;

        gameRightPanel.CommitToHistory();
        StartCaptainPhase();

        await gameRightPanel.GenerateAndUpdateHint(llm, cardManager.Deck, currentTurn);
    }

    private void UpdateMaxStreak()
    {
        if (currentTurn == Team.Blue)
        {
            if (currentStreak > blueMaxStreak) blueMaxStreak = currentStreak;
        }
        else
        {
            if (currentStreak > redMaxStreak) redMaxStreak = currentStreak;
        }
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
        base._Process(delta);
    }

    public void OnMenuButtonPressed()
    {
        GD.Print("Menu button pressed");
        menuPanel.Visible = true;
    }

    public void OnMenuBackgroundButtonDown()
    {
        GD.Print("MenuBackgroundButton pressed...");
        menuPanel.Visible = false;
    }

    public void OnQuitButtonPressed()
    {
        GD.Print("QuitButton pressed...");

        if (eosManager != null && !string.IsNullOrEmpty(eosManager.currentLobbyId))
        {
            GD.Print("🚪 Leaving lobby before returning to menu...");
            eosManager.LeaveLobby();
        }

        GetTree().ChangeSceneToFile("res://scenes/menu/main.tscn");
    }

    public void OnPauseButtonPressed()
    {
        GD.Print("PauseButton pressed...");
    }

    public void OnSettingsButtonPressed()
    {
        GD.Print("SettingsButton pressed...");
        settingsScene.Visible = true;
    }

    public void OnHelpButtonPressed()
    {
        GD.Print("HelpButton pressed...");
        helpScene.Visible = true;
    }

    public void OnResumeButtonPressed()
    {
        GD.Print("ResumeButton pressed...");
        menuPanel.Visible = false;
    }

    private void UpdatePointsDisplay()
    {
        string textBlue = "Karty niebieskich: "; // temp value, czekam na lepsza propozycje od UI teamu
        string textRed = "Karty czerwonych: "; // same
        scoreContainerBlue.ChangeScoreText(textBlue + pointsBlue.ToString());
        scoreContainerRed.ChangeScoreText(textRed + pointsRed.ToString());
    }

    private void UpdateTurnDisplay()
    {
        string text = "Aktualna\ntura: ";
        turnLabel.Text = text + turnCounter.ToString();
    }

    public void RemovePointBlue()
    {
        GD.Print("Point removed from team blue...");
        pointsBlue--;

        if (currentTurn == Team.Blue)
        {
            currentStreak++;
        }
        else
        {
            redOpponentFound++;
        }

        UpdatePointsDisplay();
        if (pointsBlue == 0)
            EndGame(Team.Blue);
    }

    public void RemovePointRed()
    {
        GD.Print("Point removed from team red...");
        pointsRed--;

        if (currentTurn == Team.Red)
        {
            currentStreak++;
        }
        else
        {
            blueOpponentFound++;
        }

        UpdatePointsDisplay();
        if (pointsRed == 0)
            EndGame(Team.Red);
    }

    public void TurnChange()
    {
        gameRightPanel.CancelHintGeneration();
        turnCounter++;
        UpdateTurnDisplay();
        if (currentTurn == Team.Blue)
            SetTurnRed();
        else
            SetTurnBlue();

        EmitSignal(SignalName.NewTurnStart);
    }

    public void SetTurnBlue()
    {
        GD.Print("Turn blue...");
        currentTurn = Team.Blue;
        if(playerTeam == currentTurn)
            gameRightPanel.EnableSkipButton();
        else
            gameRightPanel.DisableSkipButton();
        scoreContainerBlue.SetDiodeOn();
        scoreContainerRed.SetDiodeOff();
        teamListBlue.Modulate = new Color(2.8f, 2.8f, 2.8f, 1f);
        teamListRed.Modulate = new Color(1f, 1f, 1f, 1f);
    }

    public void SetTurnRed()
    {
        GD.Print("Turn red...");
        currentTurn = Team.Red;
        if(playerTeam == currentTurn)
            gameRightPanel.EnableSkipButton();
        else
            gameRightPanel.DisableSkipButton();
        scoreContainerBlue.SetDiodeOff();
        scoreContainerRed.SetDiodeOn();
        teamListBlue.Modulate = new Color(1f, 1f, 1f, 1f);
        teamListRed.Modulate = new Color(2.8f, 2.8f, 2.8f, 1f);
    }

    public void CardConfirm(AgentCard card)
    {
        switch (card.Type)
        {
            case CardManager.CardType.Blue:
                RemovePointBlue();
                if (currentTurn == Team.Red)
                    TurnChange();
                break;

            case CardManager.CardType.Red:
                RemovePointRed();
                if (currentTurn == Team.Blue)
                    TurnChange();
                break;

            case CardManager.CardType.Common:
                TurnChange();
                break;

            case CardManager.CardType.Assassin:
                if (currentTurn == Team.Blue)
                    EndGame(Team.Red);
                else
                    EndGame(Team.Blue);
                break;
        }
    }

    public void EndGame(Team winner)
    {
        gameRightPanel.CancelHintGeneration();
        GD.Print($"Koniec gry! Wygrywa: {winner}");
        UpdateMaxStreak();

        int maxBlue = (startingTeam == Team.Blue) ? 9 : 8;
        int maxRed = (startingTeam == Team.Red) ? 9 : 8;

        int foundBlue = maxBlue - pointsBlue;
        int foundRed = maxRed - pointsRed;

        TeamGameStats blueStats = new TeamGameStats
        {
            Found = foundBlue,
            Neutral = blueNeutralFound,
            Opponent = blueOpponentFound,
            Streak = blueMaxStreak
        };

        TeamGameStats redStats = new TeamGameStats
        {
            Found = foundRed,
            Neutral = redNeutralFound,
            Opponent = redOpponentFound,
            Streak = redMaxStreak
        };

        endGameScreen.ShowGameOver(blueStats, redStats);
    }
}
