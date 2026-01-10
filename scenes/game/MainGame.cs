using System.Collections.Generic;
using Godot;
using System;
using AI;
using System.Text.Json;
using Epic.OnlineServices;

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

    public sealed class HintNetworkPayload
    {
        public string Word { get; set; }
        public int Number { get; set; }
        public Team TurnTeam { get; set; }
    }

    private int turnCounter = 1;
    Team startingTeam;
    public Team StartingTeam
    {
        get => startingTeam;
    }
    Team currentTurn;

    // === P2P (DODANE) ===
    private P2PNetworkManager p2pNet;
    public P2PNetworkManager P2PNet => p2pNet;

    // Przykładowy payload do RPC "card_selected" (logika gry → tu, nie w P2P)
    private sealed class CardSelectedPayload
    {
        public int cardId { get; set; }
        public string by { get; set; }
    }

    private sealed class TestAckPayload
    {
        public string msg { get; set; }
        public int cardId { get; set; }
    }

    private sealed class TurnSkipPressedPayload
    {
        public string by { get; set; }
    }

    private bool p2pJsonTestSent = false;
    // =====================

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
            startingTeam = Team.Blue;
            GD.Print("Starting team (CLIENT TEMP): " + startingTeam.ToString());
        }

        // === P2P (DODANE) ===
        p2pNet = GetNode<P2PNetworkManager>("P2PNetworkManager");
        if (p2pNet != null)
        {
            // Podpinamy handler JAK NAJWCZEŚNIEJ (bez bufora)
            p2pNet.PacketHandlers += HandlePackets;

            if (!isHost)
            {
                p2pNet.HandshakeCompleted += OnP2PHandshakeCompletedTest;
            }
        }
        // =====================

        GD.Print($"[MainGame] localProductUserIdString={eosManager.localProductUserIdString}");

        if (isHost)
        {
            // Lista peerów (klientów) z lobby – potrzebna, żeby host wysłał pierwszy pakiet na SocketId
            // i uniknął błędu EOS: "unknown socket".
            var members = eosManager.GetCurrentLobbyMembers();
            var clientPuids = new List<string>();
            foreach (var member in members)
            {
                if (member == null || !member.ContainsKey("userId"))
                {
                    continue;
                }

                string puid = member["userId"].ToString();
                if (!string.IsNullOrEmpty(puid) && puid != eosManager.localProductUserIdString)
                {
                    clientPuids.Add(puid);
                }
            }

            p2pNet.StartAsHost(
                eosManager.CurrentGameSession.SessionId,
                eosManager.localProductUserIdString,
                clientPuids.ToArray()
            );
        }
        else
        {
            var hostPuid = eosManager.GetLobbyOwnerPuidString();

            p2pNet.StartAsClient(
                eosManager.CurrentGameSession.SessionId,
                eosManager.localProductUserIdString,
                hostPuid
            );
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
        if (playerTeam == startingTeam)
        {
            gameRightPanel.EnableSkipButton();
        }
        else
        {
            gameRightPanel.DisableSkipButton();
        }

        if (eosManager.isLobbyOwner)
        {
            if (eosManager.currentAIType == EOSManager.AIType.LocalLLM)
            {
                llm = new LocalLLM();
            }
            else
            {
                var apiKey = eosManager.ApiKey;
                if (string.IsNullOrEmpty(apiKey))
                {
                    throw new Exception("Got into game without settings API key");
                }

                llm = new DeepSeekLLM(apiKey);
            }
        }

        EmitSignal(SignalName.GameReady);
        EmitSignal(SignalName.NewTurnStart);
    }

    // === P2P (DODANE) ===
    public override void _ExitTree()
    {
        if (p2pNet != null)
        {
            p2pNet.PacketHandlers -= HandlePackets;

            if (!isHost)
            {
                p2pNet.HandshakeCompleted -= OnP2PHandshakeCompletedTest;
            }
        }
        base._ExitTree();
    }

    private void OnP2PHandshakeCompletedTest()
    {
        if (p2pNet == null) return;
        if (isHost) return;
        if (p2pJsonTestSent) return;

        p2pJsonTestSent = true;

        GD.Print("[MainGame][P2P-TEST] Handshake completed -> sending TEST JSON RPC card_selected to host...");

        int testCardId = 123;

        var payload = new
        {
            cardId = testCardId,
            by = eosManager?.localProductUserIdString,
            test = true
        };

        bool ok = p2pNet.SendRpcToHost("card_selected", payload);
        GD.Print($"[MainGame][P2P-TEST] SendRpcToHost(card_selected) ok={ok} testCardId={testCardId}");
    }

    // Handler pakietów z sieci (zgodnie z propozycją kolegi)
    private bool HandlePackets(P2PNetworkManager.NetMessage packet, ProductUserId fromPeer)
    {
        if (packet.type == "test_ack" && !isHost)
        {
            TestAckPayload ack;
            try
            {
                ack = packet.payload.Deserialize<TestAckPayload>();
            }
            catch (Exception e)
            {
                GD.PrintErr($"[MainGame] RPC test_ack payload parse error: {e.Message}");
                return true;
            }

            GD.Print($"[MainGame][P2P-TEST] CLIENT received ACK from host: msg={ack.msg} cardId={ack.cardId} fromPeer={fromPeer}");
            return true;
        }

        // Przykład: "card_selected" ma sens tylko gdy jesteśmy hostem (host rozstrzyga)
        if (packet.type == "card_selected" && isHost)
        {
            CardSelectedPayload payload;
            try
            {
                // JsonElement -> obiekt
                payload = packet.payload.Deserialize<CardSelectedPayload>();
            }
            catch (Exception e)
            {
                GD.PrintErr($"[MainGame] RPC card_selected payload parse error: {e.Message}");
                return true; // zjadamy, bo to był JSON RPC tego typu
            }

            GD.Print($"[MainGame] RPC card_selected received: cardId={payload.cardId} by={payload.by} fromPeer={fromPeer}");

            var ack = new
            {
                msg = "HOST_ACK_OK",
                cardId = payload.cardId
            };

            bool sent = p2pNet.SendRpcToPeer(fromPeer, "test_ack", ack);
            GD.Print($"[MainGame][P2P-TEST] HOST sent test_ack back to {fromPeer} ok={sent}");

            // TODO: tutaj podłączasz właściwą logikę gry
            // np. wybór/confirm karty, synchronizacja stanu, broadcast do wszystkich itp.

            return true; // zjedliśmy pakiet
        }

        if (packet.type == "hint_given" && !isHost)
        {
            if (packet.type == "hint_given" && !isHost)
            {
                if (gameRightPanel != null)
                {
                    gameRightPanel.HandleHintPacket(packet.payload);
                }
                return true;
            }

        // ... reszta kodu (skip_turn, game_ended itd.) ...
        }
    // -----------------
        // Odebranie infomacji przez hosta o tym ze klient chce pominac ture
        if (packet.type == "skip_turn_pressed" && isHost)
        {
            TurnSkipPressedPayload payload;
            try
            {
                payload = packet.payload.Deserialize<TurnSkipPressedPayload>();
            }
            catch (Exception e)
            {
                GD.PrintErr($"[MainGame] RPC skip_turn_pressed payload parse error: {e.Message}");
                return true;
            }

            GD.Print($"[MainGame] RPC skip_turn_pressed received: by={payload.by}");

            EOSManager.Team senderTeam = eosManager.GetTeamForUser(payload.by.ToString());

            if (currentTurn.ToEOSManagerTeam() != senderTeam)
            {
                GD.Print("[MainGame] Refusing to skip turn.");
                return true;
            }

            OnSkipTurnPressedHost();

            return true;
        }

        // Tu dopisujecie kolejne RPC:
        // if (packet.type == "hint_given" && isHost) { ... return true; }
        // if (packet.type == "turn_skip" && isHost) { ... return true; }
        // if (packet.type == "starting_team" && !isHost) { ... return true; }

        return false;
    }

    // Opcjonalny przykład wysyłki (np. lokalny gracz kliknął kartę)
    // W praktyce wywołasz to z UI / CardManager / AgentCard
    public void SendCardSelectedRpc_ToHost(int cardId)
    {
        if (p2pNet == null) return;

        var payload = new
        {
            cardId = cardId,
            by = eosManager?.localProductUserIdString
        };

        bool ok = p2pNet.SendRpcToHost("card_selected", payload);
        GD.Print($"[MainGame] SendRpcToHost(card_selected) ok={ok} cardId={cardId}");
    }
    // =====================

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
            bool isBlue = currentTurn == Team.Blue;
            gameRightPanel.UpdateHintDisplay(word, number, isBlue);
            gameRightPanel.BroadcastHint(word, number, currentTurn);
        }
    }

    public void OnSkipTurnPressed()
    {
        GD.Print("SkipTurnButton pressed...");

        if (isHost)
            OnSkipTurnPressedHost();
        else
            OnSkipTurnPressedClient();
    }

    public void OnSkipTurnPressedHost()
    {
        UpdateMaxStreak(); 

        TurnChange();

        // TODO: notify clients about turn change (rpc type: skip_turn)
    }

    public void OnSkipTurnPressedClient()
    {
        if (p2pNet == null) return;

        var payload = new
        {
            by = eosManager?.localProductUserIdString
        };

        bool ok = p2pNet.SendRpcToHost("skip_turn_pressed", payload);
        GD.Print($"[MainGame] SendRpcToHost(skip_turn_pressed) ok={ok}");
    }

    private async void OnNewTurnStart()
    {
        GD.Print($"Początek tury {(currentTurn == Team.Blue ? "BLUE" : "RED")}");

        currentStreak = 0;

        gameRightPanel.CommitToHistory();
        StartCaptainPhase();

        if (eosManager.isLobbyOwner)
        {
            await gameRightPanel.GenerateAndUpdateHint(llm, cardManager.Deck, currentTurn);
        }
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
        if (playerTeam == currentTurn)
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
        if (playerTeam == currentTurn)
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
