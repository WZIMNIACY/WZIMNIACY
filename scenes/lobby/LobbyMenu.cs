using Godot;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using AI;
using Diagnostics;

/// <summary>
/// Główny ekran lobby odpowiedzialny za zarządzanie listą graczy, drużynami,
/// trybem gry, typem AI, kluczem API oraz rozpoczęciem sesji gry. Integruje
/// sygnały i wywołania z <see cref="EOSManager"/>, korzysta z detekcji sprzętu
/// (<see cref="Diagnostics.HardwareResources"/>) oraz pokazuje podpowiedzi przez
/// <see cref="CustomTooltip"/>.
/// <remarks>
/// Zakłada obecność autoloadu <see cref="EOSManager"/> oraz zainicjalizowanych węzłów sceny (przypięte exporty).
/// Operacje powinny być wywoływane w wątku głównym Godota; nie jest thread-safe.
/// </remarks> 
/// </summary>
public partial class LobbyMenu : Control
{
    /// <summary>Referencja do autoloadu EOS obsługującego lobby.</summary>
    private EOSManager eosManager;
    /// <summary>Przycisk powrotu do poprzedniej sceny.</summary>
    [Export] private Button backButton;
    /// <summary>Przycisk opuszczenia lobby.</summary>
    [Export] private Button leaveLobbyButton;
    /// <summary>Lista graczy w drużynie Niebieskich.</summary>
    [Export] private ItemList blueTeamList;
    /// <summary>Lista graczy w drużynie Czerwonych.</summary>
    [Export] private ItemList redTeamList;
    /// <summary>Lista graczy nieprzypisanych (Neutral).</summary>
    [Export] private ItemList neutralTeamList;
    /// <summary>Lista graczy w drużynie Uniwersalnej (AI vs Human).</summary>
    [Export] private ItemList universalTeamList;
    /// <summary>Kontener dla drużyn Blue/Red.</summary>
    [Export] private HBoxContainer teamsContainer;
    /// <summary>Kontener widoku drużyny Uniwersalnej.</summary>
    [Export] private PanelContainer universalTeamContainer;
    /// <summary>Kontener listy Neutral.</summary>
    [Export] private PanelContainer neutralTeamContainer;
    /// <summary>Przycisk dołączenia do Niebieskich.</summary>
    [Export] private Button blueTeamJoinButton;
    /// <summary>Przycisk dołączenia do Czerwonych.</summary>
    [Export] private Button redTeamJoinButton;
    /// <summary>Etykieta licznika drużyny Niebieskich.</summary>
    [Export] private Label blueTeamCountLabel;
    /// <summary>Etykieta licznika drużyny Czerwonych.</summary>
    [Export] private Label redTeamCountLabel;
    /// <summary>Etykieta licznika drużyny Uniwersalnej.</summary>
    [Export] private Label universalTeamCountLabel;
    /// <summary>Pole tekstowe z aktualnym kodem lobby.</summary>
    [Export] private LineEdit lobbyIdInput;
    /// <summary>Przycisk kopiowania kodu lobby.</summary>
    [Export] private Button copyIdButton;
    /// <summary>Przycisk generowania nowego kodu lobby.</summary>
    [Export] private Button generateNewIdButton;
    /// <summary>Przycisk rozpoczęcia gry (tylko host).</summary>
    [Export] private Button startGameButton;
    /// <summary>Lista wyboru trybu gry.</summary>
    [Export] private OptionButton gameModeList;
    /// <summary>Kontener na ustawienia API AI.</summary>
    [Export] private HBoxContainer aiAPIBox;
    /// <summary>Lista wyboru typu AI.</summary>
    [Export] private OptionButton aiTypeList;
    /// <summary>Etykieta prezentująca tryb gry dla graczy (gdy nie są hostami).</summary>
    [Export] private Label gameModeSelectedLabel;
    /// <summary>Etykieta prezentująca wybrany typ AI dla graczy.</summary>
    [Export] private Label aiTypeSelectedLabel;
    /// <summary>Pole na klucz API (pokazywane tylko hostowi dla AI typu API).</summary>
    [Export] private LineEdit aiAPIKeyInput;
    /// <summary>Przycisk pomocy dot. pozyskania klucza API.</summary>
    [Export] private Button apiKeyHelpButton;
    /// <summary>Etykieta głównego statusu lobby.</summary>
    [Export] private Label lobbyStatusLabel;
    /// <summary>Licznik/etykieta agregująca niespełnione warunki startu.</summary>
    [Export] private Label lobbyStatusCounter;

    /// <summary>Dialog potwierdzenia opuszczenia lobby.</summary>
    private LobbyLeaveConfirmation leaveConfirmation;
    /// <summary>Handler ESC do cofania się ze sceny.</summary>
    private EscapeBackHandler escapeBackHandler;
    /// <summary>Wspólny tooltip dla podpowiedzi w UI.</summary>
    private CustomTooltip customTooltip;
    /// <summary>System popupów do potwierdzeń i komunikatów błędów.</summary>
    private PopupSystem popupSystem;
    /// <summary>Detektor wklejania obsługujący pole klucza API.</summary>
    private PasteDetector apiKeyPasteDetector;
    /// <summary>Tekst tooltipa z warunkami gotowości.</summary>
    private string lobbyReadyTooltip = "";
    /// <summary>Ostatni komunikat błędu klucza API.</summary>
    private string apiKeyErrorMessage = "";
    /// <summary>Podpowiedź dla pola klucza API.</summary>
    private string apiKeyInputTooltip = "Wprowadź klucz API od DeepSeek i zatwierdź enterem";
    /// <summary>Tooltip informacyjny przy przycisku pomocy API.</summary>
    private string apiKeyInfoTooltip = "Jak uzyskać klucz API?\n\n1. Przejdź na stronę: platform.deepseek.com\n2. Zaloguj się lub załóż konto\n3. Przejdź do sekcji API Keys\n4. Wygeneruj nowy klucz\n5. Skopiuj i wklej tutaj";

    /// <summary>Bieżący kod lobby ustawiony przez hosta.</summary>
    private string currentLobbyCode = "";
    /// <summary>Długość kodu lobby (bez liter O i I).</summary>
    private const int LobbyCodeLength = 6;
    /// <summary>Maksymalna liczba graczy w lobby.</summary>
    private const int LobbyMaxPlayers = 10;
    /// <summary>Maksymalna liczba ponowień tworzenia lobby przy braku logowania.</summary>
    private const int MaxRetryAttempts = 10;
    /// <summary>Opóźnienie (s) między próbami utworzenia lobby.</summary>
    private const float RetryDelay = 0.5f;
    /// <summary>Maksymalna liczba graczy w jednej drużynie.</summary>
    private const int MaxPlayersPerTeam = 5;
    /// <summary>Czas blokady przycisków/cooldownu w sekundach.</summary>
    private const float CooldownTime = 5.0f;
    /// <summary>Flaga blokująca zmianę drużyny podczas cooldownu.</summary>
    private bool isTeamChangeCooldownActive = false;
    /// <summary>Cooldowny per gracz dla przenoszenia między drużynami.</summary>
    private Dictionary<string, bool> playerMoveCooldowns = new Dictionary<string, bool>();

    /// <summary>
    /// Stan gotowości lobby agregujący ustawienia hosta i warunki startu gry.
    /// </summary>
    private static class LobbyStatus
    {
        /// <summary>Czy wybrano typ AI.</summary>
        public static bool aiTypeSet { get; set; } = false;
        /// <summary>Czy wybrano tryb gry.</summary>
        public static bool gameModeSet { get; set; } = false;
        /// <summary>Czy którakolwiek drużyna przekracza limit.</summary>
        public static bool isAnyTeamFull { get; set; } = false;
        /// <summary>Czy wymagana liczba drużyn zawiera graczy.</summary>
        public static bool isTeamNotEmpty { get; set; } = false;
        /// <summary>Czy lista Neutral jest pusta.</summary>
        public static bool isNeutralTeamEmpty { get; set; } = true;
        /// <summary>Czy klucz API został poprawnie ustawiony/zweryfikowany.</summary>
        public static bool isAPIKeySet { get; set; } = false;
    /// <summary>Czy wszyscy gracze wrócili do widoku lobby.</summary>
        public static bool allPlayersInLobbyView { get; set; } = true;

        /// <summary>
        /// Sprawdza, czy lobby spełnia wszystkie warunki startu gry.
        /// </summary>
        /// <remarks>
        /// Uwzględnia wybór trybu, typu AI, status klucza API oraz warunki drużyn i widoku lobby.
        /// </remarks>
        /// <returns>True, gdy wszystkie wymagane warunki są spełnione.</returns>
        /// <seealso cref="UpdateHostReadyStatus"/>
        public static bool IsReadyToStart()
        {
            return aiTypeSet && gameModeSet && isAPIKeySet && isTeamNotEmpty && !isAnyTeamFull && isNeutralTeamEmpty && allPlayersInLobbyView;
        }

    }

    /// <summary>
    /// Inicjalizuje referencje, podpina sygnały UI oraz EOSManager, wczytuje tooltips
    /// i wykonuje początkowe odświeżenie stanu lobby (drużyny, tryb gry, AI, klucz API).
    /// </summary>
    /// <remarks>
    /// Ustawia początkowe połączenia sygnałów tylko dla dostępnych węzłów oraz inicjuje detekcję VRAM w tle.
    /// </remarks>
    /// <exception>Loguje błąd, gdy autoload <see cref="EOSManager"/> jest niedostępny lub scena nie ma aktywnego lobby.</exception>
    /// <seealso cref="UpdateUIVisibility"/>
    /// <seealso cref="RefreshLobbyMembers"/>
    /// <seealso cref="UpdateHostReadyStatus"/>
    /// <seealso cref="LoadCustomTooltip"/>
    /// <seealso cref="LoadPopupSystem"/>
    public override void _Ready()
    {
        base._Ready();

        alreadySwitchedToGame = false;

        // Pobierz EOSManager z autoload
        eosManager = GetNode<EOSManager>("/root/EOSManager");

        // Oznacz gracza jako obecnego w widoku lobby
        if (eosManager != null)
        {
            eosManager.SetPlayerInLobbyView(true);

            if (eosManager.isLobbyOwner)
            {
                eosManager.ResetGameSession();
                eosManager.UnlockLobby();
            }
        }

        // Inicjalizuj LobbyLeaveConfirmation
        leaveConfirmation = GetNode<LobbyLeaveConfirmation>("LobbyLeaveConfirmation");
        escapeBackHandler = GetNode<EscapeBackHandler>("EscapeBackHandler");
        escapeBackHandler.LeaveConfirmation = leaveConfirmation;
        // Sprawdź VRAM i uzupełnij w tle
        if (HardwareResources.VRAMDetectionStatus == VRAMStatus.NotDetected)
        {
            HardwareResources.StartVRAMDetection();
        }

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
            apiKeyHelpButton.MouseFilter = MouseFilterEnum.Stop;
            apiKeyHelpButton.MouseEntered += OnAPIKeyInfoTooltipMouseEntered;
            apiKeyHelpButton.MouseExited += OnAPIKeyInfoTooltipMouseExited;
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
            blueTeamList.FixedIconSize = new Vector2I(21, 21);
        }
        if (redTeamList != null)
        {
            redTeamList.GuiInput += (inputEvent) => OnTeamListGuiInput(inputEvent, redTeamList);
            redTeamList.FixedIconSize = new Vector2I(21, 21);
        }
        if (neutralTeamList != null)
        {
            neutralTeamList.GuiInput += (inputEvent) => OnTeamListGuiInput(inputEvent, neutralTeamList);
            neutralTeamList.FixedIconSize = new Vector2I(21, 21);
        }
        if (universalTeamList != null)
        {
            universalTeamList.GuiInput += (inputEvent) => OnTeamListGuiInput(inputEvent, universalTeamList);
            universalTeamList.FixedIconSize = new Vector2I(21, 21);
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
            aiAPIKeyInput.MouseFilter = MouseFilterEnum.Stop;
            aiAPIKeyInput.MouseEntered += OnAPIKeyInputTooltipMouseEntered;
            aiAPIKeyInput.MouseExited += OnAPIKeyInputTooltipMouseExited;

            // Konfiguruj PasteDetector dla automatycznego zatwierdzania po wklejeniu
            apiKeyPasteDetector = GetNodeOrNull<PasteDetector>("PasteDetector");
            if (apiKeyPasteDetector != null)
            {
                apiKeyPasteDetector.Target = aiAPIKeyInput;
                apiKeyPasteDetector.RegisterPasteCallback(OnAPIKeySubmitted);
            }
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

            // Sprawdź obecną wartość CustomLobbyId
            if (!string.IsNullOrEmpty(eosManager.currentCustomLobbyId))
            {
                GD.Print($"[Lobby:Attributes] Current CustomLobbyId in EOSManager: '{eosManager.currentCustomLobbyId}'");
                OnCustomLobbyIdUpdated(eosManager.currentCustomLobbyId);
            }

            // Sprawdź obecną wartość GameMode
            OnGameModeUpdated(EOSManager.GetEnumDescription(eosManager.currentGameMode));

            // Sprawdź obecną wartość AIType
            OnAITypeUpdated(EOSManager.GetEnumDescription(eosManager.currentAIType));
        }
        else
        {
            GD.PrintErr("[Lobby] EOSManager is null, cannot connect to signals");
        }

        // Sprawdź czy jesteśmy w lobby (powinniśmy być, bo MainMenu/Join już je utworzyło/dołączyło)
        if (eosManager != null && !string.IsNullOrEmpty(eosManager.currentLobbyId))
        {
            GD.Print($"[Lobby] Already in lobby: {eosManager.currentLobbyId}");

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
            GD.PrintErr("[Lobby] Entered lobby scene but not in any lobby");
        }

        // Domyślnie odblokuj przyciski dołączania zanim spłyną dane z EOS
        UpdateTeamButtonsState(EOSManager.Team.None);

        // Załaduj custom tooltip ze sceny
        LoadCustomTooltip();

        // Załaduj custom popup ze sceny
        LoadPopupSystem();
    }

    /// <summary>
    /// Ładuje custom tooltip ze sceny
    /// </summary>
    /// <remarks>Tworzy instancję tooltipa i dodaje ją do drzewa sceny.</remarks>
    /// <seealso cref="CustomTooltip"/>
    private void LoadCustomTooltip()
    {
        var tooltipScene = GD.Load<PackedScene>("res://scenes/components/tooltip.tscn");
        if (tooltipScene != null)
        {
            customTooltip = tooltipScene.Instantiate<CustomTooltip>();
            AddChild(customTooltip);
        }
    }

    /// <summary>
    /// Ładuje custom popup system ze sceny
    /// </summary>
    /// <remarks>Inicjalizuje system popupów, jeśli scena jest dostępna.</remarks>
    /// <seealso cref="PopupSystem"/>
    private void LoadPopupSystem()
    {
        var popupScene = GD.Load<PackedScene>("res://scenes/popup/PopupSystem.tscn");
        if (popupScene != null)
        {
            popupSystem = popupScene.Instantiate<PopupSystem>();
            AddChild(popupSystem);
        }
    }

    /// <summary>
    /// Utrzymuje cykl per-frame sceny lobby.
    /// </summary>
    /// <remarks>Tooltip zarządza pozycją we własnym <c>_Process</c>, więc tutaj pozostaje logika bazowa.</remarks>
    public override void _Process(double delta)
    {
        base._Process(delta);
    }

    // Chroni przed wielokrotną zmianą sceny, gdy przyjdzie kilka eventów/odświeżeń lobby
    private bool alreadySwitchedToGame = false;

    /// <summary>
    /// Reaguje na sygnał <see cref="EOSManager.GameSessionStartRequested"/> i przełącza wszystkich graczy do sceny gry.
    /// </summary>
    /// <remarks>Zapobiega wielokrotnemu przełączeniu sceny oraz oznacza gracza jako nieobecnego w lobby.</remarks>
    /// <param name="sessionId">Identyfikator rozpoczynanej sesji gry.</param>
    /// <param name="hostUserId">Identyfikator hosta sesji.</param>
    /// <param name="seed">Ziarno synchronizujące rozgrywkę.</param>
    private void OnGameSessionStartRequested(string sessionId, string hostUserId, ulong seed)
    {
        if (alreadySwitchedToGame) return;

        alreadySwitchedToGame = true;

        GD.Print($"[Lobby] Starting game session: {sessionId}");

        // Oznacz gracza jako nieobecnego w widoku lobby (przechodzi do gry)
        if (eosManager != null)
        {
            eosManager.SetPlayerInLobbyView(false);
        }

        // Zmiana sceny uruchamiana synchronicznie dla hosta i klientów na podstawie atrybutów lobby
        GetTree().ChangeSceneToFile("res://scenes/game/main_game.tscn");
    }

    /// <summary>
    /// Żąda z <see cref="EOSManager.GetLobbyMembers"/> aktualizacji listy członków lobby, aby odświeżyć UI.
    /// </summary>
    /// <seealso cref="OnLobbyMembersUpdated"/>
    private void RefreshLobbyMembers()
    {
        if (eosManager != null)
        {
            eosManager.GetLobbyMembers();
        }
    }

    /// <summary>
    /// Generuje sześciocyfrowy kod lobby bez liter O i I, aby uniknąć pomyłek.
    /// </summary>
    /// <remarks>Wykorzystuje losowe znaki alfanumeryczne i zwraca kod gotowy do publikacji w lobby.</remarks>
    /// <returns>Nowo wygenerowany kod lobby.</returns>
    /// <seealso cref="OnGenerateNewIdButtonPressed"/>
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
    /// Aktualizuje listy drużyn i liczników na podstawie danych z <see cref="EOSManager"/>.
    /// Rozdziela graczy według atrybutu <c>team</c>, oznacza hosta i lokalnego gracza,
    /// a następnie synchronizuje widoczność UI oraz warunki startu gry.
    /// </summary>
    /// <remarks>Metoda czyści listy i odbudowuje je od zera na podstawie danych z lobby.</remarks>
    /// <exception>Loguje błąd, gdy listy drużyn nie są zainicjalizowane.</exception>
    /// <param name="members">Lista członków lobby wraz z atrybutami (displayName, team, isOwner, isLocalPlayer) z <see cref="EOSManager"/>.</param>
    /// <seealso cref="UpdateUIVisibility"/>
    /// <seealso cref="UpdateTeamButtonsState"/>
    /// <seealso cref="OnCheckTeamsBalanceConditions"/>
    private void OnLobbyMembersUpdated(Godot.Collections.Array<Godot.Collections.Dictionary> members)
    {
        if (blueTeamList == null || redTeamList == null || neutralTeamList == null || universalTeamList == null)
        {
            GD.PrintErr("[Lobby:Teams] Team lists not initialized");
            return;
        }

        GD.Print($"[Lobby:Teams] Updating team lists with {members.Count} members");

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

            int profileIcon = 0;
            if (member.ContainsKey("profileIcon"))
            {
                try
                {
                    profileIcon = member["profileIcon"].As<int>();
                }
                catch
                {
                    int.TryParse(member["profileIcon"].ToString(), out profileIcon);
                }
            }

            string userId = member.ContainsKey("userId") ? member["userId"].ToString() : "";

            // Sprawdź czy gracz jest w widoku lobby
            bool inLobbyView = true; // Domyślnie true
            if (member.ContainsKey("inLobbyView"))
            {
                string inLobbyViewStr = member["inLobbyView"].ToString().ToLower();
                inLobbyView = inLobbyViewStr == "true";
            }

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

            // Dodaj oznaczenie dla gracza który nie wrócił do lobby
            if (!inLobbyView && !isLocalPlayer)
            {
                displayName += " (w grze)";
            }

            // Przypisz do odpowiedniej drużyny według atrybutu
            if (team == EOSManager.Team.Blue)
            {
                int index = blueTeamList.AddItem(displayName);
                blueTeamList.SetItemMetadata(index, new Godot.Collections.Dictionary
                {
                    { "userId", userId },
                    { "isLocalPlayer", isLocalPlayer },
                    { "team", team.ToString() },
                    { "profileIcon", profileIcon }
                });

                // Ustaw ikonę jeśli istnieje
                if (profileIcon > 0 && eosManager != null)
                {
                    string iconPath = eosManager.GetProfileIconPath(team, profileIcon);
                    if (!string.IsNullOrEmpty(iconPath) && ResourceLoader.Exists(iconPath))
                    {
                        blueTeamList.SetItemIcon(index, ResourceLoader.Load<Texture2D>(iconPath));
                    }
                }

                GD.Print($"[Lobby:Teams] Added Blue member: {displayName} (Icon: {profileIcon})");
            }
            else if (team == EOSManager.Team.Red)
            {
                int index = redTeamList.AddItem(displayName);
                redTeamList.SetItemMetadata(index, new Godot.Collections.Dictionary
                {
                    { "userId", userId },
                    { "isLocalPlayer", isLocalPlayer },
                    { "team", team.ToString() },
                    { "profileIcon", profileIcon }
                });

                // Ustaw ikonę jeśli istnieje
                if (profileIcon > 0 && eosManager != null)
                {
                    string iconPath = eosManager.GetProfileIconPath(team, profileIcon);
                    if (!string.IsNullOrEmpty(iconPath) && ResourceLoader.Exists(iconPath))
                    {
                        redTeamList.SetItemIcon(index, ResourceLoader.Load<Texture2D>(iconPath));
                    }
                }

                GD.Print($"[Lobby:Teams] Added Red member: {displayName} (Icon: {profileIcon})");
            }
            else if (team == EOSManager.Team.Universal)
            {
                int index = universalTeamList.AddItem(displayName);
                universalTeamList.SetItemMetadata(index, new Godot.Collections.Dictionary
                {
                    { "userId", userId },
                    { "isLocalPlayer", isLocalPlayer },
                    { "team", team.ToString() },
                    { "profileIcon", profileIcon }
                });

                // Ustaw niebieską ikonę dla Universal team
                if (profileIcon > 0 && eosManager != null)
                {
                    string iconPath = eosManager.GetProfileIconPath(team, profileIcon);
                    if (!string.IsNullOrEmpty(iconPath) && ResourceLoader.Exists(iconPath))
                    {
                        universalTeamList.SetItemIcon(index, ResourceLoader.Load<Texture2D>(iconPath));
                    }
                }

                GD.Print($"[Lobby:Teams] Added Universal member: {displayName} (Icon: {profileIcon})");
            }
            else // team == EOSManager.Team.None (NeutralTeam)
            {
                int index = neutralTeamList.AddItem(displayName);
                neutralTeamList.SetItemMetadata(index, new Godot.Collections.Dictionary
                {
                    { "userId", userId },
                    { "isLocalPlayer", isLocalPlayer },
                    { "team", team.ToString() },
                    { "profileIcon", profileIcon }
                });
            }
        }

        GD.Print($"[Lobby:Teams] Updated - Blue:{blueTeamList.ItemCount} Red:{redTeamList.ItemCount} Neutral:{neutralTeamList.ItemCount} Universal:{universalTeamList.ItemCount}");

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
    /// Ustawia widoczność i dostępność elementów UI w zależności od roli hosta,
    /// wybranego trybu gry oraz typu AI (np. pole klucza API tylko dla hosta w AI API) na podstawie stanu w <see cref="EOSManager"/>.
    /// </summary>
    /// <remarks>Dodatkowo blokuje tryb AI vs Human, gdy przekroczono limit graczy.</remarks>
    /// <seealso cref="OnGameModeUpdated"/>
    /// <seealso cref="OnAITypeUpdated"/>
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

        GD.Print($"[Lobby:UI] Visibility updated - isHost:{isHost}");
    }

    /// <summary>
    /// Aktualizuje wyświetlany kod lobby po zmianie w <see cref="EOSManager.CustomLobbyIdUpdated"/>.
    /// </summary>
    /// <remarks>Obsługuje wartości puste i loguje nieprawidłowe identyfikatory.</remarks>
    /// <exception>Loguje błąd, gdy pole lobbyIdInput jest puste lub gdy otrzymano nieprawidłowy identyfikator.</exception>
    /// <param name="customLobbyId">Nowa wartość CustomLobbyId z serwisu EOS.</param>
    /// <seealso cref="UpdateLobbyIdDisplay"/>
    private void OnCustomLobbyIdUpdated(string customLobbyId)
    {
        GD.Print($"[Lobby:LobbyID] CustomLobbyId updated: '{customLobbyId}'");

        // Jeśli CustomLobbyId jest pusty, wyczyść pole
        if (string.IsNullOrEmpty(customLobbyId))
        {
            currentLobbyCode = "";
            if (lobbyIdInput != null)
            {
                CallDeferred(nameof(UpdateLobbyIdDisplay), "");
            }
            GD.Print("[Lobby:LobbyID] Cleared CustomLobbyId field");
            return;
        }

        if (customLobbyId != "Unknown")
        {
            GD.Print($"[Lobby:LobbyID] Setting CustomLobbyId to: '{customLobbyId}'");
            currentLobbyCode = customLobbyId;

            if (lobbyIdInput != null)
            {
                // Użyj CallDeferred aby upewnić się, że UI jest gotowe
                CallDeferred(nameof(UpdateLobbyIdDisplay), currentLobbyCode);
            }
            else
            {
                GD.PrintErr("[Lobby:LobbyID] lobbyIdInput is NULL");
            }
        }
        else
        {
            GD.PrintErr($"[Lobby:LobbyID] Received invalid CustomLobbyId: '{customLobbyId}'");
        }
    }

    /// <summary>
    /// Synchronizuje UI i skład drużyn po zmianie trybu gry w <see cref="EOSManager"/>.
    /// </summary>
    /// <remarks>Host aktualizuje skład drużyn, a gracze odświeżają tylko widok.</remarks>
    /// <param name="gameMode">Nazwa trybu gry (opis enumu) otrzymana z <see cref="EOSManager.GameModeUpdated"/>.</param>
    /// <seealso cref="UpdateUIVisibility"/>
    /// <seealso cref="EOSManager.MoveAllPlayersToUniversal"/>
    /// <seealso cref="EOSManager.RestorePlayersFromUniversal"/>
    /// <seealso cref="UpdateHostReadyStatus"/>
    private void OnGameModeUpdated(string gameMode)
    {
        GD.Print($"[Lobby:GameMode] GameMode updated: '{gameMode}'");

        // Parsuj string na enum
        EOSManager.GameMode gameModeEnum = EOSManager.ParseEnumFromDescription<EOSManager.GameMode>(gameMode, EOSManager.GameMode.AIMaster);

        // Zaktualizuj dropdown (dla hosta)
        if (gameModeList != null)
        {
            // Znajdź indeks odpowiadający trybowi gry
            for (int i = 0; i < gameModeList.ItemCount; i++)
            {
                if (gameModeList.GetItemText(i) == gameMode)
                {
                    gameModeList.Selected = i;
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
                GD.Print("[Lobby:GameMode] Moving all players to Universal team...");
                eosManager.MoveAllPlayersToUniversal();
            }
            else if (gameModeEnum == EOSManager.GameMode.AIMaster)
            {
                GD.Print("[Lobby:GameMode] Restoring players from Universal team...");
                eosManager.RestorePlayersFromUniversal();
            }
        }

        // Zaktualizuj label (dla graczy)
        if (gameModeSelectedLabel != null)
        {
            gameModeSelectedLabel.Text = gameMode;
        }

        LobbyStatus.gameModeSet = true;
        if (eosManager != null && eosManager.isLobbyOwner)
        {
            UpdateHostReadyStatus();
        }
    }

    /// <summary>
    /// Aktualizuje wybór typu AI, widoczność pola klucza API oraz status gotowości po zmianie w <see cref="EOSManager"/>.
    /// </summary>
    /// <remarks>Resetuje flagę klucza API i w razie potrzeby uruchamia walidację.</remarks>
    /// <param name="aiType">Nazwa typu AI (opis enumu) otrzymana z <see cref="EOSManager.AITypeUpdated"/>.</param>
    /// <seealso cref="SetAPIKeyInputBorder"/>
    /// <seealso cref="UpdateHostReadyStatus"/>
    private void OnAITypeUpdated(string aiType)
    {
        GD.Print($"[Lobby:AIType] AIType updated: '{aiType}'");

        LobbyStatus.isAPIKeySet = false;
        SetAPIKeyInputBorder(new Color(0.5f, 0.5f, 0.5f)); // Szary

        // Parsuj string na enum
        EOSManager.AIType aiTypeEnum = EOSManager.ParseEnumFromDescription<EOSManager.AIType>(aiType, EOSManager.AIType.API);

        // Zaktualizuj dropdown (dla hosta)
        if (aiTypeList != null)
        {
            // Znajdź indeks odpowiadający trybowi gry
            for (int i = 0; i < aiTypeList.ItemCount; i++)
            {
                if (aiTypeList.GetItemText(i) == aiType)
                {
                    aiTypeList.Selected = i;
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
        }

        //Sprawdź czy API key jest potrzebny i czy jest wypełniony
        if (aiTypeEnum == EOSManager.AIType.API)
        {
            string apiKey = aiAPIKeyInput.Text;
            if (apiKey != "")
            {
                OnAPIKeySubmitted(apiKey);
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
            GD.Print($"[Lobby:AIType] API key not required for {aiTypeEnum}");
        }

        LobbyStatus.aiTypeSet = true;
        if (eosManager != null && eosManager.isLobbyOwner)
        {
            UpdateHostReadyStatus();
        }
    }

    /// <summary>
    /// Aktualizuje etykietę stanu gotowości lobby po zmianie ogłoszonej przez hosta.
    /// </summary>
    /// <remarks>Aktualizacja dotyczy zarówno hosta, jak i pozostałych graczy.</remarks>
    /// <param name="isReady">Czy lobby spełnia warunki startu gry.</param>
    /// <seealso cref="UpdateLobbyStatusDisplay"/>
    private void OnLobbyReadyStatusUpdated(bool isReady)
    {
        GD.Print($"[Lobby:ReadyStatus] Lobby ready status updated: {isReady}");
        UpdateLobbyStatusDisplay(isReady);
    }

    /// <summary>
    /// Wylicza warunek gotowości i publikuje go do serwera lobby jako host przez <see cref="EOSManager.SetLobbyReadyStatus(bool)"/>.
    /// </summary>
    /// <remarks>Metoda działa tylko dla hosta i ignoruje wywołanie w innych rolach.</remarks>
    /// <seealso cref="LobbyStatus.IsReadyToStart"/>
    /// <seealso cref="UpdateHostReadyStatusIfOwner"/>
    private void UpdateHostReadyStatus()
    {
        if (eosManager == null || !eosManager.isLobbyOwner)
            return;

        bool isReady = LobbyStatus.IsReadyToStart();
        eosManager.SetLobbyReadyStatus(isReady);
        GD.Print($"[Lobby:ReadyStatus] Broadcasting ready status: {isReady}");
    }

    /// <summary>
    /// Waliduje skład drużyn względem trybu gry i aktualizuje flagi gotowości (pełne drużyny, puste neutralne, obecność graczy), następnie informuje hosta poprzez <see cref="UpdateHostReadyStatus"/>.
    /// </summary>
    /// <remarks>Uwzględnia tryb AI vs Human oraz stan powrotu graczy do lobby.</remarks>
    /// <seealso cref="UpdateHostReadyStatus"/>
    private void OnCheckTeamsBalanceConditions()
    {
        GD.Print("[Lobby:TeamsBalance] CheckTeamsBalanceConditions triggered");

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
            LobbyStatus.isTeamNotEmpty = universalCount > 0;
        }
        else
        {
            LobbyStatus.isTeamNotEmpty = blueCount > 0 && redCount > 0;
        }

        // W trybie AI vs Human nie sprawdzamy MaxPlayersPerTeam dla Blue/Red (są ukryte)
        if (isAIvsHuman)
        {
            LobbyStatus.isAnyTeamFull = false;
        }
        else
        {
            LobbyStatus.isAnyTeamFull = blueCount > MaxPlayersPerTeam || redCount > MaxPlayersPerTeam;
        }

        // W trybie AI vs Human neutralCount powinien być zawsze 0 (wszyscy w Universal)
        // W trybie AI Master neutralCount też powinien być 0 (wszyscy w Blue/Red)
        LobbyStatus.isNeutralTeamEmpty = neutralCount == 0;

        // Sprawdź czy wszyscy gracze są w widoku lobby (nie w grze)
        if (eosManager != null)
        {
            LobbyStatus.allPlayersInLobbyView = eosManager.AreAllPlayersInLobbyView();
            if (!LobbyStatus.allPlayersInLobbyView)
            {
                GD.Print("[Lobby:Ready] Not all players are in lobby view yet.");
            }
        }

        if (eosManager != null && eosManager.isLobbyOwner)
        {
            UpdateHostReadyStatus();
        }
    }

    /// <summary>
    /// Włącza normalny styl przycisku "Rozpocznij grę" bazując na stylu przycisku opuszczania lobby.
    /// </summary>
    /// <remarks>Przywraca aktywne kolory, style i kursor interakcji.</remarks>
    /// <seealso cref="UpdateLobbyStatusDisplay"/>
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
    /// <remarks>Ustawia szary wygląd i blokuje aktywne style dla stanu niegotowego.</remarks>
    /// <seealso cref="UpdateLobbyStatusDisplay"/>
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
    /// Aktualizuje etykiety statusu lobby (tekst, kolory, tooltip) w zależności od gotowości i roli hosta.
    /// </summary>
    /// <remarks>Host widzi szczegółowe warunki, gracze otrzymują skrócony status.</remarks>
    /// <param name="isReady">Czy lobby spełnia wszystkie warunki startu.</param>
    /// <seealso cref="EnableStartGameButtonStyle"/>
    /// <seealso cref="DisableStartGameButtonStyle"/>
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
                {
                    // Użyj niestandardowego komunikatu błędu API jeśli jest dostępny
                    if (!string.IsNullOrEmpty(apiKeyErrorMessage))
                    {
                        unmetConditions.Add(apiKeyErrorMessage);
                    }
                    else
                    {
                        unmetConditions.Add("Klucz API nie jest poprawny");
                    }
                }

                if (!LobbyStatus.allPlayersInLobbyView)
                    unmetConditions.Add("Nie wszyscy gracze wrócili do lobby");

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
        }
    }

    /// <summary>
    /// Ustawia pole tekstowe z kodem lobby i weryfikuje, że UI odzwierciedla oczekiwaną wartość.
    /// </summary>
    /// <remarks>Używane przy inicjalizacji oraz zmianie CustomLobbyId.</remarks>
    /// <exception>Loguje błąd, gdy pole tekstowe nie odzwierciedla przekazanego kodu.</exception>
    /// <param name="lobbyId">Kod lobby do wyświetlenia.</param>
    /// <seealso cref="OnCustomLobbyIdUpdated"/>
    private void UpdateLobbyIdDisplay(string lobbyId)
    {
        if (lobbyIdInput != null)
        {
            lobbyIdInput.Text = lobbyId;
            GD.Print($"[Lobby:LobbyID] Updated Lobby ID input to: '{lobbyIdInput.Text}'");

            // Sprawdź czy wartość rzeczywiście się zmieniła
            if (lobbyIdInput.Text != lobbyId)
            {
                GD.PrintErr($"[Lobby:LobbyID] Failed to update - Expected: '{lobbyId}', Got: '{lobbyIdInput.Text}'");
            }
        }
    }

    /// <summary>
    /// Sprawdza wstępnie format klucza API (długość, dozwolone znaki) i ustawia kolory ramki oraz status gotowości.
    /// </summary>
    /// <remarks>Walidacja lokalna nie sprawdza poprawności po stronie serwera.</remarks>
    /// <param name="apiKey">Klucz API wpisany przez użytkownika.</param>
    /// <returns>True, jeśli format klucza spełnia minimalne kryteria.</returns>
    /// <seealso cref="SetAPIKeyInputBorder"/>
    /// <seealso cref="UpdateHostReadyStatusIfOwner"/>
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
            GD.Print($"[Lobby:APIKey] API Key is too short: {apiKey.Length} characters (minimum {MinKeyLength})");
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
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Wysyła minimalne zapytanie do DeepSeek, aby zweryfikować klucz API, aktualizując status i atrybuty lobby przez <see cref="EOSManager.SetAPIKey(string)"/>.
    /// </summary>
    /// <remarks>Używa krótkiego zapytania testowego i mapuje błędy na komunikaty statusu lobby.</remarks>
    /// <param name="apiKey">Klucz API do walidacji online.</param>
    /// <seealso cref="SetAPIKeyInputBorder"/>
    /// <seealso cref="UpdateHostReadyStatusIfOwner"/>
    /// <seealso cref="UpdateLobbyStatusMessage"/>
    /// <exception cref="InvalidApiKeyException">Gdy DeepSeek odrzuci klucz API jako nieprawidłowy (zdefiniowane w <c>libs/AiLibs/LLM/LLMExceptions.cs</c>).</exception>
    /// <exception cref="NoTokensException">Gdy brak dostępnych tokenów API (zdefiniowane w <c>libs/AiLibs/LLM/LLMExceptions.cs</c>).</exception>
    /// <exception cref="RateLimitException">Gdy przekroczono limit zapytań do API (zdefiniowane w <c>libs/AiLibs/LLM/LLMExceptions.cs</c>).</exception>
    /// <exception cref="NoInternetException">Gdy brak połączenia z internetem podczas walidacji (zdefiniowane w <c>libs/AiLibs/LLM/LLMExceptions.cs</c>).</exception>
    /// <exception cref="ApiException">Gdy DeepSeek zwróci inny błąd API (zdefiniowane w <c>libs/AiLibs/LLM/LLMExceptions.cs</c>).</exception>
    /// <exception cref="System.Exception">Gdy wystąpi nieoczekiwany błąd podczas walidacji klucza API.</exception>
    private async void ProceedAPIKey(string apiKey)
    {
        try
        {
            GD.Print($"[Lobby:APIKey] Proceeding API Key.");
            DeepSeekLLM apiLLM = new DeepSeekLLM(apiKey);

            // Dane testowe - minimalny request
            string systemPrompt = "test";
            string userPrompt = "test";
            uint maxTokens = 1;

            string response = await apiLLM.SendRequestAsync(systemPrompt, userPrompt, maxTokens);

            GD.Print("[Lobby:APIKey] Validation successful");
            SetAPIKeyInputBorder(new Color(0, 1, 0)); // Zielony
            LobbyStatus.isAPIKeySet = true;
            apiKeyErrorMessage = ""; // Wyczyść komunikat błędu

            // Zapisz zwalidowany klucz API w atrybutach lobby
            if (eosManager != null)
            {
                eosManager.SetAPIKey(apiKey);
            }

            UpdateHostReadyStatusIfOwner();
        }
        catch (InvalidApiKeyException)
        {
            SetAPIKeyInputBorder(new Color(1, 0, 0)); // Czerwony
            LobbyStatus.isAPIKeySet = false;
            UpdateLobbyStatusMessage("Nieprawidłowy klucz API");
            UpdateHostReadyStatusIfOwner();
        }
        catch (NoTokensException)
        {
            SetAPIKeyInputBorder(new Color(1, 0, 0)); // Czerwony
            LobbyStatus.isAPIKeySet = false;
            UpdateLobbyStatusMessage("Brak tokenów AI");
            UpdateHostReadyStatusIfOwner();
        }
        catch (RateLimitException)
        {
            SetAPIKeyInputBorder(new Color(1, 0, 0)); // Czerwony
            LobbyStatus.isAPIKeySet = false;
            UpdateLobbyStatusMessage("Limit zapytań AI przekroczony");
            UpdateHostReadyStatusIfOwner();
        }
        catch (NoInternetException)
        {
            SetAPIKeyInputBorder(new Color(1, 0, 0)); // Czerwony
            LobbyStatus.isAPIKeySet = false;
            UpdateLobbyStatusMessage("Brak połączenia z internetem");
            UpdateHostReadyStatusIfOwner();
        }
        catch (ApiException)
        {
            SetAPIKeyInputBorder(new Color(1, 0, 0)); // Czerwony
            LobbyStatus.isAPIKeySet = false;
            UpdateLobbyStatusMessage("Błąd API");
            UpdateHostReadyStatusIfOwner();
        }
        catch (Exception)
        {
            SetAPIKeyInputBorder(new Color(1, 0, 0)); // Czerwony
            LobbyStatus.isAPIKeySet = false;
            UpdateLobbyStatusMessage("Błąd walidacji klucza API");
            UpdateHostReadyStatusIfOwner();
        }

    }

    /// <summary>
    /// Przechowuje i loguje komunikat o błędzie API widoczny w statusie lobby (tylko dla hosta); wpływa na komunikat pokazywany w <see cref="UpdateLobbyStatusDisplay(bool)"/>.
    /// </summary>
    /// <remarks>Wywoływane tylko przez hosta; gracze nie aktualizują komunikatu.</remarks>
    /// <param name="message">Treść komunikatu do wyświetlenia.</param>
    /// <seealso cref="UpdateLobbyStatusDisplay"/>
    private void UpdateLobbyStatusMessage(string message)
    {
        if (eosManager != null && eosManager.isLobbyOwner)
        {
            apiKeyErrorMessage = message;
            GD.Print($"[Lobby:APIKey] Updated API error message: {message}");
        }
    }

    /// <summary>
    /// Ponownie publikuje status gotowości tylko wtedy, gdy bieżący gracz jest hostem poprzez <see cref="EOSManager.SetLobbyReadyStatus(bool)"/>.
    /// </summary>
    /// <remarks>Ignoruje wywołanie, jeśli lokalny gracz nie jest hostem.</remarks>
    /// <seealso cref="UpdateHostReadyStatus"/>
    private void UpdateHostReadyStatusIfOwner()
    {
        if (eosManager != null && eosManager.isLobbyOwner)
        {
            UpdateHostReadyStatus();
        }
    }

    /// <summary>
    /// Nadaje polu klucza API obramowanie w zadanym kolorze (normal i focus) przez nadpisanie stylu.
    /// </summary>
    /// <remarks>Jeśli pole nie istnieje, metoda przywraca domyślną ramkę.</remarks>
    /// <param name="color">Kolor obramowania.</param>
    /// <seealso cref="OnAPIKeyTextChanged"/>
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
    /// Resetuje stan walidacji po każdej zmianie znaku w polu klucza API.
    /// </summary>
    /// <remarks>Po zmianie użytkownik musi ponownie zatwierdzić klucz.</remarks>
    /// <param name="newText">Aktualna treść pola klucza API.</param>
    /// <seealso cref="SetAPIKeyInputBorder"/>
    /// <seealso cref="UpdateHostReadyStatusIfOwner"/>
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
    /// Waliduje i rozpoczyna zdalną weryfikację klucza API po wciśnięciu Enter lub wklejeniu.
    /// </summary>
    /// <remarks>Najpierw wykonuje lokalną walidację, a następnie uruchamia żądanie sieciowe.</remarks>
    /// <param name="newText">Wartość klucza API przekazana z pola.</param>
    /// <seealso cref="ValidateAPIKey"/>
    /// <seealso cref="ProceedAPIKey"/>
    private void OnAPIKeySubmitted(string newText)
    {
        bool isValid = ValidateAPIKey(newText);
        if (!isValid)
        {
            GD.Print($"[Lobby:APIKey] Invalid API Key. Aborting submission.");
            return;
        }
        ProceedAPIKey(newText);
    }

    /// <summary>
    /// Podmienia zawartość listy drużyny na podany zestaw graczy.
    /// </summary>
    /// <remarks>Używane przy odświeżaniu listy graczy w UI.</remarks>
    /// <param name="teamList">Lista GUI reprezentująca drużynę.</param>
    /// <param name="players">Nazwy graczy do wyświetlenia.</param>
    /// <seealso cref="OnLobbyMembersUpdated"/>
    public void UpdateTeamList(ItemList teamList, string[] players)
    {
        if (teamList == null) return;

        teamList.Clear();
        foreach (string player in players)
        {
            teamList.AddItem(player);
        }
    }

    /// <summary>
    /// Obsługuje zmianę trybu gry z listy: weryfikuje limity, blokuje spam i wysyła wybór do EOSManager.
    /// </summary>
    /// <remarks>W trybie AI vs Human ogranicza liczbę graczy do 5.</remarks>
    /// <exception>Loguje błąd, gdy próba zmiany trybu narusza limit graczy.</exception>
    /// <param name="index">Indeks wybranego trybu gry na liście.</param>
    /// <seealso cref="BlockButtonToHandleTooManyRequests"/>
    /// <seealso cref="UpdateHostReadyStatus"/>
    private void OnSelectedGameModeChanged(long index)
    {
        if (gameModeList == null || eosManager == null) return;

        string selectedModeStr = gameModeList.GetItemText((int)index);
        EOSManager.GameMode selectedMode = EOSManager.ParseEnumFromDescription<EOSManager.GameMode>(selectedModeStr, EOSManager.GameMode.AIMaster);

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
                GD.PrintErr($"[Lobby:GameMode] Cannot switch to AI vs Human mode: Too many players ({totalPlayers}/5)");

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

    /// <summary>
    /// Sprawdza czy lokalny sprzęt spełnia minimalne wymagania do uruchomienia lokalnego LLM.
    /// </summary>
    /// <remarks>Opiera się na progach z <see cref="HardwareResources"/>.</remarks>
    /// <returns>True, jeśli CPU/RAM/VRAM przekraczają progi minimalne.</returns>
    /// <seealso cref="HardwareResources.IfAICapable"/>
    private bool CheckHardwareCapabilities()
    {
        return HardwareResources.IfAICapable();
    }

    /// <summary>
    /// Przełącza typ AI, opcjonalnie wyświetla ostrzeżenie sprzętowe i synchronizuje wybór z serwerem lobby przez <see cref="EOSManager.SetAIType(EOSManager.AIType)"/>.
    /// </summary>
    /// <remarks>Jeśli sprzęt jest zbyt słaby, użytkownik może potwierdzić kontynuację.</remarks>
    /// <param name="index">Indeks wybranego typu AI na liście.</param>
    /// <seealso cref="CheckHardwareCapabilities"/>
    /// <seealso cref="ShowHardwareWarningDialog"/>
    /// <seealso cref="BlockButtonToHandleTooManyRequests"/>
    /// <seealso cref="UpdateHostReadyStatus"/>
    private void OnSelectedAITypeChanged(long index)
    {
        if (aiTypeList == null || eosManager == null) return;

        string selectedAITypeStr = aiTypeList.GetItemText((int)index);
        EOSManager.AIType selectedAIType = EOSManager.ParseEnumFromDescription<EOSManager.AIType>(selectedAITypeStr, EOSManager.AIType.API);

        if (selectedAIType != EOSManager.AIType.API)
        {
            //sprawdzenie wymagań sprzętowych jeśli wybrano AI lokalne
            bool hardwareOk = CheckHardwareCapabilities();
            string hardwareInfo = HardwareResources.GetHardwareInfo();
            if (!hardwareOk)
            {
                // Pokaż okno ostrzeżenia z możliwością potwierdzenia
                ShowHardwareWarningDialog(selectedAIType, hardwareInfo);

                CallDeferred(nameof(OnAITypeUpdated), EOSManager.GetEnumDescription(eosManager.currentAIType));
                return;

            }
        }
        GD.Print("[Lobby:AIType] Hardware meets AI requirements.");

        //zablokuj buttonList by uniknąć wielokrotnych zapytań
        BlockButtonToHandleTooManyRequests(aiTypeList);

        //Zmien typ AI
        eosManager.SetAIType(selectedAIType);
        LobbyStatus.aiTypeSet = true;
        UpdateHostReadyStatus();
    }

    /// <summary>
    /// Otwiera stronę z instrukcją uzyskania klucza API w domyślnej przeglądarce systemu.
    /// </summary>
    /// <remarks>Wybiera polecenie odpowiednie dla systemu Windows/macOS/Linux.</remarks>
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
    /// <summary>
    /// Wyświetla ostrzeżenie o niewystarczającym sprzęcie dla lokalnego LLM i pozwala kontynuować mimo ryzyka.
    /// </summary>
    /// <remarks>W przypadku potwierdzenia ustawia typ AI i aktualizuje status gotowości.</remarks>
    /// <param name="selectedAIType">Wybrany typ AI wymagający potwierdzenia.</param>
    /// <param name="currentHardwareInfo">Opis wykrytego sprzętu prezentowany w oknie.</param>
    /// <seealso cref="BlockButtonToHandleTooManyRequests"/>
    /// <seealso cref="UpdateHostReadyStatus"/>
    private void ShowHardwareWarningDialog(EOSManager.AIType selectedAIType, string currentHardwareInfo)
    {
        string message = "Twój komputer nie spełnia zalecanych wymagań dla lokalnego LLM.\n\n";
        message += "★ Twój sprzęt:\n";
        message += currentHardwareInfo + "\n\n";
        message += "★ Zalecane wymagania:\n";
        message += $"• CPU: {HardwareResources.GetMinCPUCores} rdzeni\n";
        message += $"• RAM: {HardwareResources.GetMinMemoryMB / 1024} GB ({HardwareResources.GetMinMemoryMB} MB) \n";
        message += $"  lub\n";
        message += $"• VRAM: {HardwareResources.GetMinVRAMMB / 1024} GB ({HardwareResources.GetMinVRAMMB} MB)\n\n";
        message += "★ Uruchomienie lokalnego LLM może spowodować:\n";
        message += "• Spowolnienie systemu\n";
        message += "• Niską jakość odpowiedzi AI\n";
        message += "• Błędy lub zawieszenia gry\n\n";
        message += "Zalecane jest użycie trybu API dla lepszej wydajności.\n\n";
        message += "Czy mimo to chcesz kontynuować z lokalnym LLM?";

        if (popupSystem != null)
        {
            popupSystem.ShowConfirmation(
                "★ OSTRZEŻENIE - NIEWYSTARCZAJĄCY SPRZĘT ★",
                message,
                "KONTYNUUJ MIMO TO",
                "NIE, POWRÓĆ",
                () =>
                {
                    GD.Print("[Lobby:AIType] User confirmed local LLM despite hardware warning");

                    // Zablokuj buttonList by uniknąć wielokrotnych zapytań
                    BlockButtonToHandleTooManyRequests(aiTypeList);

                    //Zmien typ AI
                    eosManager.SetAIType(selectedAIType);
                    LobbyStatus.aiTypeSet = true;
                    UpdateHostReadyStatus();
                },
                () =>
                {
                    GD.Print("[Lobby:AIType] User cancelled local LLM selection");
                }
            );
        }
        else
        {
            GD.PrintErr("[Lobby:AIType] PopupSystem is null, cannot show hardware warning dialog");
        }
    }

    /// <summary>
    /// Kopiuje bieżący kod lobby do schowka, jeśli istnieje.
    /// </summary>
    /// <remarks>W przypadku braku kodu zapisuje informację w logu.</remarks>
    private void OnCopyIdButtonPressed()
    {
        if (!string.IsNullOrEmpty(currentLobbyCode))
        {
            DisplayServer.ClipboardSet(currentLobbyCode);
            GD.Print($"[Lobby:LobbyID] Lobby ID copied to clipboard: {currentLobbyCode}");
        }
        else
        {
            GD.Print("[Lobby:LobbyID] No lobby ID to copy");
        }
    }

    /// <summary>
    /// Generuje nowy kod lobby, zapisuje go w <see cref="EOSManager.SetCustomLobbyId(string)"/> i aktualizuje wyświetlanie.
    /// </summary>
    /// <remarks>Blokuje przycisk na czas cooldownu, by uniknąć spamowania.</remarks>
    /// <seealso cref="GenerateLobbyIDCode"/>
    /// <seealso cref="UpdateLobbyIdDisplay"/>
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

        GD.Print($"[Lobby:LobbyID] New lobby ID generated: {newCode}");

        //zablokuj button by uniknąć wielokrotnych zapytań
        BlockButtonToHandleTooManyRequests(generateNewIdButton);
    }

    /// <summary>
    /// Rozpoczyna proces startu gry po spełnieniu warunków gotowości; dostępne tylko dla hosta.
    /// </summary>
    /// <remarks>Gdy warunki nie są spełnione, loguje powód i przerywa wywołanie.</remarks>
    /// <seealso cref="LobbyStatus.IsReadyToStart"/>
    /// <seealso cref="EOSManager.RequestStartGameSession"/>
    private void OnStartGamePressed()
    {
        // Sprawdź czy gra jest gotowa do startu
        if (!LobbyStatus.IsReadyToStart())
        {
            GD.Print("[Lobby:StartGame] Cannot start game - conditions not met");
            return;
        }

        // TYLKO HOST może rozpocząć sesję
        if (eosManager == null || !eosManager.isLobbyOwner)
        {
            GD.Print("[Lobby:StartGame] Only host can start the game");
            return;
        }

        GD.Print("[Lobby:StartGame] Host requests game session start...");
        eosManager.RequestStartGameSession();

    }

    /// <summary>
    /// Otwiera dialog potwierdzenia opuszczenia lobby po naciśnięciu przycisku cofnięcia.
    /// </summary>
    /// <remarks>Dialog jest współdzielony z akcją opuszczenia lobby.</remarks>
    /// <seealso cref="LobbyLeaveConfirmation.ShowConfirmation"/>
    private void OnBackButtonPressed()
    {
        if (leaveConfirmation != null)
        {
            leaveConfirmation.ShowConfirmation();
        }
    }

    /// <summary>
    /// Otwiera dialog potwierdzenia opuszczenia lobby po wyborze akcji "Opuść".
    /// </summary>
    /// <remarks>Przycisk widoczny jest również dla hosta.</remarks>
    /// <seealso cref="LobbyLeaveConfirmation.ShowConfirmation"/>
    private void OnLeaveLobbyPressed()
    {
        if (leaveConfirmation != null)
        {
            leaveConfirmation.ShowConfirmation();
        }
    }

    /// <summary>
    /// Próbuje utworzyć lobby, powtarzając próbę po opóźnieniu gdy EOS nie jest gotowy lub brak zalogowania; finalnie wywołuje <see cref="EOSManager.CreateLobby(string, int, bool)"/>.
    /// </summary>
    /// <remarks>Wykorzystuje opóźnienie <see cref="RetryDelay"/> i limit prób <see cref="MaxRetryAttempts"/>.</remarks>
    /// <exception>Loguje błąd, gdy przekroczono maksymalną liczbę prób logowania do EOS.</exception>
    /// <param name="attempt">Aktualny numer próby tworzenia lobby.</param>
    /// <seealso cref="GenerateLobbyIDCode"/>
    /// <seealso cref="UpdateLobbyIdDisplay"/>
    private async void CreateLobbyWithRetry(int attempt = 0)
    {
        // Sprawdź czy użytkownik jest już zalogowany
        if (eosManager == null)
        {
            await ToSignal(GetTree().CreateTimer(RetryDelay), SceneTreeTimer.SignalName.Timeout);
            CreateLobbyWithRetry(attempt + 1);
            return;
        }

        // Sprawdź czy już nie ma lobby (np. powrót z innej sceny)
        if (!string.IsNullOrEmpty(eosManager.currentLobbyId))
        {
            GD.Print($"[Lobby:CreateLobby] Already in lobby: {eosManager.currentLobbyId}");
            return;
        }

        // Sprawdź czy EOS jest zalogowany
        if (!eosManager.IsLoggedIn())
        {
            if (attempt < MaxRetryAttempts)
            {
                await ToSignal(GetTree().CreateTimer(RetryDelay), SceneTreeTimer.SignalName.Timeout);
                CreateLobbyWithRetry(attempt + 1);
            }
            else
            {
                GD.PrintErr("[Lobby:CreateLobby] EOS login timeout - could not create lobby");
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
    }

    /// <summary>
    /// Żąda dołączenia do niebieskiej drużyny przez <see cref="EOSManager.SetMyTeam(EOSManager.Team)"/>.
    /// </summary>
    /// <remarks>Deleguje logikę do <see cref="TryJoinTeam"/>.</remarks>
    /// <seealso cref="TryJoinTeam"/>
    private void OnBlueTeamJoinButtonPressed()
    {
        TryJoinTeam(EOSManager.Team.Blue);
    }

    /// <summary>
    /// Żąda dołączenia do czerwonej drużyny przez <see cref="EOSManager.SetMyTeam(EOSManager.Team)"/>.
    /// </summary>
    /// <remarks>Deleguje logikę do <see cref="TryJoinTeam"/>.</remarks>
    /// <seealso cref="TryJoinTeam"/>
    private void OnRedTeamJoinButtonPressed()
    {
        TryJoinTeam(EOSManager.Team.Red);
    }

    /// <summary>
    /// Pokazuje tooltip ze statusem gotowości gdy kursor najedzie na licznik lub przycisk startu.
    /// </summary>
    /// <remarks>Treść tooltipa pochodzi z <see cref="UpdateLobbyStatusDisplay"/>.</remarks>
    /// <seealso cref="UpdateLobbyStatusDisplay"/>
    private void OnReadyTooltipMouseEntered()
    {
        if (customTooltip != null && !string.IsNullOrEmpty(lobbyReadyTooltip))
        {
            customTooltip.Show(lobbyReadyTooltip);
        }
    }

    /// <summary>
    /// Ukrywa tooltip gotowości po opuszczeniu kursora z elementu.
    /// </summary>
    /// <remarks>Nie modyfikuje stanu statusu lobby.</remarks>
    /// <seealso cref="OnReadyTooltipMouseEntered"/>
    private void OnReadyTooltipMouseExited()
    {
        if (customTooltip != null)
        {
            customTooltip.Hide();
        }
    }

    /// <summary>
    /// Pokazuje tooltip z instrukcją dla pola klucza API.
    /// </summary>
    /// <remarks>Wyświetla tekst tylko, gdy tooltip i treść są dostępne.</remarks>
    /// <seealso cref="CustomTooltip.Show"/>
    private void OnAPIKeyInputTooltipMouseEntered()
    {
        if (customTooltip != null && !string.IsNullOrEmpty(apiKeyInputTooltip))
        {
            customTooltip.Show(apiKeyInputTooltip);
        }
    }

    /// <summary>
    /// Ukrywa tooltip pola klucza API.
    /// </summary>
    /// <remarks>Wywoływane przy opuszczeniu pola wejściowego.</remarks>
    /// <seealso cref="CustomTooltip.Hide"/>
    private void OnAPIKeyInputTooltipMouseExited()
    {
        if (customTooltip != null)
        {
            customTooltip.Hide();
        }
    }

    /// <summary>
    /// Pokazuje tooltip informacyjny przy przycisku pomocy API.
    /// </summary>
    /// <remarks>Tooltip opisuje krok po kroku pozyskanie klucza.</remarks>
    /// <seealso cref="CustomTooltip.Show"/>
    private void OnAPIKeyInfoTooltipMouseEntered()
    {
        if (customTooltip != null && !string.IsNullOrEmpty(apiKeyInfoTooltip))
        {
            customTooltip.Show(apiKeyInfoTooltip);
        }
    }

    /// <summary>
    /// Ukrywa tooltip informacyjny przy przycisku pomocy API.
    /// </summary>
    /// <remarks>Wywoływane po zejściu kursora z przycisku pomocy.</remarks>
    /// <seealso cref="CustomTooltip.Hide"/>
    private void OnAPIKeyInfoTooltipMouseExited()
    {
        if (customTooltip != null)
        {
            customTooltip.Hide();
        }
    }

    /// <summary>
    /// Opuszcza obecną drużynę lokalnego gracza.
    /// </summary>
    /// <remarks>Używane przez przycisk "Opuść" na liście drużyn.</remarks>
    /// <seealso cref="TryLeftTeam"/>
    private void OnLeaveTeamButtonPressed()
    {
        TryLeftTeam();
    }

    private EOSManager.Team currentLocalTeam = EOSManager.Team.None;

    /// <summary>
    /// Próbuje dołączyć lokalnego gracza do wskazanej drużyny, uwzględniając cooldown i bieżący przydział; deleguje zmianę do <see cref="EOSManager.SetMyTeam(EOSManager.Team)"/>.
    /// </summary>
    /// <remarks>Włącza globalny cooldown, aby ograniczyć spam zapytań.</remarks>
    /// <exception>Loguje błąd, gdy <see cref="EOSManager"/> jest niedostępny.</exception>
    /// <param name="teamName">Docelowa drużyna.</param>
    /// <seealso cref="UpdateTeamButtonsState"/>
    private void TryJoinTeam(EOSManager.Team teamName)
    {
        if (eosManager == null)
        {
            GD.PrintErr("[Lobby:Team] Cannot change team: EOSManager not available");
            return;
        }

        // Sprawdź czy cooldown jest aktywny
        if (isTeamChangeCooldownActive)
        {
            return;
        }

        if (currentLocalTeam == teamName)
        {
            GD.Print($"[Lobby:Team] Already in {teamName} team, ignoring join request");
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
            // Zaktualizuj stan przycisków po zakończeniu cooldownu
            UpdateTeamButtonsState(currentLocalTeam);
        };

        eosManager.SetMyTeam(teamName);
    }
    /// <summary>
    /// Próbuje przenieść lokalnego gracza do drużyny Neutral (opuszcza obecną) przez <see cref="TryJoinTeam"/>/<see cref="EOSManager.SetMyTeam(EOSManager.Team)"/>.
    /// </summary>
    /// <remarks>W praktyce oznacza dołączenie do drużyny "None".</remarks>
    /// <exception>Loguje błąd, gdy <see cref="EOSManager"/> jest niedostępny.</exception>
    /// <seealso cref="TryJoinTeam"/>
    private void TryLeftTeam()
    {
        if (eosManager == null)
        {
            GD.PrintErr("[Lobby:Team] Cannot leave team: EOSManager not available");
            return;
        }
        TryJoinTeam(EOSManager.Team.None);
    }

    /// <summary>
    /// Sprawdza czy podana drużyna osiągnęła limit graczy.
    /// </summary>
    /// <remarks>Drużyny Neutral i Universal nie mają limitu.</remarks>
    /// <param name="team">Drużyna do sprawdzenia.</param>
    /// <returns>True, jeśli liczba graczy w drużynie >= limitowi.</returns>
    /// <seealso cref="UpdateTeamButtonsState"/>
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

    /// <summary>
    /// Ustawia stan (tekst, blokady, handler) przycisków dołączania na podstawie bieżącej drużyny gracza i limitów.
    /// </summary>
    /// <remarks>Zmienia również handler przycisku na "Opuść" dla bieżącej drużyny.</remarks>
    /// <param name="localTeam">Aktualna drużyna lokalnego gracza.</param>
    /// <seealso cref="TryJoinTeam"/>
    /// <seealso cref="OnBlueTeamJoinButtonPressed"/>
    /// <seealso cref="OnRedTeamJoinButtonPressed"/>
    /// <seealso cref="OnLeaveTeamButtonPressed"/>
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

    /// <summary>
    /// Tymczasowo blokuje przycisk po akcji, by ograniczyć spam zapytań.
    /// </summary>
    /// <remarks>Odblokowuje przycisk po upływie <see cref="CooldownTime"/>.</remarks>
    /// <param name="button">Przycisk do zablokowania.</param>
    /// <seealso cref="CooldownTime"/>
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

    /// <summary>
    /// Nakłada cooldown na możliwość przenoszenia wskazanego gracza między drużynami.
    /// </summary>
    /// <remarks>Cooldown jest śledzony per userId w słowniku.</remarks>
    /// <param name="userId">Id gracza objętego cooldownem.</param>
    /// <seealso cref="TryJoinTeam"/>
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

    /// <summary>
    /// Obsługuje kliknięcie PPM na liście drużyny przez hosta i wyświetla menu akcji dla gracza.
    /// </summary>
    /// <remarks>Reaguje tylko na kliknięcia prawym przyciskiem myszy.</remarks>
    /// <param name="@event">Zdarzenie wejściowe z listy.</param>
    /// <param name="teamList">Lista, na której wykonano akcję.</param>
    /// <seealso cref="ShowMemberActionsPopup"/>
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

                            ShowMemberActionsPopup(userId, displayName, playerTeam, mouseEvent.GlobalPosition);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Tworzy menu kontekstowe dla gracza z akcjami przenoszenia, przekazania hosta (<see cref="EOSManager.TransferLobbyOwnership(string)"/>) lub wyrzucenia (<see cref="EOSManager.KickPlayer(string)"/>).
    /// </summary>
    /// <remarks>Po wyborze akcji menu usuwa się z drzewa sceny.</remarks>
    /// <param name="userId">Id gracza.</param>
    /// <param name="displayName">Wyświetlana nazwa gracza.</param>
    /// <param name="currentTeam">Aktualna drużyna gracza.</param>
    /// <param name="globalPosition">Pozycja ekranu do wyświetlenia menu.</param>
    /// <seealso cref="StartPlayerMoveCooldown"/>
    private void ShowMemberActionsPopup(string userId, string displayName, EOSManager.Team currentTeam, Vector2 globalPosition)
    {
        GD.Print($"[Lobby:ShowMemberActionsPopup] Creating popup menu for {displayName}");

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
                if (index == idxTransferHost)
                {
                    GD.Print($"[Lobby:ShowMemberActionsPopup] Transferring host to: {displayName}");
                    eosManager.TransferLobbyOwnership(userId);
                }
                else if (index == idxKickPlayer)
                {
                    GD.Print($"[Lobby:ShowMemberActionsPopup] Kicking player: {displayName}");
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

                if (index == idxMoveBlue)
                {
                    GD.Print($"[Lobby:ShowMemberActionsPopup] Moving player {displayName} to Blue via popup");
                    eosManager.MovePlayerToTeam(userId, EOSManager.Team.Blue);
                    StartPlayerMoveCooldown(userId);
                }
                else if (index == idxMoveRed)
                {
                    GD.Print($"[Lobby:ShowMemberActionsPopup] Moving player {displayName} to Red via popup");
                    eosManager.MovePlayerToTeam(userId, EOSManager.Team.Red);
                    StartPlayerMoveCooldown(userId);
                }
                else if (index == idxMoveNeutral)
                {
                    GD.Print($"[Lobby:ShowMemberActionsPopup] Moving player {displayName} to Neutral via popup");
                    eosManager.MovePlayerToTeam(userId, EOSManager.Team.None);
                    StartPlayerMoveCooldown(userId);
                }
                else if (index == idxTransferHost)
                {
                    GD.Print($"[Lobby:ShowMemberActionsPopup] Transferring host to: {displayName}");
                    eosManager.TransferLobbyOwnership(userId);
                }
                else if (index == idxKickPlayer)
                {
                    GD.Print($"[Lobby:ShowMemberActionsPopup] Kicking player: {displayName}");
                    eosManager.KickPlayer(userId);
                }

                popup.QueueFree();
            };
        }

        // Dodaj do drzewa i pokaż
        GetTree().Root.AddChild(popup);
        popup.Position = (Vector2I)globalPosition;
        popup.PopupOnParent(new Rect2I(popup.Position, new Vector2I(1, 1)));
    }

    /// <summary>
    /// Czyści sygnały i subskrypcje przed opuszczeniem sceny lobby.
    /// </summary>
    /// <remarks>Zapobiega pozostawieniu podwójnych handlerów po powrocie do sceny.</remarks>
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
            aiAPIKeyInput.MouseEntered -= OnAPIKeyInputTooltipMouseEntered;
            aiAPIKeyInput.MouseExited -= OnAPIKeyInputTooltipMouseExited;
        }

        if (apiKeyHelpButton != null)
        {
            apiKeyHelpButton.MouseEntered -= OnAPIKeyInfoTooltipMouseEntered;
            apiKeyHelpButton.MouseExited -= OnAPIKeyInfoTooltipMouseExited;
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