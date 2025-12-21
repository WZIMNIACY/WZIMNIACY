using Godot;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using game;

public partial class LobbyMenu : Control
{
    private EOSManager eosManager;
    [Export] private Button backButton;
    [Export] private Button leaveLobbyButton;
    [Export] private ItemList blueTeamList;
    [Export] private ItemList redTeamList;
    [Export] private ItemList neutralTeamList;
    [Export] private ItemList universalTeamList;
    [Export] private HBoxContainer teamsContainer;
    [Export] private PanelContainer universalTeamContainer;
    [Export] private PanelContainer neutralTeamContainer;
    [Export] private Button blueTeamJoinButton;
    [Export] private Button redTeamJoinButton;
    [Export] private Label blueTeamCountLabel;
    [Export] private Label redTeamCountLabel;
    [Export] private Label universalTeamCountLabel;
    [Export] private LineEdit lobbyIdInput;
    [Export] private Button copyIdButton;
    [Export] private Button generateNewIdButton;
    [Export] private Button startGameButton;
    [Export] private OptionButton gameModeList;
    [Export] private HBoxContainer aiAPIBox;
    [Export] private OptionButton aiTypeList;
    [Export] private Label gameModeSelectedLabel;
    [Export] private Label aiTypeSelectedLabel;
    [Export] private LineEdit aiAPIKeyInput;
    [Export] private Button apiKeyHelpButton;
    [Export] private Label lobbyStatusLabel;
    [Export] private Label lobbyStatusCounter;

    // Custom tooltip
    private CustomTooltip customTooltip;
    private string lobbyReadyTooltip = "";

    private string currentLobbyCode = "";
    private const int LobbyCodeLength = 6;
    private const int LobbyMaxPlayers = 10;
    private const int MaxRetryAttempts = 10;
    private const float RetryDelay = 0.5f;
    private const int MaxPlayersPerTeam = 5;
    private const float CooldownTime = 5.0f;
    private bool isTeamChangeCooldownActive = false;
    private Dictionary<string, bool> playerMoveCooldowns = new Dictionary<string, bool>();

    private static class LobbyStatus
    {
        public static bool aiTypeSet { get; set; } = false;
        public static bool gameModeSet { get; set; } = false;
        public static bool isAnyTeamFull { get; set; } = false;
        public static bool isTeamNotEmpty { get; set; } = false;
        public static bool isNeutralTeamEmpty { get; set; } = true;
        public static bool isAPIKeySet { get; set; } = false;

        public static bool IsReadyToStart()
        {
            return aiTypeSet && gameModeSet && isAPIKeySet && isTeamNotEmpty && !isAnyTeamFull && isNeutralTeamEmpty;
        }

    }

    public override void _Ready()
    {
        base._Ready();

        // Pobierz EOSManager z autoload
        eosManager = GetNode<EOSManager>("/root/EOSManager");

        // Podłącz sygnały przycisków
        if (backButton != null)
        {
            backButton.Pressed += OnBackButtonPressed;
        }

        if (leaveLobbyButton != null)
        {
            leaveLobbyButton.Pressed += OnLeaveLobbyPressed;
        }

        if (copyIdButton != null)
        {
            copyIdButton.Pressed += OnCopyIdButtonPressed;
        }

        if (generateNewIdButton != null)
        {
            generateNewIdButton.Pressed += OnGenerateNewIdButtonPressed;
        }

        if (gameModeList != null)
        {
            gameModeList.ItemSelected += OnSelectedGameModeChanged;
        }
        if (aiTypeList != null)
        {
            aiTypeList.ItemSelected += OnSelectedAITypeChanged;
        }
        if (apiKeyHelpButton != null)
        {
            apiKeyHelpButton.Pressed += OnAPIKeyHelpButtonPressed;
        }

        if (startGameButton != null)
        {
            startGameButton.Pressed += OnStartGamePressed;
            startGameButton.MouseEntered += OnReadyTooltipMouseEntered;
            startGameButton.MouseExited += OnReadyTooltipMouseExited;
        }

        if (lobbyStatusCounter != null)
        {
            lobbyStatusCounter.MouseFilter = MouseFilterEnum.Stop;
            lobbyStatusCounter.MouseEntered += OnReadyTooltipMouseEntered;
            lobbyStatusCounter.MouseExited += OnReadyTooltipMouseExited;
        }

        if (blueTeamList != null)
        {
            blueTeamList.GuiInput += (inputEvent) => OnTeamListGuiInput(inputEvent, blueTeamList);
        }
        if (redTeamList != null)
        {
            redTeamList.GuiInput += (inputEvent) => OnTeamListGuiInput(inputEvent, redTeamList);
        }
        if (neutralTeamList != null)
        {
            neutralTeamList.GuiInput += (inputEvent) => OnTeamListGuiInput(inputEvent, neutralTeamList);
        }
        if (universalTeamList != null)
        {
            universalTeamList.GuiInput += (inputEvent) => OnTeamListGuiInput(inputEvent, universalTeamList);
        }

        if (blueTeamJoinButton != null)
        {
            blueTeamJoinButton.Pressed += OnBlueTeamJoinButtonPressed;
        }

        if (redTeamJoinButton != null)
        {
            redTeamJoinButton.Pressed += OnRedTeamJoinButtonPressed;
        }

        // Podłącz walidację API key przy wciśnięciu Enter
        if (aiAPIKeyInput != null)
        {
            aiAPIKeyInput.TextSubmitted += OnAPIKeySubmitted;
            aiAPIKeyInput.TextChanged += OnAPIKeyTextChanged;
        }

        // WAŻNE: Podłącz sygnał z EOSManager do aktualizacji drużyn
        if (eosManager != null)
        {
            eosManager.LobbyMembersUpdated += OnLobbyMembersUpdated;
            eosManager.CustomLobbyIdUpdated += OnCustomLobbyIdUpdated;
            eosManager.GameModeUpdated += OnGameModeUpdated;
            eosManager.AITypeUpdated += OnAITypeUpdated;
            eosManager.CheckTeamsBalanceConditions += OnCheckTeamsBalanceConditions;
            eosManager.LobbyReadyStatusUpdated += OnLobbyReadyStatusUpdated;
            // Game session: odbieramy sygnał startu sesji z EOSManager (ustawiany na podstawie atrybutów lobby)
            eosManager.GameSessionStartRequested += OnGameSessionStartRequested;

            GD.Print("✅ Connected to LobbyMembersUpdated, CustomLobbyIdUpdated, GameModeUpdated, AITypeUpdated, CheckTeamsBalanceConditions and LobbyReadyStatusUpdated signals");

            // Sprawdź obecną wartość CustomLobbyId
            if (!string.IsNullOrEmpty(eosManager.currentCustomLobbyId))
            {
                GD.Print($"🆔 Current CustomLobbyId in EOSManager: '{eosManager.currentCustomLobbyId}'");
                OnCustomLobbyIdUpdated(eosManager.currentCustomLobbyId);
            }

            // Sprawdź obecną wartość GameMode
            OnGameModeUpdated(EOSManager.GetEnumDescription(eosManager.currentGameMode));

            // Sprawdź obecną wartość AIType
            OnAITypeUpdated(EOSManager.GetEnumDescription(eosManager.currentAIType));
        }
        else
        {
            GD.PrintErr("❌ EOSManager is null, cannot connect to signal!");
        }

        // Sprawdź czy jesteśmy w lobby (powinniśmy być, bo MainMenu/Join już je utworzyło/dołączyło)
        if (eosManager != null && !string.IsNullOrEmpty(eosManager.currentLobbyId))
        {
            GD.Print($"✅ Already in lobby: {eosManager.currentLobbyId}");

            // Wywołaj początkową aktualizację UI na podstawie obecnego stanu
            CallDeferred(nameof(UpdateUIVisibility));

            // Odśwież listę członków - to wywoła sygnał LobbyMembersUpdated
            CallDeferred(nameof(RefreshLobbyMembers));

            if (eosManager.isLobbyOwner)
            {
                CallDeferred(nameof(UpdateHostReadyStatus));
            }
        }
        else
        {
            GD.PrintErr("⚠️ Entered lobby scene but not in any lobby!");
        }

        // Domyślnie odblokuj przyciski dołączania zanim spłyną dane z EOS
        UpdateTeamButtonsState(EOSManager.Team.None);

        // Załaduj custom tooltip ze sceny
        LoadCustomTooltip();
    }

    /// <summary>
    /// Ładuje custom tooltip ze sceny
    /// </summary>
    private void LoadCustomTooltip()
    {
        var tooltipScene = GD.Load<PackedScene>("res://scenes/components/tooltip.tscn");
        if (tooltipScene != null)
        {
            customTooltip = tooltipScene.Instantiate<CustomTooltip>();
            AddChild(customTooltip);
        }
    }

    // Tooltip aktualizuje swoją pozycję sam w swoim _Process
    public override void _Process(double delta)
    {
        base._Process(delta);
    }

    // Chroni przed wielokrotną zmianą sceny, gdy przyjdzie kilka eventów/odświeżeń lobby
    private bool alreadySwitchedToGame = false;

    // Game Session: wszyscy gracze przechodzą do sceny gry dopiero, gdy lobby ogłosi stan "Starting"
    private void OnGameSessionStartRequested(string sessionId, string hostUserId, ulong seed)
    {
        if (alreadySwitchedToGame) return;
        
        alreadySwitchedToGame = true;

        GD.Print($"🎮 Switching to game. Session={sessionId}, Host={hostUserId}, Seed={seed}");

        // Zmiana sceny uruchamiana synchronicznie dla hosta i klientów na podstawie atrybutów lobby
        GetTree().ChangeSceneToFile("res://scenes/game/main_game.tscn");    
    }

    /// <summary>
    /// Helper do odświeżenia listy członków lobby
    /// </summary>
    private void RefreshLobbyMembers()
    {
        if (eosManager != null)
        {
            eosManager.GetLobbyMembers();
        }
    }

    private string GenerateLobbyIDCode()
    {
        //Bez liter O i I
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";
        var random = new Random();
        char[] code = new char[LobbyCodeLength];

        for (int i = 0; i < LobbyCodeLength; i++)
        {
            code[i] = chars[random.Next(chars.Length)];
        }

        return new string(code);
    }

    /// <summary>
    /// NOWA METODA: Obsługuje aktualizacje listy członków z EOSManager
    /// Rozdziela graczy na drużyny WEDŁUG ATRYBUTU "team"
    /// </summary>
    private void OnLobbyMembersUpdated(Godot.Collections.Array<Godot.Collections.Dictionary> members)
    {
        if (blueTeamList == null || redTeamList == null || neutralTeamList == null || universalTeamList == null)
        {
            GD.PrintErr("❌ Team lists not found!");
            return;
        }

        GD.Print($"🔄 Updating team lists with {members.Count} members");

        // Wyczyść wszystkie drużyny
        blueTeamList.Clear();
        redTeamList.Clear();
        neutralTeamList.Clear();
        universalTeamList.Clear();

        EOSManager.Team detectedLocalTeam = EOSManager.Team.None;

        // Rozdziel graczy na drużyny WEDŁUG ATRYBUTU "team"
        foreach (var member in members)
        {
            string displayName = member["displayName"].ToString();
            bool isOwner = (bool)member["isOwner"];
            bool isLocalPlayer = (bool)member["isLocalPlayer"];

            EOSManager.Team team = EOSManager.Team.None;
            if (member.ContainsKey("team") && !string.IsNullOrEmpty(member["team"].ToString()))
            {
                if (!Enum.TryParse<EOSManager.Team>(member["team"].ToString(), out team))
                {
                    team = EOSManager.Team.None;
                }
            }

            string userId = member.ContainsKey("userId") ? member["userId"].ToString() : "";

            if (isLocalPlayer)
            {
                detectedLocalTeam = team;
            }

            // Dodaj ikonę korony dla właściciela
            if (isOwner)
            {
                displayName = "👑 " + displayName;
            }

            // Dodaj oznaczenie (TY) dla lokalnego gracza
            if (isLocalPlayer)
            {
                displayName += " (TY)";
            }

            // Przypisz do odpowiedniej drużyny według atrybutu
            if (team == EOSManager.Team.Blue)
            {
                int index = blueTeamList.AddItem(displayName);
                blueTeamList.SetItemMetadata(index, new Godot.Collections.Dictionary
                {
                    { "userId", userId },
                    { "isLocalPlayer", isLocalPlayer },
                    { "team", team.ToString() }
                });
                GD.Print($"  ➕ Blue: {displayName}");
            }
            else if (team == EOSManager.Team.Red)
            {
                int index = redTeamList.AddItem(displayName);
                redTeamList.SetItemMetadata(index, new Godot.Collections.Dictionary
                {
                    { "userId", userId },
                    { "isLocalPlayer", isLocalPlayer },
                    { "team", team.ToString() }
                });
                GD.Print($"  ➕ Red: {displayName}");
            }
            else if (team == EOSManager.Team.Universal)
            {
                int index = universalTeamList.AddItem(displayName);
                universalTeamList.SetItemMetadata(index, new Godot.Collections.Dictionary
                {
                    { "userId", userId },
                    { "isLocalPlayer", isLocalPlayer },
                    { "team", team.ToString() }
                });
                GD.Print($"  ➕ Universal: {displayName}");
            }
            else // team == EOSManager.Team.None (NeutralTeam)
            {
                int index = neutralTeamList.AddItem(displayName);
                neutralTeamList.SetItemMetadata(index, new Godot.Collections.Dictionary
                {
                    { "userId", userId },
                    { "isLocalPlayer", isLocalPlayer },
                    { "team", team.ToString() }
                });
                GD.Print($"  ➕ Neutral: {displayName}");
            }
        }

        GD.Print($"✅ Teams updated: Blue={blueTeamList.ItemCount}, Red={redTeamList.ItemCount}, Neutral={neutralTeamList.ItemCount}, Universal={universalTeamList.ItemCount}");

        // Aktualizuj liczniki drużyn
        if (blueTeamCountLabel != null)
        {
            blueTeamCountLabel.Text = $"{blueTeamList.ItemCount}/{MaxPlayersPerTeam}";
        }
        if (redTeamCountLabel != null)
        {
            redTeamCountLabel.Text = $"{redTeamList.ItemCount}/{MaxPlayersPerTeam}";
        }
        if (universalTeamCountLabel != null)
        {
            universalTeamCountLabel.Text = $"{universalTeamList.ItemCount}/{MaxPlayersPerTeam}";
        }

        // Zaktualizuj widoczność przycisków dla hosta/gracza
        UpdateUIVisibility();

        // Odśwież stan przycisków drużynowych
        UpdateTeamButtonsState(detectedLocalTeam);

        // Sprawdza warunki rozpoczęcia gry dla drużyn
        OnCheckTeamsBalanceConditions();
    }

    /// <summary>
    /// Aktualizuje widoczność przycisków w zależności od tego czy jesteśmy hostem
    /// </summary>
    private void UpdateUIVisibility()
    {
        bool isHost = eosManager != null && eosManager.isLobbyOwner;

        // Przyciski dostępne TYLKO dla hosta
        if (generateNewIdButton != null)
        {
            generateNewIdButton.Visible = isHost;
        }

        if (startGameButton != null)
        {
            startGameButton.Visible = isHost;
        }

        if (gameModeList != null)
        {
            gameModeList.Visible = isHost;

            // Wyłącz opcję "AI vs Human" jeśli jest więcej niż 5 graczy w trybie AI Master
            if (isHost && eosManager != null && eosManager.currentGameMode == EOSManager.GameMode.AIMaster)
            {
                int totalPlayers = 0;
                if (blueTeamList != null) totalPlayers += blueTeamList.ItemCount;
                if (redTeamList != null) totalPlayers += redTeamList.ItemCount;
                if (neutralTeamList != null) totalPlayers += neutralTeamList.ItemCount;

                // Znajdź indeks "AI vs Human" i wyłącz go jeśli jest więcej niż 5 graczy
                for (int i = 0; i < gameModeList.ItemCount; i++)
                {
                    string itemText = gameModeList.GetItemText(i);
                    if (itemText == EOSManager.GetEnumDescription(EOSManager.GameMode.AIvsHuman))
                    {
                        gameModeList.SetItemDisabled(i, totalPlayers > 5);
                        break;
                    }
                }
            }
            else
            {
                // W trybie AI vs Human odblokuj wszystkie opcje
                for (int i = 0; i < gameModeList.ItemCount; i++)
                {
                    gameModeList.SetItemDisabled(i, false);
                }
            }
        }
        if (aiTypeList != null)
        {
            aiTypeList.Visible = isHost;
        }
        if (aiAPIKeyInput != null)
        {
            aiAPIKeyInput.Visible = isHost && eosManager != null && eosManager.currentAIType == EOSManager.AIType.API;
        }
        if (apiKeyHelpButton != null)
        {
            apiKeyHelpButton.Visible = isHost && eosManager != null && eosManager.currentAIType == EOSManager.AIType.API;
        }

        if (eosManager != null)
        {
            bool isAIvsHuman = eosManager.currentGameMode == EOSManager.GameMode.AIvsHuman;

            if (universalTeamContainer != null)
            {
                universalTeamContainer.Visible = isAIvsHuman;
            }

            if (teamsContainer != null)
            {
                teamsContainer.Visible = !isAIvsHuman;
            }

            if (neutralTeamContainer != null)
            {
                neutralTeamContainer.Visible = !isAIvsHuman;
            }
        }

        if (aiTypeSelectedLabel != null)
        {
            aiTypeSelectedLabel.Visible = !isHost;
        }

        if (gameModeSelectedLabel != null)
        {
            gameModeSelectedLabel.Visible = !isHost;
        }

        GD.Print($"🔧 UI visibility updated: isHost={isHost}");
    }

    /// <summary>
    /// Callback wywoływany gdy CustomLobbyId zostanie zaktualizowany w EOSManager
    /// </summary>
    private void OnCustomLobbyIdUpdated(string customLobbyId)
    {
        GD.Print($"🆔 [SIGNAL] CustomLobbyId updated: '{customLobbyId}'");
        GD.Print($"   lobbyIdInput is null: {lobbyIdInput == null}");

        if (lobbyIdInput != null)
        {
            GD.Print($"   Current lobbyIdInput.Text: '{lobbyIdInput.Text}'");
            GD.Print($"   lobbyIdInput.Editable: {lobbyIdInput.Editable}");
            GD.Print($"   lobbyIdInput.PlaceholderText: '{lobbyIdInput.PlaceholderText}'");
        }

        // Jeśli CustomLobbyId jest pusty, wyczyść pole
        if (string.IsNullOrEmpty(customLobbyId))
        {
            currentLobbyCode = "";
            if (lobbyIdInput != null)
            {
                CallDeferred(nameof(UpdateLobbyIdDisplay), "");
            }
            GD.Print("🧹 Cleared CustomLobbyId field");
            return;
        }

        if (customLobbyId != "Unknown")
        {
            currentLobbyCode = customLobbyId;

            if (lobbyIdInput != null)
            {
                // Użyj CallDeferred aby upewnić się, że UI jest gotowe
                CallDeferred(nameof(UpdateLobbyIdDisplay), currentLobbyCode);
            }
            else
            {
                GD.PrintErr("❌ lobbyIdInput is NULL!");
            }
        }
        else
        {
            GD.Print($"⚠️ Received invalid CustomLobbyId: '{customLobbyId}'");
        }
    }

    /// <summary>
    /// Callback wywoływany gdy GameMode zostanie zaktualizowany w EOSManager
    /// </summary>
    private void OnGameModeUpdated(string gameMode)
    {
        GD.Print($"🎮 [SIGNAL] GameMode updated: '{gameMode}'");

        // Parsuj string na enum
        EOSManager.GameMode gameModeEnum = EOSManager.ParseEnumFromDescription<EOSManager.GameMode>(gameMode, EOSManager.GameMode.AIMaster);
        GD.Print($"🔍 Parsed GameMode enum: {gameModeEnum}");

        // Zaktualizuj dropdown (dla hosta)
        if (gameModeList != null)
        {
            // Znajdź indeks odpowiadający trybowi gry
            for (int i = 0; i < gameModeList.ItemCount; i++)
            {
                if (gameModeList.GetItemText(i) == gameMode)
                {
                    gameModeList.Selected = i;
                    GD.Print($"✅ GameMode dropdown updated to: {gameMode} (index: {i})");
                    break;
                }
            }
        }

        // Aktualizuj widoczność kontenerów drużyn w zależności od trybu gry
        UpdateUIVisibility();

        // Host przenosi graczy między drużynami
        if (eosManager != null && eosManager.isLobbyOwner)
        {
            if (gameModeEnum == EOSManager.GameMode.AIvsHuman)
            {
                GD.Print("🔄 Host: Moving all players to Universal team...");
                eosManager.MoveAllPlayersToUniversal();
            }
            else if (gameModeEnum == EOSManager.GameMode.AIMaster)
            {
                GD.Print("🔄 Host: Restoring players from Universal team...");
                eosManager.RestorePlayersFromUniversal();
            }
        }

        // Zaktualizuj label (dla graczy)
        if (gameModeSelectedLabel != null)
        {
            gameModeSelectedLabel.Text = gameMode;
            GD.Print($"✅ GameMode label updated to: {gameMode}");
        }

        LobbyStatus.gameModeSet = true;
        if (eosManager != null && eosManager.isLobbyOwner)
        {
            UpdateHostReadyStatus();
        }
    }

    /// <summary>
    /// Callback wywoływany gdy AIType zostanie zaktualizowany w EOSManager
    /// </summary>
    private async void OnAITypeUpdated(string aiType)
    {
        GD.Print($"🤖 [SIGNAL] AIType updated: '{aiType}'");

        LobbyStatus.isAPIKeySet = false;
        SetAPIKeyInputBorder(new Color(0.5f, 0.5f, 0.5f)); // Szary

        // Parsuj string na enum
        EOSManager.AIType aiTypeEnum = EOSManager.ParseEnumFromDescription<EOSManager.AIType>(aiType, EOSManager.AIType.API);
        GD.Print($"🔍 Parsed AIType enum: {aiTypeEnum}");

        // Zaktualizuj dropdown (dla hosta)
        if (aiTypeList != null)
        {
            // Znajdź indeks odpowiadający trybowi gry
            for (int i = 0; i < aiTypeList.ItemCount; i++)
            {
                if (aiTypeList.GetItemText(i) == aiType)
                {
                    aiTypeList.Selected = i;
                    GD.Print($"✅ AIType dropdown updated to: {aiType} (index: {i})");
                    break;
                }
            }

            // Pokaż/ukryj pole klucza API - porównaj z enumem
            if (aiAPIKeyInput != null && eosManager != null)
            {
                bool isHost = eosManager.isLobbyOwner;
                bool shouldShowAPIKey = isHost && aiTypeEnum == EOSManager.AIType.API;
                aiAPIKeyInput.Visible = shouldShowAPIKey;
                apiKeyHelpButton.Visible = shouldShowAPIKey;
            }
        }

        // Zaktualizuj label (dla graczy)
        if (aiTypeSelectedLabel != null)
        {
            aiTypeSelectedLabel.Text = aiType;
            GD.Print($"✅ AIType label updated to: {aiType}");
        }

        //Sprawdź czy API key jest potrzebny i czy jest wypełniony
        if (aiTypeEnum == EOSManager.AIType.API)
        {
            string apiKey = aiAPIKeyInput.Text;
            if (apiKey != "")
            {
                ProceedAPIKey(apiKey);
            }
            else
            {
                LobbyStatus.isAPIKeySet = false;
            }
        }
        else
        {
            // API nie jest wymagane - automatycznie ustawione na true
            LobbyStatus.isAPIKeySet = true;
            GD.Print($"✅ API key not required for {aiTypeEnum}");
        }

        LobbyStatus.aiTypeSet = true;
        if (eosManager != null && eosManager.isLobbyOwner)
        {
            UpdateHostReadyStatus();
        }
    }

    /// <summary>
    /// Callback wywoływany gdy status gotowości lobby zostanie zaktualizowany
    /// </summary>
    private void OnLobbyReadyStatusUpdated(bool isReady)
    {
        GD.Print($"✅ [SIGNAL] Lobby ready status updated: {isReady}");
        UpdateLobbyStatusDisplay(isReady);
    }

    /// <summary>
    /// Host aktualizuje i synchronizuje status gotowości
    /// </summary>
    private void UpdateHostReadyStatus()
    {
        if (eosManager == null || !eosManager.isLobbyOwner)
            return;

        bool isReady = LobbyStatus.IsReadyToStart();
        eosManager.SetLobbyReadyStatus(isReady);
        GD.Print($"📤 Host broadcasting ready status: {isReady}");
    }

    /// <summary>
    /// Sprawdza warunki rozpoczęcia gry dla liczby graczy w drużynach
    /// </summary>
    private void OnCheckTeamsBalanceConditions()
    {
        GD.Print("🎮 [SIGNAL] CheckTeamsBalanceConditions triggered");

        if (blueTeamList == null || redTeamList == null)
            return;

        int blueCount = blueTeamList.ItemCount;
        int redCount = redTeamList.ItemCount;
        int neutralCount = neutralTeamList != null ? neutralTeamList.ItemCount : 0;
        int universalCount = universalTeamList != null ? universalTeamList.ItemCount : 0;

        // Sprawdź tryb gry
        bool isAIvsHuman = eosManager != null && eosManager.currentGameMode == EOSManager.GameMode.AIvsHuman;

        // W trybie AI vs Human wystarczy że Universal ma graczy
        // W trybie AI Master muszą być gracze w Blue i Red
        if (isAIvsHuman)
        {
            if (universalCount > 0)
            {
                LobbyStatus.isTeamNotEmpty = true;
                GD.Print($"✅ Universal team has {universalCount} players (AI vs Human mode).");
            }
            else
            {
                LobbyStatus.isTeamNotEmpty = false;
                GD.Print("❌ Universal team is empty (AI vs Human mode).");
            }
        }
        else
        {
            if (blueCount > 0 && redCount > 0)
            {
                LobbyStatus.isTeamNotEmpty = true;
                GD.Print("✅ Both Blue and Red teams have players (AI Master mode).");
            }
            else
            {
                LobbyStatus.isTeamNotEmpty = false;
                GD.Print("❌ Blue or Red team is empty (AI Master mode).");
            }
        }

        // W trybie AI vs Human nie sprawdzamy MaxPlayersPerTeam dla Blue/Red (są ukryte)
        if (isAIvsHuman)
        {
            LobbyStatus.isAnyTeamFull = false;
        }
        else
        {
            if (blueCount > MaxPlayersPerTeam || redCount > MaxPlayersPerTeam)
            {
                LobbyStatus.isAnyTeamFull = true;
                GD.Print("❌ At least one team is full.");
            }
            else
            {
                LobbyStatus.isAnyTeamFull = false;
                GD.Print("✅ No team is full.");
            }
        }

        // W trybie AI vs Human neutralCount powinien być zawsze 0 (wszyscy w Universal)
        // W trybie AI Master neutralCount też powinien być 0 (wszyscy w Blue/Red)
        if (neutralCount == 0)
        {
            LobbyStatus.isNeutralTeamEmpty = true;
            GD.Print("✅ Neutral team is empty.");
        }
        else
        {
            LobbyStatus.isNeutralTeamEmpty = false;
            GD.Print("❌ Neutral team has players.");
        }

        if (eosManager != null && eosManager.isLobbyOwner)
        {
            UpdateHostReadyStatus();
        }
    }

    /// <summary>
    /// Włącza normalny styl przycisku "Rozpocznij grę"
    /// </summary>
    private void EnableStartGameButtonStyle()
    {
        if (startGameButton == null || leaveLobbyButton == null)
            return;

        startGameButton.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        startGameButton.MouseFilter = Control.MouseFilterEnum.Stop;
        startGameButton.Modulate = new Color(1, 1, 1); // Normalny kolor

        // Przywróć domyślny styl
        var normalStyle = leaveLobbyButton.GetThemeStylebox("normal");
        var hoverStyle = leaveLobbyButton.GetThemeStylebox("hover");
        var pressedStyle = leaveLobbyButton.GetThemeStylebox("pressed");
        var focusStyle = leaveLobbyButton.GetThemeStylebox("focus");
        var fontHoverColor = leaveLobbyButton.GetThemeColor("font_hover_color");

        if (normalStyle != null)
            startGameButton.AddThemeStyleboxOverride("normal", normalStyle);
        if (hoverStyle != null)
            startGameButton.AddThemeStyleboxOverride("hover", hoverStyle);
        if (pressedStyle != null)
            startGameButton.AddThemeStyleboxOverride("pressed", pressedStyle);
        if (focusStyle != null)
            startGameButton.AddThemeStyleboxOverride("focus", focusStyle);

        startGameButton.AddThemeColorOverride("font_hover_color", fontHoverColor);
    }

    /// <summary>
    /// Wyłącza styl przycisku "Rozpocznij grę" (disabled look)
    /// </summary>
    private void DisableStartGameButtonStyle()
    {
        if (startGameButton == null)
            return;

        startGameButton.MouseDefaultCursorShape = Control.CursorShape.Forbidden;
        startGameButton.MouseFilter = Control.MouseFilterEnum.Stop;
        startGameButton.Modulate = new Color(0.5f, 0.5f, 0.5f); // Szary (disabled)

        var normalStyle = startGameButton.GetThemeStylebox("normal");
        if (normalStyle != null)
        {
            startGameButton.AddThemeStyleboxOverride("hover", normalStyle);
            startGameButton.AddThemeStyleboxOverride("pressed", normalStyle);
            startGameButton.AddThemeStyleboxOverride("focus", normalStyle);
        }

        var whiteFontColor = new Color(1, 1, 1); // Biały
        startGameButton.AddThemeColorOverride("font_color", whiteFontColor);
        startGameButton.AddThemeColorOverride("font_hover_color", whiteFontColor);
        startGameButton.AddThemeColorOverride("font_pressed_color", whiteFontColor);
    }

    /// <summary>
    /// Aktualizuje wyświetlanie statusu lobby
    /// </summary>
    private void UpdateLobbyStatusDisplay(bool isReady)
    {
        if (lobbyStatusLabel == null)
            return;

        bool isHost = eosManager != null && eosManager.isLobbyOwner;

        if (isHost)
        {
            List<string> unmetConditions = new List<string>();
            // Host widzi szczegółowy status
            if (isReady)
            {
                // Gdy gotowe
                if (lobbyStatusCounter != null)
                {
                    lobbyStatusCounter.Text = "Status: ";
                    lobbyStatusCounter.Modulate = new Color(0, 1, 0); // Zielony
                    lobbyStatusCounter.Visible = true;
                }

                lobbyStatusLabel.Text = "Gra gotowa";
                lobbyStatusLabel.Modulate = new Color(0, 1, 0); // Zielony

                // Wyczyść tooltip dla gotowego lobby
                lobbyReadyTooltip = "";

                EnableStartGameButtonStyle();
            }
            else
            {
                if (!LobbyStatus.gameModeSet)
                    unmetConditions.Add("Nie wybrano trybu gry");

                if (!LobbyStatus.aiTypeSet)
                    unmetConditions.Add("Nie wybrano typu AI");

                if (!LobbyStatus.isTeamNotEmpty)
                    unmetConditions.Add("Drużyny nie mogą być puste");

                if (!LobbyStatus.isNeutralTeamEmpty)
                    unmetConditions.Add("Występują gracze bez drużyny");

                if (LobbyStatus.isAnyTeamFull)
                    unmetConditions.Add("Jedna z drużyn jest przepełniona");

                if (!LobbyStatus.isAPIKeySet)
                    unmetConditions.Add("Klucz API nie jest poprawny");

                if (unmetConditions.Count > 0)
                {
                    lobbyReadyTooltip = string.Join("\n", unmetConditions);

                    int totalCount = unmetConditions.Count;
                    if (lobbyStatusCounter != null)
                    {
                        if (totalCount > 1)
                        {
                            lobbyStatusCounter.Text = $"Status({totalCount}): ";
                        }
                        else
                        {
                            lobbyStatusCounter.Text = "Status: ";
                        }
                        lobbyStatusCounter.Modulate = new Color(1f, 1f, 1f); // Biały
                        lobbyStatusCounter.Visible = true;
                    }

                    lobbyStatusLabel.Text = unmetConditions[0];
                    lobbyStatusLabel.Modulate = new Color(0.7f, 0.7f, 0.7f); // Szary
                }

                DisableStartGameButtonStyle();
            }

            GD.Print($"📊 Host Status: {(lobbyStatusCounter != null ? lobbyStatusCounter.Text : "")} {lobbyStatusLabel.Text}");
        }
        else
        {
            // Gracze czekają na hosta
            if (lobbyStatusCounter != null)
            {
                lobbyStatusCounter.Text = "Status: ";
                lobbyStatusCounter.Visible = true;
            }

            if (isReady)
            {
                if (lobbyStatusCounter != null)
                {
                    lobbyStatusCounter.Modulate = new Color(0, 1, 0); // Zielony
                }
                lobbyStatusLabel.Text = "Gra gotowa";
                lobbyStatusLabel.Modulate = new Color(0, 1, 0); // Zielony
            }
            else
            {
                if (lobbyStatusCounter != null)
                {
                    lobbyStatusCounter.Modulate = new Color(1f, 1f, 1f); // Biały
                }
                lobbyStatusLabel.Text = "Oczekiwanie na hosta";
                lobbyStatusLabel.Modulate = new Color(0.7f, 0.7f, 0.7f); // Szary
            }

            GD.Print($"📊 Player Status: {(lobbyStatusCounter != null ? lobbyStatusCounter.Text : "")} {lobbyStatusLabel.Text} (isReady={isReady})");
        }
    }

    /// <summary>
    /// Aktualizuje wyświetlanie Lobby ID w polu tekstowym
    /// </summary>
    private void UpdateLobbyIdDisplay(string lobbyId)
    {
        if (lobbyIdInput != null)
        {
            lobbyIdInput.Text = lobbyId;
            GD.Print($"✅ [DEFERRED] Updated Lobby ID input to: '{lobbyIdInput.Text}'");

            // Sprawdź czy wartość rzeczywiście się zmieniła
            if (lobbyIdInput.Text != lobbyId)
            {
                GD.PrintErr($"❌ Failed to update! Expected: '{lobbyId}', Got: '{lobbyIdInput.Text}'");
            }
        }
    }

    /// <summary>
    /// Waliduje czy klucz API jest poprawnie sformatowany
    /// </summary>
    private bool ValidateAPIKey(string apiKey)
    {
        // Sprawdź czy klucz nie jest null lub pusty
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SetAPIKeyInputBorder(new Color(0.5f, 0.5f, 0.5f)); // Szary
            LobbyStatus.isAPIKeySet = false;
            UpdateHostReadyStatusIfOwner();
            return false;
        }

        // Minimalna długość klucza API
        const int MinKeyLength = 35;
        if (apiKey.Length < MinKeyLength)
        {
            GD.Print($"⚠️ API Key is too short: {apiKey.Length} characters (minimum {MinKeyLength})");
            SetAPIKeyInputBorder(new Color(1, 0, 0)); // Czerwony
            LobbyStatus.isAPIKeySet = false;
            UpdateHostReadyStatusIfOwner();
            return false;
        }

        // Sprawdź dozwolone znaki
        foreach (char c in apiKey)
        {
            bool isValidChar = char.IsLetterOrDigit(c) ||
                              c == '-' || c == '_' || c == '.' || c == '~' ||
                              c == ':' || c == '/' || c == '?' || c == '#' ||
                              c == '[' || c == ']' || c == '@' || c == '!' ||
                              c == '$' || c == '&' || c == '\'' || c == '(' ||
                              c == ')' || c == '*' || c == '+' || c == ',' ||
                              c == ';' || c == '=';

            if (!isValidChar)
            {
                GD.Print($"⚠️ API Key contains invalid character: {c}");
                return false;
            }
        }
        return true;
    }

    private async void ProceedAPIKey(string apiKey)
    {
        try
        {
            GD.Print($"Proceeding API Key.");
            LLM apiLLM = new LLM(apiKey);

            // Dane testowe - minimalny request
            string systemPrompt = "test";
            string userPrompt = "test";
            int maxTokens = 1;

            string response = await apiLLM.SendRequestAsync(systemPrompt, userPrompt, maxTokens);

            GD.Print($"✅ API Key validation successful!");
            SetAPIKeyInputBorder(new Color(0, 1, 0)); // Zielony
            LobbyStatus.isAPIKeySet = true;

            // Zapisz zwalidowany klucz API w atrybutach lobby
            if (eosManager != null)
            {
                eosManager.SetAPIKey(apiKey);
            }

            UpdateHostReadyStatusIfOwner();
        }
        catch (Exception ex)
        {
            string errorMessage = ex.Message.ToLower();

            if (errorMessage.Contains("401") || errorMessage.Contains("unauthorized") || errorMessage.Contains("authentication"))
            {
                GD.PrintErr($"❌ API Key validation failed: Invalid API key");
            }
            else if (errorMessage.Contains("429") || errorMessage.Contains("rate_limit") || errorMessage.Contains("too many requests"))
            {
                GD.PrintErr($"❌ API Key validation failed: Rate limit exceeded");
            }
            else if (errorMessage.Contains("quota") || errorMessage.Contains("insufficient_quota") || errorMessage.Contains("limit"))
            {
                GD.PrintErr($"❌ API Key validation failed: Quota exceeded");
            }
            else if (errorMessage.Contains("max_tokens") || errorMessage.Contains("token") && errorMessage.Contains("limit"))
            {
                GD.PrintErr($"❌ API Key validation failed: Token limit in request");
            }
            else if (errorMessage.Contains("400") || errorMessage.Contains("bad request") || errorMessage.Contains("invalid_request"))
            {
                GD.PrintErr($"❌ API Key validation failed: Bad request");
            }
            else if (errorMessage.Contains("500") || errorMessage.Contains("503") || errorMessage.Contains("internal") || errorMessage.Contains("server"))
            {
                GD.PrintErr($"❌ API Key validation failed: Server error");
            }
            else if (errorMessage.Contains("timeout") || errorMessage.Contains("timed out"))
            {
                GD.PrintErr($"❌ API Key validation failed: Timeout");
            }
            else if (errorMessage.Contains("network") || errorMessage.Contains("connection"))
            {
                GD.PrintErr($"❌ API Key validation failed: Network error");
            }
            else
            {
                GD.PrintErr($"❌ API Key validation failed: {ex.Message}");
            }

            SetAPIKeyInputBorder(new Color(1, 0, 0)); // Czerwony
            LobbyStatus.isAPIKeySet = false;
            UpdateHostReadyStatusIfOwner();
        }

    }

    /// <summary>
    /// Helper do aktualizacji statusu gotowości jeśli jesteśmy hostem
    /// </summary>
    private void UpdateHostReadyStatusIfOwner()
    {
        if (eosManager != null && eosManager.isLobbyOwner)
        {
            UpdateHostReadyStatus();
        }
    }

    /// <summary>
    /// Ustawia kolor obramowania dla pola API Key
    /// </summary>
    private void SetAPIKeyInputBorder(Color color)
    {
        if (aiAPIKeyInput != null)
        {
            // Pobierz aktualny theme override lub utwórz nowy StyleBox
            var styleBox = aiAPIKeyInput.GetThemeStylebox("normal") as StyleBoxFlat;
            if (styleBox != null)
            {
                // Klonuj StyleBox aby nie modyfikować oryginalnego
                styleBox = (StyleBoxFlat)styleBox.Duplicate();
                styleBox.BorderColor = color;
                styleBox.BorderWidthLeft = 2;
                styleBox.BorderWidthRight = 2;
                styleBox.BorderWidthTop = 2;
                styleBox.BorderWidthBottom = 2;
                aiAPIKeyInput.AddThemeStyleboxOverride("normal", styleBox);
                aiAPIKeyInput.AddThemeStyleboxOverride("focus", styleBox);
            }
        }
        else
        {
            // Resetuj border do domyślnego
            SetAPIKeyInputBorder(new Color(0.5f, 0.5f, 0.5f));
        }
    }

    /// <summary>
    /// Callback wywoływany gdy użytkownik zmienia tekst w polu API Key
    /// </summary>
    private void OnAPIKeyTextChanged(string newText)
    {
        SetAPIKeyInputBorder(new Color(0.7f, 0.7f, 0.7f));

        // Resetuj flagę walidacji - użytkownik musi ponownie wcisnąć Enter
        if (LobbyStatus.isAPIKeySet)
        {
            LobbyStatus.isAPIKeySet = false;
            UpdateHostReadyStatusIfOwner();
        }
    }

    /// <summary>
    /// Callback wywoływany gdy użytkownik wciśnie Enter w polu API Key
    /// </summary>
    private void OnAPIKeySubmitted(string newText)
    {
        bool isValid = ValidateAPIKey(newText);
        if (!isValid)
        {
            GD.Print($"⚠️ Invalid API Key. Aborting submission.");
            return;
        }
        ProceedAPIKey(newText);
    }

    /// <summary>
    /// Aktualizuje listę graczy w drużynie
    /// </summary>
    /// <param name="teamList">Lista drużyny do zaktualizowania</param>
    /// <param name="players">Tablica nazw graczy</param>
    public void UpdateTeamList(ItemList teamList, string[] players)
    {
        if (teamList == null) return;

        teamList.Clear();
        foreach (string player in players)
        {
            teamList.AddItem(player);
        }
    }

    private void OnSelectedGameModeChanged(long index)
    {
        if (gameModeList == null || eosManager == null) return;

        string selectedModeStr = gameModeList.GetItemText((int)index);
        EOSManager.GameMode selectedMode = EOSManager.ParseEnumFromDescription<EOSManager.GameMode>(selectedModeStr, EOSManager.GameMode.AIMaster);

        GD.Print($"👆 User selected game mode: {selectedModeStr} -> {selectedMode}");

        // Sprawdź czy próbujemy zmienić na AI vs Human
        if (selectedMode == EOSManager.GameMode.AIvsHuman)
        {
            // Policz wszystkich graczy (Blue + Red + Neutral)
            int totalPlayers = 0;
            if (blueTeamList != null) totalPlayers += blueTeamList.ItemCount;
            if (redTeamList != null) totalPlayers += redTeamList.ItemCount;
            if (neutralTeamList != null) totalPlayers += neutralTeamList.ItemCount;

            // Jeśli jest więcej niż 5 graczy, nie pozwól na zmianę
            if (totalPlayers > 5)
            {
                GD.PrintErr($"❌ Cannot switch to AI vs Human mode: Too many players ({totalPlayers}/5)");

                // Przywróć poprzednią wartość w dropdown (AI Master)
                for (int i = 0; i < gameModeList.ItemCount; i++)
                {
                    if (gameModeList.GetItemText(i) == EOSManager.GetEnumDescription(EOSManager.GameMode.AIMaster))
                    {
                        gameModeList.Selected = i;
                        break;
                    }
                }

                return;
            }
        }

        //zablokuj buttonList by uniknąć wielokrotnych zapytań
        BlockButtonToHandleTooManyRequests(gameModeList);

        // Ustaw tryb gry w EOSManager - zostanie zsynchronizowany z innymi graczami
        eosManager.SetGameMode(selectedMode);
        LobbyStatus.gameModeSet = true;
        UpdateHostReadyStatus();
    }
    private void OnSelectedAITypeChanged(long index)
    {
        if (aiTypeList == null || eosManager == null) return;

        string selectedAITypeStr = aiTypeList.GetItemText((int)index);
        EOSManager.AIType selectedAIType = EOSManager.ParseEnumFromDescription<EOSManager.AIType>(selectedAITypeStr, EOSManager.AIType.API);

        GD.Print($"👆 User selected AI type: {selectedAITypeStr} -> {selectedAIType}");

        //zablokuj buttonList by uniknąć wielokrotnych zapytań
        BlockButtonToHandleTooManyRequests(aiTypeList);

        // Ustaw tryb gry w EOSManager - zostanie zsynchronizowany z innymi graczami
        eosManager.SetAIType(selectedAIType);
        LobbyStatus.aiTypeSet = true;
        UpdateHostReadyStatus();
    }

    /// <summary>
    /// Callback wywoływany gdy użytkownik kliknie przycisk pomocy do klucza API
    /// </summary>
    private void OnAPIKeyHelpButtonPressed()
    {
        string helpUrl = "https://www.deepseek.com/en";

        if (OS.GetName() == "Windows") //Windows
        {
            Process.Start("cmd", $"/c start {helpUrl}");
        }
        else if (OS.GetName() == "macOS") // macOS
        {
            Process.Start("open", helpUrl);
        }
        else // Linux
        {
            Process.Start("xdg-open", helpUrl);
        }
    }

    private void OnCopyIdButtonPressed()
    {
        if (!string.IsNullOrEmpty(currentLobbyCode))
        {
            DisplayServer.ClipboardSet(currentLobbyCode);
            GD.Print($"✅ Lobby ID copied to clipboard: {currentLobbyCode}");
        }
        else
        {
            GD.Print("⚠️ No lobby ID to copy");
        }
    }

    private void OnGenerateNewIdButtonPressed()
    {
        // Wygeneruj nowy kod
        string newCode = GenerateLobbyIDCode();
        currentLobbyCode = newCode;

        // Wyświetl w UI i zaktualizuj w EOSManager
        if (lobbyIdInput != null)
        {
            CallDeferred(nameof(UpdateLobbyIdDisplay), newCode);
            eosManager.SetCustomLobbyId(newCode);
        }

        GD.Print($"✅ New lobby ID generated: {newCode}");

        //zablokuj button by uniknąć wielokrotnych zapytań
        BlockButtonToHandleTooManyRequests(generateNewIdButton);
    }

    // Obsługa przycisku "Start gry" - tylko host inicjuje start sesji
    private void OnStartGamePressed()
    {
        // Sprawdź czy gra jest gotowa do startu
        if (!LobbyStatus.IsReadyToStart())
        {
            GD.Print("⚠️ Cannot start game - conditions not met");
            return;
        }

        // TYLKO HOST może rozpocząć sesję
        if (eosManager == null || !eosManager.isLobbyOwner)
        {
            GD.Print("⚠️ Only host can start the game");
            return;
        }

        GD.Print("🎮 Host requests game session start...");
        eosManager.RequestStartGameSession();
        
    }

    private void OnBackButtonPressed()
    {
        GD.Print("Returning to main menu...");

        // Opuść lobby jeśli jesteś w jakimś
        if (eosManager != null && !string.IsNullOrEmpty(eosManager.currentLobbyId))
        {
            GD.Print("🚪 Leaving lobby before returning to menu...");
            eosManager.LeaveLobby();
        }

        GetTree().ChangeSceneToFile("res://scenes/menu/main.tscn");
    }

    private void OnLeaveLobbyPressed()
    {
        GD.Print("Returning to main menu...");

        // Opuść lobby jeśli jesteś w jakimś
        if (eosManager != null && !string.IsNullOrEmpty(eosManager.currentLobbyId))
        {
            GD.Print("🚪 Leaving lobby before returning to menu...");
            eosManager.LeaveLobby();
        }

        GetTree().ChangeSceneToFile("res://scenes/menu/main.tscn");
    }

    private async void CreateLobbyWithRetry(int attempt = 0)
    {
        // Sprawdź czy użytkownik jest już zalogowany
        if (eosManager == null)
        {
            GD.Print("⚠️ EOSManager not found, retrying in 0.5s...");
            await ToSignal(GetTree().CreateTimer(RetryDelay), SceneTreeTimer.SignalName.Timeout);
            CreateLobbyWithRetry(attempt + 1);
            return;
        }

        // Sprawdź czy już nie ma lobby (np. powrót z innej sceny)
        if (!string.IsNullOrEmpty(eosManager.currentLobbyId))
        {
            GD.Print($"✅ Already in lobby: {eosManager.currentLobbyId}");
            return;
        }

        // Sprawdź czy EOS jest zalogowany
        if (!eosManager.IsLoggedIn())
        {
            if (attempt < MaxRetryAttempts)
            {
                GD.Print($"⏳ Waiting for EOS login... (attempt {attempt + 1}/{MaxRetryAttempts})");
                await ToSignal(GetTree().CreateTimer(RetryDelay), SceneTreeTimer.SignalName.Timeout);
                CreateLobbyWithRetry(attempt + 1);
            }
            else
            {
                GD.Print("❌ EOS login timeout - could not create lobby");
            }
            return;
        }

        // Teraz możemy bezpiecznie utworzyć lobby
        string lobbyIdCode = GenerateLobbyIDCode();
        currentLobbyCode = lobbyIdCode;


        // Wyświetl kod w UI
        if (lobbyIdInput != null)
        {
            CallDeferred(nameof(UpdateLobbyIdDisplay), lobbyIdCode);
        }

        eosManager.CreateLobby(lobbyIdCode, LobbyMaxPlayers, true);
        GD.Print("✅ EOS logged in, creating lobby. Lobby ID: " + lobbyIdCode);
    }

    private void OnBlueTeamJoinButtonPressed()
    {
        TryJoinTeam(EOSManager.Team.Blue);
    }

    private void OnRedTeamJoinButtonPressed()
    {
        TryJoinTeam(EOSManager.Team.Red);
    }

    private void OnReadyTooltipMouseEntered()
    {
        if (customTooltip != null && !string.IsNullOrEmpty(lobbyReadyTooltip))
        {
            customTooltip.Show(lobbyReadyTooltip);
        }
    }

    private void OnReadyTooltipMouseExited()
    {
        if (customTooltip != null)
        {
            customTooltip.Hide();
        }
    }

    private void OnLeaveTeamButtonPressed()
    {
        TryLeftTeam();
    }

    private EOSManager.Team currentLocalTeam = EOSManager.Team.None;

    private void TryJoinTeam(EOSManager.Team teamName)
    {
        if (eosManager == null)
        {
            GD.PrintErr("❌ Cannot change team: EOSManager not available");
            return;
        }

        // Sprawdź czy cooldown jest aktywny
        if (isTeamChangeCooldownActive)
        {
            return;
        }

        if (currentLocalTeam == teamName)
        {
            GD.Print($"ℹ️ Already in {teamName} team, ignoring join request");
            return;
        }

        // Aktywuj globalny cooldown na ustalony czas
        isTeamChangeCooldownActive = true;

        // Od razu zaktualizuj stan przycisków
        UpdateTeamButtonsState(currentLocalTeam);

        GetTree().CreateTimer(CooldownTime).Timeout += () =>
        {
            // Sprawdź czy scena nadal istnieje
            if (!IsInsideTree())
                return;

            isTeamChangeCooldownActive = false;
            GD.Print("✅ Team change cooldown finished");
            // Zaktualizuj stan przycisków po zakończeniu cooldownu
            UpdateTeamButtonsState(currentLocalTeam);
        };

        eosManager.SetMyTeam(teamName);
        GD.Print($"🔁 Sending request to join {teamName} team");
    }
    private void TryLeftTeam()
    {
        if (eosManager == null)
        {
            GD.PrintErr("❌ Cannot leave team: EOSManager not available");
            return;
        }
        TryJoinTeam(EOSManager.Team.None);
    }

    /// <summary>
    /// Sprawdza czy drużyna osiągnęła limit graczy
    /// </summary>
    private bool IsTeamFull(EOSManager.Team team)
    {
        switch (team)
        {
            case EOSManager.Team.Blue:
                return blueTeamList != null && blueTeamList.ItemCount >= MaxPlayersPerTeam;
            case EOSManager.Team.Red:
                return redTeamList != null && redTeamList.ItemCount >= MaxPlayersPerTeam;
            default:
                return false; // Neutral i Universal nie mają limitu
        }
    }

    private EOSManager.Team previousLocalTeam = EOSManager.Team.None;

    private void UpdateTeamButtonsState(EOSManager.Team localTeam)
    {
        bool teamChanged = (previousLocalTeam != localTeam);
        previousLocalTeam = localTeam;
        currentLocalTeam = localTeam;

        bool isBlueTeamFull = IsTeamFull(EOSManager.Team.Blue);
        bool isRedTeamFull = IsTeamFull(EOSManager.Team.Red);

        if (blueTeamJoinButton != null)
        {
            if (blueTeamJoinButton.IsConnected("pressed", Callable.From(OnBlueTeamJoinButtonPressed)))
            {
                blueTeamJoinButton.Pressed -= OnBlueTeamJoinButtonPressed;
            }
            if (blueTeamJoinButton.IsConnected("pressed", Callable.From(OnLeaveTeamButtonPressed)))
            {
                blueTeamJoinButton.Pressed -= OnLeaveTeamButtonPressed;
            }

            if (currentLocalTeam == EOSManager.Team.Blue)
            {
                blueTeamJoinButton.Text = "Opuść";
                blueTeamJoinButton.Pressed += OnLeaveTeamButtonPressed;
                // Ustaw stan przycisku na podstawie globalnego cooldownu
                blueTeamJoinButton.Disabled = isTeamChangeCooldownActive;
            }
            else
            {
                blueTeamJoinButton.Text = isBlueTeamFull ? "Pełna" : "Dołącz";
                // Zablokuj gdy drużyna pełna LUB gdy cooldown aktywny
                blueTeamJoinButton.Disabled = isBlueTeamFull || isTeamChangeCooldownActive;
                blueTeamJoinButton.Pressed += OnBlueTeamJoinButtonPressed;
            }
        }

        if (redTeamJoinButton != null)
        {
            if (redTeamJoinButton.IsConnected("pressed", Callable.From(OnRedTeamJoinButtonPressed)))
            {
                redTeamJoinButton.Pressed -= OnRedTeamJoinButtonPressed;
            }
            if (redTeamJoinButton.IsConnected("pressed", Callable.From(OnLeaveTeamButtonPressed)))
            {
                redTeamJoinButton.Pressed -= OnLeaveTeamButtonPressed;
            }

            if (currentLocalTeam == EOSManager.Team.Red)
            {
                redTeamJoinButton.Text = "Opuść";
                redTeamJoinButton.Pressed += OnLeaveTeamButtonPressed;
                // Ustaw stan przycisku na podstawie globalnego cooldownu
                redTeamJoinButton.Disabled = isTeamChangeCooldownActive;
            }
            else
            {
                redTeamJoinButton.Text = isRedTeamFull ? "Pełna" : "Dołącz";
                // Zablokuj gdy drużyna pełna LUB gdy cooldown aktywny
                redTeamJoinButton.Disabled = isRedTeamFull || isTeamChangeCooldownActive;
                redTeamJoinButton.Pressed += OnRedTeamJoinButtonPressed;
            }
        }
    }

    private void BlockButtonToHandleTooManyRequests(Button button)
    {
        if (button == null) return;

        button.Disabled = true;

        // Odblokuj przycisk po ustalonym czasie
        GetTree().CreateTimer(CooldownTime).Timeout += () =>
        {
            // Sprawdź czy przycisk nadal istnieje przed odwołaniem
            if (button != null && GodotObject.IsInstanceValid(button))
            {
                button.Disabled = false;
            }
        };
    }

    private void StartPlayerMoveCooldown(string userId)
    {
        playerMoveCooldowns[userId] = true;

        GetTree().CreateTimer(CooldownTime).Timeout += () =>
        {
            if (playerMoveCooldowns.ContainsKey(userId))
            {
                playerMoveCooldowns[userId] = false;
            }
        };
    }

    private void OnTeamListGuiInput(InputEvent @event, ItemList teamList)
    {
        if (!eosManager.isLobbyOwner)
        {
            return;
        }

        if (@event is InputEventMouseButton mouseEvent)
        {
            if (mouseEvent.ButtonIndex == MouseButton.Right && mouseEvent.Pressed)
            {
                // Sprawdź czy kliknęliśmy na gracza
                int clickedIndex = teamList.GetItemAtPosition(mouseEvent.Position);

                if (clickedIndex >= 0)
                {
                    var metadata = teamList.GetItemMetadata(clickedIndex).AsGodotDictionary();

                    if (metadata != null && metadata.ContainsKey("userId") && metadata.ContainsKey("isLocalPlayer"))
                    {
                        bool isLocalPlayer = (bool)metadata["isLocalPlayer"];

                        // Nie możemy wyrzucić siebie!
                        if (!isLocalPlayer)
                        {
                            string userId = metadata["userId"].ToString();
                            string displayName = teamList.GetItemText(clickedIndex);
                            EOSManager.Team playerTeam = EOSManager.Team.None;

                            if (metadata.ContainsKey("team"))
                            {
                                string teamStr = metadata["team"].ToString();
                                if (Enum.TryParse<EOSManager.Team>(teamStr, out EOSManager.Team parsedTeam))
                                {
                                    playerTeam = parsedTeam;
                                }
                            }

                            GD.Print($"🖱️ Right-clicked on player: {displayName} ({userId})");
                            ShowMemberActionsPopup(userId, displayName, playerTeam, mouseEvent.GlobalPosition);
                        }
                    }
                }
            }
        }
    }

    private void ShowMemberActionsPopup(string userId, string displayName, EOSManager.Team currentTeam, Vector2 globalPosition)
    {
        GD.Print($"📋 Creating popup menu for {displayName}");

        bool isBlueTeamFull = IsTeamFull(EOSManager.Team.Blue);
        bool isRedTeamFull = IsTeamFull(EOSManager.Team.Red);

        // Sprawdź czy dla tego gracza jest aktywny cooldown
        bool hasPlayerCooldown = playerMoveCooldowns.ContainsKey(userId) && playerMoveCooldowns[userId];
        var popup = new PopupMenu();

        if (eosManager.currentGameMode == EOSManager.GameMode.AIvsHuman)
        {
            // Opcje zarządzania lobby (tryb AI vs Human)
            int idxTransferHost = 0;
            popup.AddItem($"Przekaż hosta", idxTransferHost);
            
            int idxKickPlayer = 1;
            popup.AddItem($"Wyrzuć z lobby", idxKickPlayer);
            
            popup.IndexPressed += (index) =>
            {
                GD.Print($"📋 Popup menu item {index} pressed for {displayName}");
                if (index == idxTransferHost)
                {
                    GD.Print($"👑 Transferring host to: {displayName}");
                    eosManager.TransferLobbyOwnership(userId);
                }
                else if (index == idxKickPlayer)
                {
                    GD.Print($"👢 Kicking player: {displayName}");
                    eosManager.KickPlayer(userId);
                }

                popup.QueueFree();
            };
        }
        else
        {
            // Opcje zarządzania drużynami
            int currentIndex = 0;

            int idxMoveBlue = currentIndex++;
            popup.AddItem("Przenieś do Niebieskich");
            popup.SetItemDisabled(idxMoveBlue, currentTeam == EOSManager.Team.Blue || isBlueTeamFull || hasPlayerCooldown);

            int idxMoveRed = currentIndex++;
            popup.AddItem("Przenieś do Czerwonych");
            popup.SetItemDisabled(idxMoveRed, currentTeam == EOSManager.Team.Red || isRedTeamFull || hasPlayerCooldown);

            int idxMoveNeutral = currentIndex++;
            popup.AddItem("Wyrzuć z drużyny");
            popup.SetItemDisabled(idxMoveNeutral, currentTeam == EOSManager.Team.None || hasPlayerCooldown);

            popup.AddSeparator();
            currentIndex++; // Separator też zajmuje index

            // Opcje zarządzania lobby
            int idxTransferHost = currentIndex++;
            popup.AddItem($"Przekaż hosta");

            int idxKickPlayer = currentIndex++;
            popup.AddItem($"Wyrzuć z lobby");

            popup.IndexPressed += (index) =>
            {
                GD.Print($"📋 Popup menu item {index} pressed for {displayName}");

                if (index == idxMoveBlue)
                {
                    GD.Print($"🔁 Moving player {displayName} to Blue via popup");
                    eosManager.MovePlayerToTeam(userId, EOSManager.Team.Blue);
                    StartPlayerMoveCooldown(userId);
                }
                else if (index == idxMoveRed)
                {
                    GD.Print($"🔁 Moving player {displayName} to Red via popup");
                    eosManager.MovePlayerToTeam(userId, EOSManager.Team.Red);
                    StartPlayerMoveCooldown(userId);
                }
                else if (index == idxMoveNeutral)
                {
                    GD.Print($"🔁 Moving player {displayName} to Neutral via popup");
                    eosManager.MovePlayerToTeam(userId, EOSManager.Team.None);
                    StartPlayerMoveCooldown(userId);
                }
                else if (index == idxTransferHost)
                {
                    GD.Print($"👑 Transferring host to: {displayName}");
                    eosManager.TransferLobbyOwnership(userId);
                }
                else if (index == idxKickPlayer)
                {
                    GD.Print($"👢 Kicking player: {displayName}");
                    eosManager.KickPlayer(userId);
                }

                popup.QueueFree();
            };
        }

        // Dodaj do drzewa i pokaż
        GetTree().Root.AddChild(popup);
        popup.Position = (Vector2I)globalPosition;
        popup.PopupOnParent(new Rect2I(popup.Position, new Vector2I(1, 1)));

        GD.Print($"📋 Popup shown at position {globalPosition}");
    }

    public override void _ExitTree()
    {
        base._ExitTree();

        // Odłącz sygnały przy wyjściu
        if (eosManager != null)
        {
            eosManager.LobbyMembersUpdated -= OnLobbyMembersUpdated;
            eosManager.CustomLobbyIdUpdated -= OnCustomLobbyIdUpdated;
            eosManager.GameModeUpdated -= OnGameModeUpdated;
            eosManager.AITypeUpdated -= OnAITypeUpdated;
            eosManager.CheckTeamsBalanceConditions -= OnCheckTeamsBalanceConditions;
            eosManager.LobbyReadyStatusUpdated -= OnLobbyReadyStatusUpdated;
            // Game session: odpinamy sygnał startu sesji (żeby nie został podwójny handler po ponownym wejściu na scenę)
            eosManager.GameSessionStartRequested -= OnGameSessionStartRequested;
        }

        if (aiAPIKeyInput != null)
        {
            aiAPIKeyInput.TextSubmitted -= OnAPIKeySubmitted;
            aiAPIKeyInput.TextChanged -= OnAPIKeyTextChanged;
        }

        if (startGameButton != null)
        {
            startGameButton.MouseEntered -= OnReadyTooltipMouseEntered;
            startGameButton.MouseExited -= OnReadyTooltipMouseExited;
        }

        if (lobbyStatusCounter != null)
        {
            lobbyStatusCounter.MouseEntered -= OnReadyTooltipMouseEntered;
            lobbyStatusCounter.MouseExited -= OnReadyTooltipMouseExited;
        }

        if (blueTeamJoinButton != null)
        {
            if (blueTeamJoinButton.IsConnected("pressed", Callable.From(OnBlueTeamJoinButtonPressed)))
            {
                blueTeamJoinButton.Pressed -= OnBlueTeamJoinButtonPressed;
            }
            if (blueTeamJoinButton.IsConnected("pressed", Callable.From(OnLeaveTeamButtonPressed)))
            {
                blueTeamJoinButton.Pressed -= OnLeaveTeamButtonPressed;
            }
        }

        if (redTeamJoinButton != null)
        {
            if (redTeamJoinButton.IsConnected("pressed", Callable.From(OnRedTeamJoinButtonPressed)))
            {
                redTeamJoinButton.Pressed -= OnRedTeamJoinButtonPressed;
            }
            if (redTeamJoinButton.IsConnected("pressed", Callable.From(OnLeaveTeamButtonPressed)))
            {
                redTeamJoinButton.Pressed -= OnLeaveTeamButtonPressed;
            }
        }
    }

}