using Godot;
using System;
using System.Collections.Generic;

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
    [Export] private OptionButton aiTypeList;
    [Export] private Label gameModeSelectedLabel;
    [Export] private Label aiTypeSelectedLabel;
    [Export] private LineEdit aiAPIKeyInput;
    [Export] private Label lobbyStatusLabel;
    [Export] private Label lobbyStatusCounter;

    private LobbyLeaveConfirmation leaveConfirmation;
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

        // Inicjalizuj LobbyLeaveConfirmation
        leaveConfirmation = GetNode<LobbyLeaveConfirmation>("LobbyLeaveConfirmation");

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

        if (startGameButton != null)
        {
            startGameButton.Pressed += OnStartGamePressed;
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

        // Podłącz walidację API key przy zmianie tekstu
        if (aiAPIKeyInput != null)
        {
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
    private void OnAITypeUpdated(string aiType)
    {
        GD.Print($"🤖 [SIGNAL] AIType updated: '{aiType}'");

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
            }
        }

        // Zaktualizuj label (dla graczy)
        if (aiTypeSelectedLabel != null)
        {
            aiTypeSelectedLabel.Text = aiType;
            GD.Print($"✅ AIType label updated to: {aiType}");
        }

        //Jeśli nie jest potrzebne API to nie sprawdzaj go by rozpocząć rozgrywkę - porównaj z enumem
        if (aiTypeEnum != EOSManager.AIType.API)
        {
            LobbyStatus.isAPIKeySet = true;
            GD.Print($"✅ API key not required for {aiTypeEnum}");
        }
        else
        {
            LobbyStatus.isAPIKeySet = false;
            GD.Print($"⚠️ API key required for {aiTypeEnum}");
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
    /// Aktualizuje wyświetlanie statusu lobby
    /// </summary>
    private void UpdateLobbyStatusDisplay(bool isReady)
    {
        if (lobbyStatusLabel == null)
            return;

        bool isHost = eosManager != null && eosManager.isLobbyOwner;

        if (isHost)
        {
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

                if (startGameButton != null)
                {
                    startGameButton.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
                    startGameButton.MouseFilter = Control.MouseFilterEnum.Stop;
                    startGameButton.Modulate = new Color(1, 1, 1); // Normalny kolor
                }
            }
            else
            {
                var unmetConditions = new System.Collections.Generic.List<string>();

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

                if (startGameButton != null)
                {
                    startGameButton.MouseDefaultCursorShape = Control.CursorShape.Arrow;
                    startGameButton.MouseFilter = Control.MouseFilterEnum.Ignore;
                    startGameButton.Modulate = new Color(0.5f, 0.5f, 0.5f); // Szary (disabled)
                }
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

    //DO POPRAWIENIA GDY DOSTANIEMY SPECYFIKACJE KLUCZA API!!!!
    /// <summary>
    /// Waliduje czy klucz API jest poprawnie sformatowany
    /// </summary>
    private bool ValidateAPIKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            GD.Print("⚠️ API Key is empty");
            return false;
        }

        apiKey = apiKey.Trim();

        // Minimalna długość klucza API
        const int MinKeyLength = 20;
        if (apiKey.Length < MinKeyLength)
        {
            GD.Print($"⚠️ API Key too short ({apiKey.Length} chars, minimum {MinKeyLength})");
            return false;
        }

        // Sprawdź dozwolone znaki (alfanumeryczne i kilka symboli)
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
                GD.Print($"⚠️ API Key contains invalid character: '{c}'");
                return false;
            }
        }

        // Sprawdź czy nie jest typowym placeholder'em
        string lowerKey = apiKey.ToLower();
        if (lowerKey.Contains("your_api_key") ||
            lowerKey.Contains("insert") ||
            lowerKey.Contains("paste") ||
            lowerKey.Contains("example") ||
            lowerKey == "xxxx" ||
            lowerKey == "****")
        {
            GD.Print("⚠️ API Key looks like a placeholder");
            return false;
        }

        GD.Print($"✅ API Key validation passed ({apiKey.Length} chars)");
        return true;
    }

    /// <summary>
    /// Callback wywoływany przy zmianie tekstu w polu API Key
    /// </summary>
    private void OnAPIKeyTextChanged(string newText)
    {
        bool isValid = ValidateAPIKey(newText);
        LobbyStatus.isAPIKeySet = isValid;

        if (eosManager != null && eosManager.isLobbyOwner)
        {
            UpdateHostReadyStatus();
        }
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

    private void OnStartGamePressed()
    {
        // Sprawdź czy gra jest gotowa do startu
        if (!LobbyStatus.IsReadyToStart())
        {
            GD.Print("⚠️ Cannot start game - conditions not met");
            return;
        }

        GD.Print("🎮 Starting game...");
        GetTree().ChangeSceneToFile("res://scenes/game/main_game.tscn");
    }

    private void OnBackButtonPressed()
    {
        if (leaveConfirmation != null)
        {
            leaveConfirmation.ShowConfirmation();
        }
    }

    private void OnLeaveLobbyPressed()
    {
        if (leaveConfirmation != null)
        {
            leaveConfirmation.ShowConfirmation();
        }
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

    private void OnLeaveTeamButtonPressed()
    {
        TryLeftTeam();
    }

    private EOSManager.Team currentLocalTeam = EOSManager.Team.None;

    // Enum dla akcji w popup menu gracza
    private enum PlayerPopupAction
    {
        MoveToBlue = 0,
        MoveToRed = 1,
        MoveToNeutral = 2,
        KickPlayer = 4
    }

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
            //jest tylko jedna opcja - pomijamy enum
            popup.AddItem($"Wyrzuć z lobby", 0);
            popup.IndexPressed += (index) =>
            {
                GD.Print($"📋 Popup menu item {index} pressed for {displayName}");
                if (index == 0)
                {
                    GD.Print($"👢 Kicking player: {displayName}");
                    eosManager.KickPlayer(userId);
                }

                popup.QueueFree();
            };
        }
        else
        {
            popup.AddItem("Przenieś do Niebieskich", (int)PlayerPopupAction.MoveToBlue);
            popup.SetItemDisabled((int)PlayerPopupAction.MoveToBlue, currentTeam == EOSManager.Team.Blue || isBlueTeamFull || hasPlayerCooldown);
            popup.AddItem("Przenieś do Czerwonych", (int)PlayerPopupAction.MoveToRed);
            popup.SetItemDisabled((int)PlayerPopupAction.MoveToRed, currentTeam == EOSManager.Team.Red || isRedTeamFull || hasPlayerCooldown);
            popup.AddItem("Wyrzuć z drużyny", (int)PlayerPopupAction.MoveToNeutral);
            popup.SetItemDisabled((int)PlayerPopupAction.MoveToNeutral, currentTeam == EOSManager.Team.None || hasPlayerCooldown);
            popup.AddSeparator();
            popup.AddItem($"Wyrzuć z lobby", (int)PlayerPopupAction.KickPlayer);

            popup.IndexPressed += (index) =>
            {
                GD.Print($"📋 Popup menu item {index} pressed for {displayName}");

                switch (index)
                {
                    case (int)PlayerPopupAction.MoveToBlue:
                        GD.Print($"🔁 Moving player {displayName} to Blue via popup");
                        eosManager.MovePlayerToTeam(userId, EOSManager.Team.Blue);
                        StartPlayerMoveCooldown(userId);
                        break;
                    case (int)PlayerPopupAction.MoveToRed:
                        GD.Print($"🔁 Moving player {displayName} to Red via popup");
                        eosManager.MovePlayerToTeam(userId, EOSManager.Team.Red);
                        StartPlayerMoveCooldown(userId);
                        break;
                    case (int)PlayerPopupAction.MoveToNeutral:
                        GD.Print($"🔁 Moving player {displayName} to Neutral via popup");
                        eosManager.MovePlayerToTeam(userId, EOSManager.Team.None);
                        StartPlayerMoveCooldown(userId);
                        break;
                    case (int)PlayerPopupAction.KickPlayer:
                        GD.Print($"👢 Kicking player: {displayName}");
                        eosManager.KickPlayer(userId);
                        break;
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
        }

        if (aiAPIKeyInput != null)
        {
            aiAPIKeyInput.TextChanged -= OnAPIKeyTextChanged;
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