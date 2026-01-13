using AI;
using Epic.OnlineServices;
using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

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
    [Export] LoadingScreen loadingScreen;


    private bool isGameStarted = false;
    private readonly Dictionary<int, P2PNetworkManager.GamePlayer> playersByIndex = new();
    private Godot.Timer sendSelectionsTimer;

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

    // === P2P (DODANE) ===
    private P2PNetworkManager p2pNet;

    // Przykładowy payload do RPC "card_selected" (logika gry → tu, nie w P2P)
    private sealed class CardSelectedPayload
    {
        public byte cardId { get; set; }
        public byte playerIndex { get; set; }
        public bool unselect { get; set; }
    }

    private sealed class CardsSelectionsPayload
    {
        public Dictionary<byte, ushort> cardsSelections { get; set; }
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

    private sealed class TurnSkipPayload
    {
        public string skippedBy { get; set; }
    }

    private sealed class RemovePointAckPayload
    {
        public Team team { get; set; }
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

        isGameStarted = false;

        loadingScreen.ShowLoading();

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
            GD.Print("Starting team (CLIENT): waiting for game_start...");
        }


        // === P2P (DODANE) ===
        p2pNet = GetNode<P2PNetworkManager>("P2PNetworkManager");
        if (p2pNet != null)
        {
            // Podpinamy handler JAK NAJWCZEŚNIEJ (bez bufora)
            p2pNet.PacketHandlers += HandlePackets;
            p2pNet.PacketHandlers += HandleGameStartPacket;

            if (isHost)
            {
                p2pNet.hostBuildGameStartPayload = BuildGameStartPayloadFromLobby;
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

        sendSelectionsTimer = new Timer();
        sendSelectionsTimer.WaitTime = 0.05f;
        sendSelectionsTimer.OneShot = false;
        sendSelectionsTimer.Autostart = false;
        sendSelectionsTimer.Timeout += SendSelectionsToClients;
        AddChild(sendSelectionsTimer);
    }

    // === P2P (DODANE) ===
    public override void _ExitTree()
    {
        if (p2pNet != null)
        {
            p2pNet.PacketHandlers -= HandlePackets;
            p2pNet.PacketHandlers -= HandleGameStartPacket;
        }
        base._ExitTree();
    }

    // Handler pakietów z sieci (zgodnie z propozycją kolegi)
    private bool HandlePackets(P2PNetworkManager.NetMessage packet, ProductUserId fromPeer)
    {
        // Przykład: "card_selected" ma sens tylko gdy jesteśmy hostem (host rozstrzyga)
        if (packet.type == "card_selected" && isHost)
        {
            if (!isGameStarted)
            {
                GD.Print("[MainGame] Ignoring card_selected (game not started yet)");
                return true;
            }

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

            GD.Print($"[MainGame] RPC card_selected received: playerIndex={payload.playerIndex} cardId={payload.cardId} unselected={payload.unselect} fromPeer={fromPeer}");

            OnCardSelectedHost(payload.cardId, payload.playerIndex, payload.unselect);

            return true; // zjedliśmy pakiet
        }

        // Odebranie infomacji przez clienta o zaznaczonych kartach
        if (packet.type == "selected_cards" && !isHost)
        {
            CardsSelectionsPayload payload;
            try
            {
                payload = packet.payload.Deserialize<CardsSelectionsPayload>();
            }
            catch (Exception e)
            {
                GD.PrintErr($"[MainGame] RPC selected_cards payload parse error: {e.Message}");
                return true;
            }

            GD.Print($"[MainGame] RPC selected_cards received: cards={payload.cardsSelections.Count}");

            cardManager.ModifyAllSelections(payload.cardsSelections);
        }

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

            string senderPuid = payload.by.ToString();

            OnSkipTurnPressedHost(senderPuid);

            return true;
        }

        // Odebranie infomacji przez clienta o tym ze nalezy pominac ture
        if (packet.type == "skip_turn" && !isHost)
        {
            TurnSkipPayload payload;
            try
            {
                payload = packet.payload.Deserialize<TurnSkipPayload>();
            }
            catch (Exception e)
            {
                GD.PrintErr($"[MainGame] RPC skip_turn payload parse error: {e.Message}");
                return true;
            }

            GD.Print($"[MainGame] RPC skip_turn received: skippedBy={payload.skippedBy}");

            UpdateMaxStreak();

            TurnChange();

            return true;
        }

        if(packet.type == "remove_point_ack" && !isHost)
        {
            RemovePointAckPayload ack;
            try
            {
                ack = packet.payload.Deserialize<RemovePointAckPayload>();
            }
            catch (Exception e)
            {
                GD.PrintErr($"[MainGame] RPC remove_point_ack payload parse error: {e.Message}");
                return true;
            }

            GD.Print($"[MainGame][P2P-TEST] CLIENT received remove_point_ack from host: removing point from: {ack.team} fromPeer={fromPeer}");
            if(ack.team == Team.Blue) RemovePointBlue();
            if(ack.team == Team.Red) RemovePointRed();
            return true;
        }

        // Tu dopisujecie kolejne RPC:
        // if (packet.type == "hint_given" && isHost) { ... return true; }
        // if (packet.type == "starting_team" && !isHost) { ... return true; }

        return false;
    }

    private bool HandleGameStartPacket(P2PNetworkManager.NetMessage packet, ProductUserId fromPeer)
    {
        if (packet.type != "game_start")
        {
            return false;
        }

        P2PNetworkManager.GameStartPayload payload;
        try
        {
            payload = packet.payload.Deserialize<P2PNetworkManager.GameStartPayload>();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[MainGame] game_start payload parse error: {e.Message}");
            return true;
        }

        if (payload == null || payload.players == null || payload.players.Length == 0)
        {
            GD.PrintErr("[MainGame] game_start payload invalid (no players)");
            return true;
        }

        // Jeśli lokalna sesja ISTNIEJE, sprawdzamy zgodność ID
        if (eosManager != null && eosManager.CurrentGameSession != null)
        {
            if (!string.IsNullOrEmpty(payload.sessionId) &&
                payload.sessionId != eosManager.CurrentGameSession.SessionId)
            {
                GD.PrintErr(
                    $"[MainGame] game_start ignored (session mismatch): payload={payload.sessionId} local={eosManager.CurrentGameSession.SessionId}"
                );
                return true;
            }
        }
        // Jeśli lokalna sesja NIE istnieje → pozwalamy wystartować grę


        ApplyGameStart(payload);
        return true;
    }

    private void ApplyGameStart(P2PNetworkManager.GameStartPayload payload)
    {
        if (isGameStarted)
        {
            GD.Print("[MainGame] ApplyGameStart ignored (already started)");
            return;
        }

        isGameStarted = true;

        if (payload == null || payload.players == null || payload.players.Length == 0)
        {
            GD.PrintErr("[MainGame] ApplyGameStart: payload/players invalid");
            isGameStarted = false;
            return;
        }


        playersByIndex.Clear();
        foreach (var p in payload.players)
        {
            if (p == null) continue;
            if (string.IsNullOrEmpty(p.puid)) continue;

            playersByIndex[p.index] = p; // trzymamy cały obiekt (puid + name + team)
        }


        string local = eosManager.localProductUserIdString;
        playerTeam = Team.None;

        foreach (var p in payload.players)
        {
            if (p == null) continue;
            if (p.puid != local) continue;

            playerTeam = p.team;
            break;
        }

        if (playerTeam == Team.None)
        {
            GD.PrintErr("[MainGame] GAME START: local player not found in payload.players (playerTeam=None)");
            // fallback na wszelki wypadek (tylko gdyby payload był uszkodzony)
            playerTeam = Team.Blue;
        }


        if (!string.IsNullOrEmpty(payload.startingTeam) && Enum.TryParse<Team>(payload.startingTeam, out var parsedStart))
        {
            startingTeam = parsedStart;
        }
        else
        {
            GD.PrintErr($"[MainGame] GAME START missing/invalid startingTeam={payload.startingTeam}");
            startingTeam = Team.Blue;
        }

        GD.Print($"[MainGame] GAME START seed={payload.seed}");


        GD.Print($"[MainGame] GAME START: players={playersByIndex.Count} sessionId={payload.sessionId}");

        loadingScreen.HideLoading();

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

        // playerTeam jest już ustawiony z game_start (RPC) i w trakcie gry się nie zmienia.
        // Nie nadpisujemy go danymi z lobby.
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
        sendSelectionsTimer.Start();
    }

    private P2PNetworkManager.GameStartPayload BuildGameStartPayloadFromLobby()
    {
        // Kolejność graczy: host (index 0) + reszta według lobby (albo sort fallback)
        var players = new List<P2PNetworkManager.GamePlayer>();

        // Host jako index 0
        players.Add(new P2PNetworkManager.GamePlayer
        {
            index = 0,
            puid = eosManager.localProductUserIdString,
            name = GetDisplayNameFromLobby(eosManager.localProductUserIdString),
            team = eosManager.GetTeamForUser(eosManager.localProductUserIdString) == EOSManager.Team.Blue
                ? Team.Blue
                : Team.Red
        });

        // Klienci z lobby
        var members = eosManager.GetCurrentLobbyMembers();
        var clientPuids = new List<string>();

        foreach (var member in members)
        {
            if (member == null || !member.ContainsKey("userId")) continue;

            string puid = member["userId"].ToString();
            if (string.IsNullOrEmpty(puid)) continue;
            if (puid == eosManager.localProductUserIdString) continue;

            clientPuids.Add(puid);
        }

        // Stabilna kolejność (żeby indexy były deterministyczne nawet jak lobby zwróci inaczej)
        clientPuids.Sort(StringComparer.Ordinal);

        int index = 1;
        foreach (string puid in clientPuids)
        {
            players.Add(new P2PNetworkManager.GamePlayer
            {
                index = index,
                puid = puid,
                name = GetDisplayNameFromLobby(puid),
                team = eosManager.GetTeamForUser(puid) == EOSManager.Team.Blue
                    ? Team.Blue
                    : Team.Red
            });

            index++;
        }

        return new P2PNetworkManager.GameStartPayload
        {
            sessionId = eosManager.CurrentGameSession.SessionId,
            players = players.ToArray(),
            startingTeam = startingTeam.ToString(),
            seed = eosManager.CurrentGameSession.Seed
        };

    }

    private string GetDisplayNameFromLobby(string puid)
    {
        if (eosManager == null || string.IsNullOrEmpty(puid))
        {
            return "";
        }

        // To jest to samo cache, które budujesz w EOSManager.GetLobbyMembers()
        var members = eosManager.GetCurrentLobbyMembers();
        if (members == null)
        {
            return "";
        }

        foreach (var member in members)
        {
            if (member == null) continue;
            if (!member.ContainsKey("userId")) continue;

            string memberPuid = member["userId"].ToString();
            if (memberPuid != puid) continue;

            if (member.ContainsKey("displayName"))
            {
                string displayName = member["displayName"].ToString();
                if (!string.IsNullOrEmpty(displayName))
                {
                    return displayName;
                }
            }

            break;
        }

        // fallback jakby coś poszło nie tak z cache
        return $"Player_{puid.Substring(Math.Max(0, puid.Length - 4))}";
    }

    private bool CanInteractWithGame()
    {
        return isGameStarted;
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
        if (!CanInteractWithGame()) return;

        GD.Print("Koniec tury");
        GD.Print("SkipTurnButton pressed...");

        if (isHost)
            OnSkipTurnPressedHost(eosManager?.localProductUserIdString);
        else
            OnSkipTurnPressedClient();
    }

    public void OnSkipTurnPressedHost(string skippedBy)
    {
        UpdateMaxStreak();

        TurnChange();

        if (p2pNet == null) return;

        var payload = new
        {
            skippedBy = skippedBy
        };

        int RPCsSent = p2pNet.SendRpcToAllClients("skip_turn", payload);
        GD.Print($"[MainGame] SendRpcToAllClients(skip_turn) RPCsSent={RPCsSent}");
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
        if (!CanInteractWithGame()) return;

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

    public void OnCardSelected(AgentCard card)
    {
        byte cardId = card.Id!.Value;
        string puid = eosManager?.localProductUserIdString;
        int playerIndex = PuidToIndex(puid);
        bool unselect = card.IsSelectedBy(playerIndex);

        GD.Print($"[MainGame][Conversion] Converting puid={puid} hsot={isHost} to index={playerIndex}");
        if (isHost)
            OnCardSelectedHost(cardId, playerIndex, unselect);
        else
            OnCardSelectedClient(cardId, playerIndex, unselect);
    }

    public void OnCardSelectedHost(byte cardId, int playerIndex, bool unselect)
    {
        cardManager.ModifySelection(cardId, playerIndex, unselect);
    }

    public void OnCardSelectedClient(byte cardId, int playerIndex, bool unselect)
    {
        if (!CanInteractWithGame()) return;
        if (p2pNet == null) return;

        var payload = new
        {
            cardId = cardId,
            playerIndex = (byte)playerIndex,
            unselect = unselect
        };

        bool ok = p2pNet.SendRpcToHost("card_selected", payload);
        GD.Print($"[MainGame] SendRpcToHost(card_selected) ok={ok}");
    }

    public void SendSelectionsToClients()
    {
        if (!CanInteractWithGame()) return;
        if (p2pNet == null) return;

        var payload = new
        {
            cardsSelections = cardManager.GetAllSelections()
        };

        int RPCsSent = p2pNet.SendRpcToAllClients("selected_cards", payload);
        GD.Print($"[MainGame] SendRpcToAllClients(selected_cards) RPCsSent={RPCsSent}");
    }

    public void CardConfirm(AgentCard card)
    {
        if (!CanInteractWithGame()) return;

        Team teamToRemovePoint = Team.None;
        switch (card.Type)
        {
            case CardManager.CardType.Blue:
                RemovePointBlue();
                teamToRemovePoint = Team.Blue;
                if (currentTurn == Team.Red)
                    TurnChange();
                break;

            case CardManager.CardType.Red:
                RemovePointRed();
                teamToRemovePoint = Team.Red;
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

        //Narazie tylko host rozsyła info o usunięciu punktu do klientów
        if(teamToRemovePoint != Team.None && isHost)
        {
            string str = eosManager.localProductUserIdString;
            ProductUserId fromPeer = ProductUserId.FromString(str);
            var ack = new
            {
                team = teamToRemovePoint
            };

            int sentInit = p2pNet.SendRpcToAllClients("remove_point_ack", ack);
            GD.Print($"[MainGame][P2P-TEST] HOST sent remove_point_ack to all clients number of successful sendings={sentInit}");

        }
    }

    public void EndGame(Team winner)
    {
        sendSelectionsTimer.Stop();

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

    public int PuidToIndex(string puid)
    {
        foreach (var player in playersByIndex)
        {
            if (player.Value.puid == puid)
                return player.Key;
        }
        GD.PrintErr($"Cant find a player with puid={puid}");
        return -1;
    }
}
