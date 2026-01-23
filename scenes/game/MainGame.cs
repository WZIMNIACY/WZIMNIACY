using AI;
using Epic.OnlineServices;
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using hints;
using game;
using System.Threading.Tasks;

public partial class MainGame : Control
{
    /// <summary>
    /// Signal emitted when the game is fully ready and initialized.
    /// </summary>
    [Signal] public delegate void GameReadyEventHandler();

    /// <summary>
    /// Signal emitted when a new turn starts.
    /// </summary>
    [Signal] public delegate void NewTurnStartEventHandler();

    /// <summary>
    /// Reference to the end game screen.
    /// </summary>
    [Export] public EndGameScreen endGameScreen;

    /// <summary>
    /// Reference to the main menu panel.
    /// </summary>
    [Export] Panel menuPanel;

    /// <summary>
    /// Reference to the blue team's score container.
    /// </summary>
    [Export] ScoreContainer scoreContainerBlue;

    /// <summary>
    /// Reference to the red team's score container.
    /// </summary>
    [Export] ScoreContainer scoreContainerRed;

    /// <summary>
    /// Reference to the blue team's player list.
    /// </summary>
    [Export] PlayerListContainer teamListBlue;

    /// <summary>
    /// Reference to the red team's player list.
    /// </summary>
    [Export] PlayerListContainer teamListRed;

    /// <summary>
    /// Reference to the right panel containing game info and controls.
    /// </summary>
    [Export] public RightPanel gameRightPanel;

    /// <summary>
    /// Reference to the captain's input panel.
    /// </summary>
    [Export] public CaptainInput gameInputPanel;

    /// <summary>
    /// Label displaying the current turn number.
    /// </summary>
    [Export] Label turnLabel;

    /// <summary>
    /// Reference to the settings screen control.
    /// </summary>
    [Export] Control settingsScene;

    /// <summary>
    /// Reference to the help screen control.
    /// </summary>
    [Export] Control helpScene;

    /// <summary>
    /// Reference to the card manager which handles the game board.
    /// </summary>
    [Export] CardManager cardManager;

    /// <summary>
    /// Reference to the loading screen.
    /// </summary>
    [Export] LoadingScreen loadingScreen;

    /// <summary>
    /// Reference to the reaction overlay for displaying emotions/messages.
    /// </summary>
    [Export] public ReactionOverlay reactionOverlay;

    private bool isGameStarted = false;
    /// <summary>
    /// Indicates if the game has finished.
    /// </summary>
    public bool isGameFinished {get; private set;} = false;
    private readonly Dictionary<int, P2PNetworkManager.GamePlayer> playersByIndex = new();
    
    /// <summary>
    /// Dictionary of players indexed by their unique game index.
    /// </summary>
    public Dictionary<int, P2PNetworkManager.GamePlayer> PlayersByIndex => playersByIndex;

    private Godot.Timer sendSelectionsTimer;

    private EOSManager eosManager;

    private ILLM llm;
    
    /// <summary>
    /// The AI player instance if playing with AI.
    /// </summary>
    public AIPlayer.LLMPlayer llmPlayer { get; private set; }

    // Określa czy lokalny gracz jest hostem (właścicielem lobby EOS) - wartość ustawiana dynamicznie na podstawie EOSManager.IsLobbyOwner
    /// <summary>
    /// Indicates if the local player is the host (EOS Lobby Owner).
    /// </summary>
    public bool isHost = false;

    private Team playerTeam;

    private int pointsBlue;
    /// <summary>
    /// Current score for the Blue team.
    /// </summary>
    public int PointsBlue
    {
        get => pointsBlue;
    }
    private int pointsRed;
    
    /// <summary>
    /// Current score for the Red team.
    /// </summary>
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

    /// <summary>
    /// Maximum streak for the Blue team.
    /// </summary>
    public int BlueMaxStreak => blueMaxStreak;
    /// <summary>
    /// Maximum streak for the Red team.
    /// </summary>
    public int RedMaxStreak => redMaxStreak;

    /// <summary>
    /// Number of neutral cards found by Blue team.
    /// </summary>
    public int BlueNeutralFound => blueNeutralFound;
    /// <summary>
    /// Number of neutral cards found by Red team.
    /// </summary>
    public int RedNeutralFound => redNeutralFound;
    /// <summary>
    /// Number of opponent cards found by Blue team.
    /// </summary>
    public int BlueOpponentFound => blueOpponentFound;
    /// <summary>
    /// Number of opponent cards found by Red team.
    /// </summary>
    public int RedOpponentFound => redOpponentFound;

    /// <summary>
    /// Enum representing the teams in the game.
    /// </summary>
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
    public P2PNetworkManager P2PNet => p2pNet;

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

    // Po przejściu na autorytatywny model (turn_changed) payload nie jest już używany(TurnSkipPayload). Potencjalnie do usunięcia
    private sealed class TurnSkipPayload
    {
        public string skippedBy { get; set; }
    }


    private sealed class RemovePointAckPayload
    {
        public Team team { get; set; }
    }

    private sealed class CardConfirmPressedPayload
    {
        public int cardId { get; set; }
        public string by { get; set; }
    }

    private sealed class CardRevealedPayload
    {
        public int cardId { get; set; }
        public string confirmedBy { get; set; }
        public bool isAssassin { get; set; }
    }

    private sealed class TurnChangedPayload
    {
        public Team currentTurn { get; set; }
        public int turnCounter { get; set; }
    }

    private sealed class ReactionPayload
    {
        public string text { get; set; }
        public uint durationMs { get; set; }
    }
    private bool p2pJsonTestSent = false;
    // =====================

    private Task reactionTask;
    private bool isShuttingDown = false;
    private const int ReactionDurationMs = 2500;

    private readonly HashSet<int> confirmedCardIds = new();

    private EndGameReason lastEndGameReason = EndGameReason.AllCardsFound;

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

        eosManager.Connect(EOSManager.SignalName.LobbyOwnerChanged, new Callable(this, nameof(OnHostLeave)));

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
        isShuttingDown = true;

        if (p2pNet != null)
        {
            p2pNet.PacketHandlers -= HandlePackets;
            p2pNet.PacketHandlers -= HandleGameStartPacket;
        }
        base._ExitTree();
    }

    // Handler pakietów z sieci (zgodnie z propozycją kolegi)
    /// <summary>
    /// Handles incoming network packets from peers.
    /// </summary>
    /// <param name="packet">The network message packet.</param>
    /// <param name="fromPeer">The ID of the peer who sent the packet.</param>
    /// <returns>True if the packet was handled, false otherwise.</returns>
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

        // === REACTION (NEW RPC) ===
        // Każdy (host i client) wyświetla reakcję z hosta
        if (packet.type == "reaction")
        {
            ReactionPayload payload;
            try
            {
                payload = packet.payload.Deserialize<ReactionPayload>();
            }
            catch (Exception e)
            {
                GD.PrintErr($"[MainGame] RPC reaction payload parse error: {e.Message}");
                return true;
            }

            if (payload == null || string.IsNullOrWhiteSpace(payload.text))
            {
                GD.PrintErr("[MainGame] RPC reaction payload invalid (null/empty text)");
                return true;
            }

            float seconds = payload.durationMs / 1000.0f;
            ShowReactionBubble(payload.text, seconds);

            return true;
        }
        // ==========================

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

            //GD.Print($"[MainGame] RPC selected_cards received: cards={payload.cardsSelections.Count}");

            cardManager.ModifyAllSelections(payload.cardsSelections);

            return true;
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

            if (payload == null || string.IsNullOrEmpty(payload.by))
            {
                GD.PrintErr("[MainGame] RPC skip_turn_pressed payload invalid (null/empty by)");
                return true;
            }

            string senderPuid = payload.by.ToString();
            GD.Print($"[MainGame] RPC skip_turn_pressed received: by={senderPuid}");

            // Bierzemy team z danych z game_start (playersByIndex), a nie z EOSManager
            Team senderTeamLocal = GetTeamForPuidFromGameStart(senderPuid);

            if (senderTeamLocal == Team.None)
            {
                GD.PrintErr($"[MainGame] Refusing to skip turn (unknown sender team). puid={senderPuid}");
                return true;
            }

            if (currentTurn != senderTeamLocal)
            {
                GD.Print($"[MainGame] Refusing to skip turn (wrong team). currentTurn={currentTurn} senderTeam={senderTeamLocal} puid={senderPuid}");
                return true;
            }

            OnSkipTurnPressedHost(senderPuid);
            return true;
        }

        if (packet.type == "remove_point_ack" && !isHost)
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
            if (ack.team == Team.Blue) RemovePointBlue();
            if (ack.team == Team.Red) RemovePointRed();
            return true;
        }

        // Odebranie informacji przez hosta o tym ze klient chce zatwierdzic karte
        // Odebranie informacji przez hosta o tym ze klient chce zatwierdzic karte
        if (packet.type == "card_confirm_pressed" && isHost)
        {
            if (!isGameStarted)
            {
                GD.Print("[MainGame] Ignoring card_confirm_pressed (game not started yet)");
                return true;
            }

            CardConfirmPressedPayload payload;
            try
            {
                payload = packet.payload.Deserialize<CardConfirmPressedPayload>();
                GD.Print($"[MainGame] card_confirm_pressed parsed OK: cardId={payload.cardId} by={payload.by}");

            }
            catch (Exception e)
            {
                GD.PrintErr($"[MainGame] RPC card_confirm_pressed payload parse error: {e.Message}");
                return true;
            }

            if (payload == null)
            {
                GD.PrintErr("[MainGame] card_confirm_pressed payload is null");
                return true;
            }

            GD.Print($"[MainGame] RPC card_confirm_pressed received: cardId={payload.cardId} by={payload.by}");

            HostConfirmCardAndBroadcast(payload.cardId, payload.by);
            return true;
        }

        // Odebranie informacji przez clienta o tym ze host zatwierdzil karte
        if (packet.type == "card_revealed" && !isHost)
        {
            CardRevealedPayload payload;
            try
            {
                payload = packet.payload.Deserialize<CardRevealedPayload>();
            }
            catch (Exception e)
            {
                GD.PrintErr($"[MainGame] RPC card_revealed payload parse error: {e.Message}");
                return true;
            }

            if (payload == null)
            {
                GD.PrintErr("[MainGame] card_revealed payload is null");
                return true;
            }

            GD.Print($"[MainGame] RPC card_revealed received: cardId={payload.cardId} confirmedBy={payload.confirmedBy} isAssassin={payload.isAssassin}");

            // TODO (#30): tutaj finalnie podepniemy odsloniecie karty / zablokowanie ponownego klikniecia w UI
            // Celowo NIE robimy tutaj EndGame() - tym zajmuje sie zadanie #31.
            if (cardManager == null)
            {
                GD.PrintErr("[MainGame] cardManager is null on client");
                return true;
            }

            if (payload.cardId < 0 || payload.cardId >= cardManager.GetChildCount())
            {
                GD.PrintErr($"[MainGame] Invalid cardId={payload.cardId} (out of range)");
                return true;
            }

            AgentCard cardNode = cardManager.GetChild(payload.cardId) as AgentCard;
            if (cardNode == null)
            {
                GD.PrintErr($"[MainGame] Child at index {payload.cardId} is not AgentCard");
                return true;
            }

            // Klient dopiero teraz aplikuje reveal (UI + usunięcie z decka)
            cardManager.ApplyCardRevealed(cardNode);

            // Nie robimy EndGame() — to jest zadanie #31.


            return true;
        }

        // Odebranie informacji przez clienta o zmianie tury (np. po zatwierdzeniu karty)
        if (packet.type == "turn_changed" && !isHost)
        {
            TurnChangedPayload payload;
            try
            {
                payload = packet.payload.Deserialize<TurnChangedPayload>();
            }
            catch (Exception e)
            {
                GD.PrintErr($"[MainGame] RPC turn_changed payload parse error: {e.Message}");
                return true;
            }

            if (payload == null)
            {
                GD.PrintErr("[MainGame] turn_changed payload is null");
                return true;
            }

            GD.Print($"[MainGame] RPC turn_changed received: currentTurn={payload.currentTurn} turnCounter={payload.turnCounter}");

            // Ustawiamy licznik tury dokładnie na wartość z hosta
            turnCounter = payload.turnCounter;
            UpdateTurnDisplay();

            // Ustawiamy turę bez wywoływania TurnChange() (żeby nie inkrementować drugi raz)
            if (payload.currentTurn == Team.Blue)
                SetTurnBlue();
            else if (payload.currentTurn == Team.Red)
                SetTurnRed();

            // W starym flow TurnChange() emitował NewTurnStart.
            // Teraz klient nie woła TurnChange(), więc musimy odpalić start tury ręcznie.
            EmitSignal(SignalName.NewTurnStart);

            return true;
        }

        return false; // nie obsłużyliśmy tego pakietu
    }

    /// <summary>
    /// Handles the "game_start" packet to initialize the game state on clients.
    /// </summary>
    /// <param name="packet">The network message packet.</param>
    /// <param name="fromPeer">The ID of the peer who sent the packet.</param>
    /// <returns>True if the packet was handled, false otherwise.</returns>
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

    /// <summary>
    /// Applies the game start payload to initialize the game locally.
    /// </summary>
    /// <param name="payload">The game start payload containing player and session info.</param>
    private void ApplyGameStart(P2PNetworkManager.GameStartPayload payload)
    {
        if (isGameStarted)
        {
            GD.Print("[MainGame] ApplyGameStart ignored (already started)");
            return;
        }

        isGameStarted = true;

        confirmedCardIds.Clear();

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

        if (isHost)
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

        if (eosManager.currentGameMode == EOSManager.GameMode.AIvsHuman)
        {
            llmPlayer = new AIPlayer.LLMPlayer(llm);
            playersByIndex.Add(-1, new P2PNetworkManager.GamePlayer
            {
                index = -1,
                puid = "ai_player",
                name = "AI",
                team = Team.Red,
                profileIconPath = eosManager.GetProfileIconPath(EOSManager.Team.Red, 5)
            });
        }

        EmitSignal(SignalName.GameReady);
        EmitSignal(SignalName.NewTurnStart);
        sendSelectionsTimer.Start();
    }

    /// <summary>
    /// Builds the GameStartPayload using data from the current EOS lobby.
    /// Collects all members, assigns indices, and sets initial game parameters.
    /// </summary>
    /// <returns>A fully populated <see cref="P2PNetworkManager.GameStartPayload"/>.</returns>
    private P2PNetworkManager.GameStartPayload BuildGameStartPayloadFromLobby()
    {
        // Kolejność graczy: host (index 0) + reszta według lobby (albo sort fallback)
        var players = new List<P2PNetworkManager.GamePlayer>();

        // Host jako index 0
        int index = 0;
        players.Add(new P2PNetworkManager.GamePlayer
        {
            index = 0,
            puid = eosManager.localProductUserIdString,
            name = GetDisplayNameFromLobby(eosManager.localProductUserIdString),
            team = TeamEnumExt.FromEOSManagerTeam(eosManager.GetTeamForUser(eosManager.localProductUserIdString)),
            profileIconPath = eosManager.GetProfileIconPathForUser(eosManager.localProductUserIdString)
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

        index++;
        foreach (string puid in clientPuids)
        {
            players.Add(new P2PNetworkManager.GamePlayer
            {
                index = index,
                puid = puid,
                name = GetDisplayNameFromLobby(puid),
                team = TeamEnumExt.FromEOSManagerTeam(eosManager.GetTeamForUser(puid)),
                profileIconPath = eosManager.GetProfileIconPathForUser(puid)
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

    /// <summary>
    /// Retrieves the display name of a player from the lobby member cache.
    /// </summary>
    /// <param name="puid"> The unique product user ID of the player.</param>
    /// <returns>The display name if found, otherwise a default fallback name.</returns>
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

    /// <summary>
    /// Finds the team assigned to a player ID based on the initial game start data.
    /// Used to validate actions like turn skipping against the authoritative game state.
    /// </summary>
    /// <param name="puid">The player's unique product user ID.</param>
    /// <returns>The team the player belongs to, or <see cref="Team.None"/> if not found.</returns>
    private Team GetTeamForPuidFromGameStart(string puid)
    {
        if (string.IsNullOrEmpty(puid)) return Team.None;

        foreach (var kv in playersByIndex)
        {
            var p = kv.Value;
            if (p == null) continue;
            if (p.puid == puid)
                return p.team;
        }

        return Team.None;
    }

    /// <summary>
    /// Checks if the game is in a state where interaction is allowed.
    /// </summary>
    /// <returns>True if the game is started and interactions are permitted.</returns>
    private bool CanInteractWithGame()
    {
        return isGameStarted;
    }

    /// <summary>
    /// Attempts to extract the reaction text from a raw JSON response (or fallback text).
    /// </summary>
    /// <param name="raw">The raw string response from the LLM.</param>
    /// <param name="text">The extracted reaction text.</param>
    /// <returns>True if extraction was successful and text is not empty.</returns>
    private static bool TryExtractReactionText(string raw, out string text)
    {
        text = null;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        // Reaction.create() zwraca string przycięty do { ... } (JSON)
        // ale czasem LLM może zwrócić sam tekst bez JSON -> wtedy fallback.
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                // Najczęstsze pola jakie modele zwracają
                if (root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                {
                    text = t.GetString();
                    return !string.IsNullOrWhiteSpace(text);
                }
                if (root.TryGetProperty("reaction", out var r) && r.ValueKind == JsonValueKind.String)
                {
                    text = r.GetString();
                    return !string.IsNullOrWhiteSpace(text);
                }
                if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                {
                    text = m.GetString();
                    return !string.IsNullOrWhiteSpace(text);
                }
            }
        }
        catch
        {
            // ignore -> fallback niżej
        }

        // Fallback: jeśli to nie JSON, pokaż jak leci,
        // ale usuń zewnętrzne klamry (bo Reaction.create() je dokleja)
        text = raw.Trim();

        if (text.Length >= 2 && text[0] == '{' && text[^1] == '}')
        {
            text = text.Substring(1, text.Length - 2).Trim();
        }

        return !string.IsNullOrWhiteSpace(text);
    }

    /// <summary>
    /// Displays a reaction bubble on the UI.
    /// </summary>
    /// <param name="text">The text to display.</param>
    /// <param name="seconds">Duration in seconds to show the bubble.</param>
    private void ShowReactionBubble(string text, float seconds = 2.5f)
    {
        if (reactionOverlay == null)
        {
            GD.PrintErr("[MainGame] ReactionOverlay is null (Export not assigned in Inspector)");
            return;
        }

        reactionOverlay.ShowReaction(text, seconds);
    }

    /// <summary>
    /// Starts the captain phase by setting up the input panel.
    /// </summary>
    private void StartCaptainPhase()
    {
        if (gameInputPanel != null)
        {
            gameInputPanel.SetupTurn(currentTurn == Team.Blue);
        }
    }

    /// <summary>
    /// Callback when a captain submits a hint.
    /// Updates game state and broadcasts the hint.
    /// </summary>
    /// <param name="word">The hint word.</param>
    /// <param name="number">The number of associated cards.</param>
    private void OnCaptainHintReceived(string word, int number)
    {
        GD.Print($"{word} [{number}]");
        if (gameRightPanel != null)
        {
            bool isBlue = currentTurn == Team.Blue;
            gameRightPanel.UpdateHintDisplay(word, number, isBlue);
            gameRightPanel.BroadcastHint(word, number, currentTurn);

            // FIX: ustawiamy LastGeneratedHint, bo TryBuildReactionText tego wymaga.
            gameRightPanel.SetLastGeneratedHint(word, number);
        }
    }

    /// <summary>
    /// Handler for the skip turn button press.
    /// Initiates the skip turn logic via host or client RPC.
    /// </summary>
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

    /// <summary>
    /// Host-side logic for handling a skip turn request.
    /// Updates stats, changes turn, and broadcasts the change.
    /// </summary>
    /// <param name="skippedBy">The PUID of the player skipping the turn.</param>
    public void OnSkipTurnPressedHost(string skippedBy)
    {
        UpdateMaxStreak();

        TurnChange();

        // Zamiast starego RPC "skip_turn" wysyłamy autorytatywny stan tury
        BroadcastTurnChanged();

        GD.Print($"[MainGame] Skip turn processed by host. skippedBy={skippedBy}");
    }

    /// <summary>
    /// Client-side logic for requesting a skip turn.
    /// Sends an RPC to the host.
    /// </summary>
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

    /// <summary>
    /// Broadcasts a turn change update to all clients.
    /// Used by the host to synchronize the turn counter and current team.
    /// </summary>
    private void BroadcastTurnChanged()
    {
        if (p2pNet == null) return;
        if (!isHost) return;

        var payload = new TurnChangedPayload
        {
            currentTurn = currentTurn,
            turnCounter = turnCounter
        };

        int sent = p2pNet.SendRpcToAllClients("turn_changed", payload);
        GD.Print($"[MainGame] SendRpcToAllClients(turn_changed) sent={sent} currentTurn={currentTurn} turnCounter={turnCounter}");
    }

    /// <summary>
    /// Broadcasts a reaction message to all clients.
    /// Also displays it locally for the host.
    /// </summary>
    /// <param name="text">The reaction text.</param>
    /// <param name="durationMs">Duration in milliseconds.</param>
    private void BroadcastReaction(string text, uint durationMs = ReactionDurationMs)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Host i tak powinien zobaczyć to samo od razu
        CallDeferred(nameof(ShowReactionBubble), text, durationMs / 1000.0f);

        if (p2pNet == null) return;
        if (!isHost) return;

        var payload = new ReactionPayload
        {
            text = text,
            durationMs = durationMs
        };

        int sent = p2pNet.SendRpcToAllClients("reaction", payload);
        GD.Print($"[MainGame] SendRpcToAllClients(reaction) sent={sent}");
    }

    /// <summary>
    /// Asynchronously attempts to build a reaction text using the LLM.
    /// </summary>
    /// <param name="hint">The current hint context.</param>
    /// <param name="pickedCard">The card that triggered the reaction.</param>
    /// <param name="turnAtPick">The team whose turn it was during the pick.</param>
    /// <returns>The raw reaction string (JSON or text) from the LLM, or null on failure.</returns>
    private async Task<string> TryBuildReactionTextAsync(Hint hint, Card pickedCard, Team turnAtPick)
    {
        if (llm == null)
        {
            GD.PrintErr("[MainGame] Reaction: llm is null");
            return null;
        }

        if (hint == null)
        {
            GD.PrintErr("[MainGame] Reaction: hint is null");
            return null;
        }

        if (pickedCard == null)
        {
            GD.PrintErr("[MainGame] Reaction: pickedCard is null");
            return null;
        }

		var args = OS.GetCmdlineArgs();
		bool captain_bomb = args.Contains("--kapitanbomba");

		game.Team actualTour = turnAtPick.ToAiLibTeam();

        try
        {
            string reactionRaw = await global::Reaction.Reaction.create(llm, hint, pickedCard, captain_bomb, actualTour);
            return reactionRaw;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[MainGame] Reaction: create() exception: {e}");
            return null;
        }
    }

    /// <summary>
    /// Orchestrates the process of generating a reaction and broadcasting it.
    /// Handles exceptions and UI state checks.
    /// </summary>
    /// <param name="hintSnapshot">Snapshot of the hint data.</param>
    /// <param name="pickedCardSnapshot">Snapshot of the picked card.</param>
    /// <param name="turnSnapshot">Snapshot of the turn state.</param>
    private async Task GenerateAndBroadcastReactionAsync(Hint hintSnapshot, Card pickedCardSnapshot, Team turnSnapshot)
    {
        try
        {
            // (1) jeśli już zamykamy scenę, nie zaczynaj
            if (isShuttingDown || !IsInsideTree()) return;

            string reactionRaw = await TryBuildReactionTextAsync(hintSnapshot, pickedCardSnapshot, turnSnapshot);

            // (2) najważniejsze: po await scena mogła zniknąć
            if (isShuttingDown || !IsInsideTree()) return;

            if (string.IsNullOrWhiteSpace(reactionRaw))
            {
                GD.PrintErr("[MainGame] Reaction skipped (reactionRaw empty/null).");
                return;
            }

            if (!TryExtractReactionText(reactionRaw, out string cleanText))
            {
                GD.PrintErr("[MainGame] Reaction skipped (could not extract text).");
                return;
            }

            // UI + RPC (NA MAIN THREAD)
            CallDeferred(nameof(BroadcastReaction), cleanText, (uint)ReactionDurationMs);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[MainGame] GenerateAndBroadcastReactionAsync exception: {e}");
        }
    }

    // Wspólna ścieżka: host potwierdza kartę i rozsyła efekty do klientów
    /// <summary>
    /// Host-side logic to confirm a card selection and broadcast the result to all clients.
    /// This is the source of truth for card reveals.
    /// </summary>
    /// <param name="cardId">The ID of the card being confirmed.</param>
    /// <param name="confirmedBy">The PUID of the player who confirmed the card.</param>
    public void HostConfirmCardAndBroadcast(int cardId, string confirmedBy)
    {
        GD.Print($"[MainGame] HostConfirmCardAndBroadcast ENTER cardId={cardId} by={confirmedBy}");

        if (!isHost) return;
        if (!isGameStarted)
        {
            GD.Print("[MainGame] Ignoring HostConfirmCardAndBroadcast (game not started yet)");
            return;
        }

        // Anty-duplikaty (ten sam mechanizm dla host-click i client-click)
        if (confirmedCardIds.Contains(cardId))
        {
            GD.Print($"[MainGame] Ignoring duplicate confirm: cardId={cardId}");
            return;
        }
        confirmedCardIds.Add(cardId);

        if (cardManager == null)
           {
            GD.PrintErr("[MainGame] cardManager is null on host");
            return;
        }

        if (cardId < 0 || cardId >= cardManager.GetChildCount())
        {
            GD.PrintErr($"[MainGame] Invalid cardId={cardId} (out of range)");
            return;
        }

        AgentCard cardNode = cardManager.GetChild(cardId) as AgentCard;
        if (cardNode == null)
        {
            GD.PrintErr($"[MainGame] Child at index {cardId} is not AgentCard");
            return;
        }

        bool isAssassin = cardNode.Type == CardManager.CardType.Assassin;

        // Zapisujemy stan tury PRZED logiką, żeby wykryć zmianę po ApplyCardConfirmedHost
        Team beforeTurn = currentTurn;
        int beforeTurnCounter = turnCounter;

        GD.Print($"[ReactionTrace][HOST] Before ApplyCardConfirmedHost cardId={cardId} turn={currentTurn} counter={turnCounter}");

        // ===== SNAPSHOT dla reakcji (MUSI być przed ApplyCardConfirmedHost) =====
        Hint hintSnapshot = gameRightPanel?.LastGeneratedHint;
        Card pickedCardSnapshot = cardNode?.cardInfo;
        Team turnSnapshot = currentTurn;
        // ==============================================================

        // 1) Host wykonuje logikę gry lokalnie (źródło prawdy)
        cardManager.ApplyCardConfirmedHost(cardNode);

        GD.Print($"[ReactionTrace][HOST] After ApplyCardConfirmedHost cardId={cardId} turn={currentTurn} counter={turnCounter}");

        // 2) Host wysyła informację do WSZYSTKICH klientów (klienci dopiero teraz robią UI/deck)
        if (p2pNet != null)
        {
            var revealPayload = new CardRevealedPayload
            {
                cardId = cardId,
                confirmedBy = confirmedBy,
                isAssassin = isAssassin
            };

            int revealedSent = p2pNet.SendRpcToAllClients("card_revealed", revealPayload);
            GD.Print($"[MainGame] SendRpcToAllClients(card_revealed) sent={revealedSent} cardId={cardId} isAssassin={isAssassin}");
        }

        // 3) Jeśli zmieniła się tura/licznik -> broadcast
        if (currentTurn != beforeTurn || turnCounter != beforeTurnCounter)
        {
            BroadcastTurnChanged();
        }

        // 4) Generujemy i broadcastujemy reakcję ASYNC (nie blokujemy gry)
        if (hintSnapshot == null)
        {
            GD.Print("[MainGame] Reaction skipped (hintSnapshot is null). Did you call SetLastGeneratedHint()?");
            return;
        }

        if (pickedCardSnapshot == null)
        {
            GD.PrintErr("[MainGame] Reaction skipped (pickedCardSnapshot is null).");
            return;
        }

        reactionTask = GenerateAndBroadcastReactionAsync(hintSnapshot, pickedCardSnapshot, turnSnapshot);
        reactionTask.ContinueWith(
            t => GD.PrintErr($"[MainGame] Reaction task faulted: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted
        );

    }

    /// <summary>
    /// Client-side callback when card confirmation is requested.
    /// Sends an RPC to the host.
    /// </summary>
    /// <param name="cardId">The ID of the card to confirm.</param>
    public void OnCardConfirmPressedClient(int cardId)
    {
        GD.Print($"[MainGame] OnCardConfirmPressedClient fired cardId={cardId}");

        if (!CanInteractWithGame()) return;
        if (p2pNet == null) return;

        var payload = new CardConfirmPressedPayload
        {
            cardId = cardId,
            by = eosManager?.localProductUserIdString
        };

        bool ok = p2pNet.SendRpcToHost("card_confirm_pressed", payload);
        GD.Print($"[MainGame] SendRpcToHost(card_confirm_pressed) ok={ok} cardId={cardId}");
    }


    /// <summary>
    /// Handles the start of a new turn.
    /// Resets turn-specific state and triggers hint generation for the captain.
    /// </summary>
    private async void OnNewTurnStart()
    {
        GD.Print($"Początek tury {(currentTurn == Team.Blue ? "BLUE" : "RED")}");

        currentStreak = 0;

        gameRightPanel.CommitToHistory();
        StartCaptainPhase();

        if (isHost)
        {
            await gameRightPanel.GenerateAndUpdateHint(llm, cardManager.Deck, currentTurn);
        }
        else
        {
            gameRightPanel.HintGenerationAnimationStart();
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

    /// <summary>
    /// UI callback for the Menu button. Shows the menu panel.
    /// </summary>
    public void OnMenuButtonPressed()
    {
        if (!CanInteractWithGame()) return;

        GD.Print("Menu button pressed");
        menuPanel.Visible = true;
    }

    /// <summary>
    /// UI callback for the Menu background. Hides the menu.
    /// </summary>
    public void OnMenuBackgroundButtonDown()
    {
        GD.Print("MenuBackgroundButton pressed...");
        menuPanel.Visible = false;
    }

    /// <summary>
    /// UI callback for the Quit button.
    /// Leaves the lobby and returns to the main menu.
    /// </summary>
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

    /// <summary>
    /// UI callback for the Settings button. Shows the settings screen.
    /// </summary>
    public void OnSettingsButtonPressed()
    {
        GD.Print("SettingsButton pressed...");
        settingsScene.Visible = true;
    }

    /// <summary>
    /// UI callback for the Help button. Shows the help screen.
    /// </summary>
    public void OnHelpButtonPressed()
    {
        GD.Print("HelpButton pressed...");
        helpScene.Visible = true;
    }

    /// <summary>
    /// UI callback for the Resume button. Hides the menu.
    /// </summary>
    public void OnResumeButtonPressed()
    {
        GD.Print("ResumeButton pressed...");
        menuPanel.Visible = false;
    }

    /// <summary>
    /// Updates the points display on the UI.
    /// </summary>
    private void UpdatePointsDisplay()
    {
        string textBlue = "Karty niebieskich: "; // temp value, czekam na lepsza propozycje od UI teamu
        string textRed = "Karty czerwonych: "; // same
        scoreContainerBlue.ChangeScoreText(textBlue + pointsBlue.ToString());
        scoreContainerRed.ChangeScoreText(textRed + pointsRed.ToString());
    }

    /// <summary>
    /// Updates the turn counter display on the UI.
    /// </summary>
    private void UpdateTurnDisplay()
    {
        string text = "Aktualna\ntura: ";
        turnLabel.Text = text + turnCounter.ToString();
    }

    /// <summary>
    /// Removes a point from the Blue team.
    /// Updates scores, streaks, and checks for game end conditions.
    /// </summary>
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
        {
            lastEndGameReason = EndGameReason.AllCardsFound;
            EndGame(Team.Blue);
        }
    }

    /// <summary>
    /// Removes a point from the Red team.
    /// Updates scores, streaks, and checks for game end conditions.
    /// </summary>
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
        {
            lastEndGameReason = EndGameReason.AllCardsFound;
            EndGame(Team.Red);
        }
    }

    /// <summary>
    /// Advances the game to the next turn.
    /// Swaps the current team and emits the <see cref="NewTurnStart"/> signal.
    /// </summary>
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

    /// <summary>
    /// Explicitly sets the turn to the Blue team.
    /// </summary>
    public void SetTurnBlue()
    {
        GD.Print("Turn blue...");
        cardManager.ClearAllSelections();
        currentTurn = Team.Blue;
        if (playerTeam == currentTurn)
            gameRightPanel.EnableSkipButton();
        else
            gameRightPanel.DisableSkipButton();
        scoreContainerBlue.SetDiodeOn();
        scoreContainerRed.SetDiodeOff();
        teamListBlue.SelfModulate = new Color(2.8f, 2.8f, 2.8f, 1f);
        teamListRed.SelfModulate = new Color(1f, 1f, 1f, 1f);
    }

    /// <summary>
    /// Explicitly sets the turn to the Red team.
    /// </summary>
    public void SetTurnRed()
    {
        GD.Print("Turn red...");
        cardManager.ClearAllSelections();
        currentTurn = Team.Red;
        if (playerTeam == currentTurn)
            gameRightPanel.EnableSkipButton();
        else
            gameRightPanel.DisableSkipButton();
        scoreContainerBlue.SetDiodeOff();
        scoreContainerRed.SetDiodeOn();
        teamListBlue.SelfModulate = new Color(1f, 1f, 1f, 1f);
        teamListRed.SelfModulate = new Color(2.8f, 2.8f, 2.8f, 1f);
    }

    /// <summary>
    /// Called when a card is selected (clicked) by a player.
    /// Handles selection logic and delegates to host/client specific handlers.
    /// </summary>
    /// <param name="card">The card that was selected.</param>
    public void OnCardSelected(AgentCard card)
    {
        byte cardId = card.Id!.Value;
        string puid = eosManager?.localProductUserIdString;
        int playerIndex = PuidToIndex(puid);
        var player = PlayersByIndex[playerIndex];

        if (player.team != currentTurn)
        {
            return;
        }

        bool unselect = card.IsSelectedBy(playerIndex);

        GD.Print($"[MainGame][Conversion] Converting puid={puid} hsot={isHost} to index={playerIndex}");
        if (isHost)
            OnCardSelectedHost(cardId, playerIndex, unselect);
        else
            OnCardSelectedClient(cardId, playerIndex, unselect);
    }

    /// <summary>
    /// Handles the "Host" version of card selection (updating local state).
    /// </summary>
    /// <param name="cardId">The selected card ID.</param>
    /// <param name="playerIndex">The selecting player's index.</param>
    /// <param name="unselect">If true, unselects the card.</param>
    public void OnCardSelectedHost(byte cardId, int playerIndex, bool unselect)
    {
        cardManager.ModifySelection(cardId, playerIndex, unselect);
    }

    /// <summary>
    /// Handles the "Client" version of card selection (sending RPC to host).
    /// </summary>
    /// <param name="cardId">The selected card ID.</param>
    /// <param name="playerIndex">The selecting player's index.</param>
    /// <param name="unselect">If true, unselects the card.</param>
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

    /// <summary>
    /// Periodically sends the current card selection state to all clients.
    /// </summary>
    public void SendSelectionsToClients()
    {
        if (!CanInteractWithGame()) return;
        if (p2pNet == null) return;

        var payload = new
        {
            cardsSelections = cardManager.GetAllSelections()
        };

        int RPCsSent = p2pNet.SendRpcToAllClients("selected_cards", payload);
        //GD.Print($"[MainGame] SendRpcToAllClients(selected_cards) RPCsSent={RPCsSent}");
    }

    /// <summary>
    /// Processes card identification logic when a card is confirmed (revealed).
    /// Updates points, turn state, and triggers game end if necessary.
    /// </summary>
    /// <param name="card">The card being confirmed.</param>
    public void CardConfirm(AgentCard card)
    {
        if (!CanInteractWithGame()) return;

        if (!isHost)
        {
            OnCardConfirmPressedClient(card.GetIndex());
            return;
        }

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
                if (currentTurn == Team.Blue)
                {
                    blueNeutralFound++;
                }
                else if (currentTurn == Team.Red)
                {
                    redNeutralFound++;
                }

                TurnChange();
                break;

            case CardManager.CardType.Assassin:
                lastEndGameReason = EndGameReason.AssassinPicked;
                if (currentTurn == Team.Blue)
                    EndGame(Team.Red);
                else
                    EndGame(Team.Blue);
                break;
        }

        //Narazie tylko host rozsyła info o usunięciu punktu do klientów
        if (teamToRemovePoint != Team.None && isHost)
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

    /// <summary>
    /// Ends the game and displays the game over screen.
    /// Only the host calls this method to act as the source of truth.
    /// </summary>
    /// <param name="winner">The winning team.</param>
    public void EndGame(Team winner)
    {
        if (!isHost) return;

        sendSelectionsTimer.Stop();

        isGameFinished = true;

        gameRightPanel.CancelHintGeneration();
        GD.Print($"Koniec gry! Wygrywa: {winner}");
        UpdateMaxStreak();

        if (endGameScreen != null)
        {
            endGameScreen.TriggerGameOver(winner, lastEndGameReason);
        }
    }

    /// <summary>
    /// Converts a Product User ID string to a game player index.
    /// </summary>
    /// <param name="puid">The PUID string to convert.</param>
    /// <returns>The player index, or -1 if not found.</returns>
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

    /// <summary>
    /// Gets the game player index of the local user.
    /// </summary>
    /// <returns>The local player's index.</returns>
    public int GetLocalPlayerIndex()
    {
        string localPuid = eosManager?.localProductUserIdString;
        return PuidToIndex(localPuid);
    }

    /// <summary>
    /// Handles the event when the host leaves the lobby.
    /// Shows a popup and exits the game.
    /// </summary>
    private void OnHostLeave()
    {
        GD.Print("[MainGame] Host has left the game.");
        eosManager.popupSystem.ShowMessage("Host wyszedł.", "Gra się zakończyła z powodu wyjścia hosta z gry.", () =>
        {
            OnQuitButtonPressed();
        });
    }
}

