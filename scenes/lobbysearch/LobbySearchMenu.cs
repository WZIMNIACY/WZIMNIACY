using Godot;

/// <summary>
/// Ekran wyszukiwania i dołączania do lobby po CustomLobbyId: obsługuje wklejanie/Enter,
/// animację przycisku, timeout oraz przejście do sceny lobby po sukcesie.
/// Korzysta z <see cref="EOSManager.JoinLobbyByCustomId(string)"/> i sygnałów <see cref="EOSManager.LobbyJoined"/>/<see cref="EOSManager.LobbyJoinFailed"/> oraz <see cref="PasteDetector"/>.
/// </summary>
/// <remarks>
/// Wymaga autoloadu <see cref="EOSManager"/> oraz przypiętych węzłów UI w scenie. Logika powinna działać w wątku głównym Godota; klasa nie jest thread-safe.
/// </remarks>
public partial class LobbySearchMenu : Node
{
    private const string LobbyScenePath = "res://scenes/lobby/Lobby.tscn";

    /// <summary>Autoload EOS do obsługi logiki lobby.</summary>
    private EOSManager eosManager;

    /// <summary>Przycisk powrotu do menu.</summary>
    [Export] private Button backButton;
    /// <summary>Pole wprowadzania ID lobby.</summary>
    [Export] private LineEdit searchInput;
    /// <summary>Przycisk dołączenia do lobby.</summary>
    [Export] private Button joinButton;

    /// <summary>Detektor wklejania ułatwiający szybkie dołączenie.</summary>
    private PasteDetector pasteDetector;
    private PopupSystem popupSystem;

    // Animacja przycisku
    /// <summary>Timer dodający kropki do tekstu przycisku.</summary>
    private Timer animationTimer;
    /// <summary>Licznik kropek w animacji.</summary>
    private int dotCount = 0;
    /// <summary>Flaga informująca, że trwa próba dołączenia.</summary>
    private bool isJoining = false;

    // Timeout dla dołączania
    /// <summary>Timer nadzorujący przekroczenie czasu dołączenia.</summary>
    private Timer joinTimeoutTimer;
    private const float JoinTimeout = 7.0f; // 7 sekund timeout

    // Timer dla timeoutu opuszczania lobby
    private Timer leaveTimeoutTimer;
    private const float LeaveTimeout = 3.0f; // 3 sekund timeout na opuszczenie

    // Zabezpieczenie przed wielokrotnym wywołaniem
    private bool isPending = false;

    // Zapamietany kod lobby do dołączenia po opuszczeniu obecnego
    private string pendingLobbyCodeToJoin = null;

    /// <summary>
    /// Inicjalizuje referencje do <see cref="EOSManager"/>, podpina sygnały UI i tworzy timery animacji oraz timeoutu (wykorzystywane w <see cref="OnAnimationTimerTimeout"/> i <see cref="OnJoinTimeout"/>).
    /// </summary>
    /// <seealso cref="OnJoinButtonPressed"/>
    /// <seealso cref="PasteDetector.RegisterPasteCallback"/>
    public override void _Ready()
    {
        base._Ready();

        // Pobierz EOSManager z autoload
        eosManager = GetNode<EOSManager>("/root/EOSManager");

        // Podłącz sygnały z EOSManager
        if (eosManager != null)
        {
            eosManager.LobbyJoined += OnLobbyJoinedSuccessfully;
            eosManager.LobbyJoinFailed += OnLobbyJoinFailed;
            eosManager.LobbyLeft += OnLobbyLeftSuccessfully;
        }

        // Podłącz sygnały przycisków
        if (backButton != null)
        {
            backButton.Pressed += OnBackButtonPressed;
        }

        if (joinButton != null)
        {
            joinButton.Pressed += OnJoinButtonPressed;
        }

        // Podłącz Enter w polu wpisywania
        if (searchInput != null)
        {
            searchInput.TextSubmitted += OnSearchInputSubmitted;
        }

        // Utwórz timer dla animacji
        animationTimer = new Timer();
        animationTimer.WaitTime = 0.5; // Co 0.5 sekundy dodaj kropkę
        animationTimer.Timeout += OnAnimationTimerTimeout;
        AddChild(animationTimer);

        // Utwórz timer dla timeoutu
        joinTimeoutTimer = new Timer();
        joinTimeoutTimer.WaitTime = JoinTimeout;
        joinTimeoutTimer.OneShot = true;
        joinTimeoutTimer.Timeout += OnJoinTimeout;
        AddChild(joinTimeoutTimer);

        // Utwórz timer dla timeoutu opuszczania
        leaveTimeoutTimer = new Timer();
        leaveTimeoutTimer.WaitTime = LeaveTimeout;
        leaveTimeoutTimer.OneShot = true;
        leaveTimeoutTimer.Timeout += OnLeaveTimeout;
        AddChild(leaveTimeoutTimer);

        pasteDetector = GetNodeOrNull<PasteDetector>("PasteDetector");
        if (pasteDetector != null)
        {
            // Ustaw Target programatycznie zamiast z .tscn
            pasteDetector.Target = searchInput;
            pasteDetector.RegisterPasteCallback(OnLobbyIdPasted);
        }

        // Załaduj popup system
        var popupScene = GD.Load<PackedScene>("res://scenes/popup/PopupSystem.tscn");
        if (popupScene != null)
        {
            popupSystem = popupScene.Instantiate<PopupSystem>();
            AddChild(popupSystem);
        }
    }

    /// <summary>
    /// Wywoływane gdy użytkownik wklei tekst do pola lobby ID.
    /// </summary>
    /// <param name="pastedText">Wklejony ciąg znaków (ignorowany; używany do uruchomienia logiki join).</param>
    /// <seealso cref="OnJoinButtonPressed"/>
    private void OnLobbyIdPasted(string pastedText)
    {
        GD.Print($"[LobbySearchMenu] Lobby ID pasted: {pastedText}");

        // Wywołaj tę samą funkcję co przycisk "Dołącz"
        OnJoinButtonPressed();
        joinButton.GrabFocus();
    }

    /// <summary>
    /// Wywoływane gdy użytkownik naciśnie Enter w polu lobby ID.
    /// </summary>
    /// <param name="text">Aktualny tekst w polu wyszukiwania.</param>
    /// <seealso cref="OnJoinButtonPressed"/>
    private void OnSearchInputSubmitted(string text)
    {
        GD.Print($"[LobbySearchMenu] Enter pressed in search input: {text}");
        OnJoinButtonPressed();
        joinButton.GrabFocus();
    }

    /// <summary>
    /// Obsługuje cofnięcie do głównego menu z ekranu wyszukiwania lobby.
    /// </summary>
    private void OnBackButtonPressed()
    {
        GD.Print("[LobbySearchMenu] Returning to main menu...");
        GetTree().ChangeSceneToFile("res://scenes/menu/main.tscn");
    }

    /// <summary>
    /// Waliduje ID lobby, uruchamia animację i timeout, a następnie prosi <see cref="EOSManager.JoinLobbyByCustomId(string)"/> o dołączenie.
    /// </summary>
    /// <seealso cref="StartJoiningAnimation"/>
    /// <seealso cref="StopJoiningAnimation"/>
    /// <seealso cref="OnJoinTimeout"/>
    /// <exception>Loguje błąd, gdy pole wyszukiwania lub <see cref="EOSManager"/> jest puste.</exception>
    private void OnJoinButtonPressed()
    {
        if (searchInput == null || eosManager == null)
        {
            GD.PrintErr("[LobbySearchMenu] Search input or EOSManager is null!");
            return;
        }
        if (isPending)
        {
            return;
        }

        string customId = searchInput.Text.Trim().ToUpper();

        if (string.IsNullOrEmpty(customId))
        {
            GD.Print("[LobbySearchMenu] Please enter a lobby ID");
            return;
        }

        GD.Print($"[LobbySearchMenu] Attempting to join lobby: {customId}");

        // Ustaw flagę pending
        isPending = true;

        // Rozpocznij animację dołączania
        StartJoiningAnimation();

        // Sprawdź czy gracz jest już w jakimś lobby
        if (!string.IsNullOrEmpty(eosManager.currentLobbyId))
        {
            GD.Print($"⚠️ Player is already in lobby {eosManager.currentLobbyId}, leaving first...");

            // Zapisz kod lobby do dołączenia po opuszczeniu obecnego
            pendingLobbyCodeToJoin = customId;
            eosManager.LeaveLobby();
            leaveTimeoutTimer.Start();
            return;
        }

        // Jeśli nie ma obecnego lobby, dołącz bezpośrednio
        JoinLobbyByCode(customId);
    }

    /// <summary>
    /// Faktycznie dołącza do lobby po podanym kodzie
    /// </summary>
    private void JoinLobbyByCode(string customId)
    {
        GD.Print($"🔗 Joining lobby: {customId}");

        // Wyszukaj i dołącz do lobby (scena zmieni się automatycznie po sygnale LobbyJoined)
        eosManager.JoinLobbyByCustomId(customId);

        // Uruchom timeout timer
        joinTimeoutTimer.Start();
    }

    /// <summary>
    /// Rozpoczyna animację "Dołączanie..." z kolejnymi kropkami.
    /// </summary>
    /// <seealso cref="OnAnimationTimerTimeout"/>
    private void StartJoiningAnimation()
    {
        if (joinButton == null) return;

        isJoining = true;
        dotCount = 0;
        joinButton.Disabled = true;
        joinButton.Text = "Dołączanie";

        // Zablokuj również przycisk Menu
        if (backButton != null)
        {
            backButton.Disabled = true;
            backButton.FocusMode = Control.FocusModeEnum.None;
            backButton.MouseDefaultCursorShape = Control.CursorShape.Forbidden;
        }

        // Uruchom timer animacji
        animationTimer.Start();
    }

    /// <summary>
    /// Zatrzymuje animację i przywraca przycisk do stanu początkowego.
    /// </summary>
    /// <seealso cref="StartJoiningAnimation"/>
    /// <seealso cref="OnJoinTimeout"/>
    private void StopJoiningAnimation()
    {
        if (joinButton == null) return;

        isJoining = false;
        isPending = false;
        animationTimer.Stop();
        joinTimeoutTimer.Stop();
        leaveTimeoutTimer.Stop();

        joinButton.Disabled = false;
        joinButton.Text = "Dołącz";

        // Odblokuj przycisk Menu
        if (backButton != null)
        {
            backButton.Disabled = false;
            backButton.FocusMode = Control.FocusModeEnum.All;
            backButton.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        }
    }

    /// <summary>
    /// Callback timera animacji – aktualizuje tekst przycisku o kolejne kropki.
    /// </summary>
    /// <seealso cref="StartJoiningAnimation"/>
    private void OnAnimationTimerTimeout()
    {
        if (!isJoining || joinButton == null) return;

        dotCount = (dotCount + 1) % 4; // 0, 1, 2, 3, 0, ...

        string dots = new string('.', dotCount);
        joinButton.Text = "Dołączanie" + dots;
    }

    /// <summary>
    /// Reaguje na przekroczenie czasu dołączenia: loguje błąd i przywraca stan przycisku po wywołaniu <see cref="OnJoinButtonPressed"/>.
    /// </summary>
    /// <seealso cref="StopJoiningAnimation"/>
    /// <exception>Loguje błąd w przypadku przekroczenia czasu dołączenia.</exception>
    private void OnJoinTimeout()
    {
        GD.PrintErr("[LobbySearchMenu:JoinLobby] Join timeout - lobby not found or connection failed");

        pendingLobbyCodeToJoin = null;

        // Przywróć przycisk
        StopJoiningAnimation();

        // Pokaż komunikat użytkownikowi
        if (popupSystem != null)
        {
            popupSystem.ShowMessage(
                "★ BŁĄD POŁĄCZENIA ★",
                "Nie udało się dołączyć do lobby.\n\nLobby może nie istnieć lub przekroczono limit czasu połączenia.\n\nSpróbuj ponownie."
            );
        }
    }

    /// <summary>
    /// Callback gdy przekroczono timeout opuszczania lobby
    /// </summary>
    private void OnLeaveTimeout()
    {
        GD.PrintErr("❌ Leave timeout - failed to leave previous lobby");

        // Wyczyść pending lobby code
        pendingLobbyCodeToJoin = null;

        // Przywróć przycisk
        StopJoiningAnimation();

        // Pokaż komunikat użytkownikowi
        if (popupSystem != null)
        {
            popupSystem.ShowMessage(
                "★ BŁĄD POŁĄCZENIA ★",
                "Nie udało się opuścić poprzedniego lobby.\n\nSpróbuj ponownie lub uruchom ponownie grę."
            );
        }
    }

    /// <summary>
    /// Callback wywoływany gdy dołączenie do lobby się nie powiodło (sygnał <see cref="EOSManager.LobbyJoinFailed"/>).
    /// </summary>
    /// <param name="errorMessage">Opis błędu przekazany z EOSManager.</param>
    /// <seealso cref="StopJoiningAnimation"/>
    /// <exception>Loguje błąd, gdy dołączenie do lobby się nie powiedzie.</exception>
    private void OnLobbyJoinFailed(string errorMessage)
    {
        GD.PrintErr($"[LobbySearchMenu:JoinLobby] Failed to join lobby: {errorMessage}");

        pendingLobbyCodeToJoin = null;

        // Przywróć przycisk
        StopJoiningAnimation();

        // Pokaż komunikat użytkownikowi z odpowiednim powodem
        if (popupSystem != null)
        {
            string userMessage = "Nie udało się dołączyć do lobby.";
            string reason = "";

            // Parsuj błąd i dostosuj komunikat
            if (errorMessage.Contains("full") || errorMessage.Contains("Full"))
            {
                reason = "\n\nPowód: Lobby jest pełne.\nZa dużo graczy w lobby.";
            }
            else if (errorMessage.Contains("not found") || errorMessage.Contains("NotFound"))
            {
                reason = "\n\nPowód: Lobby nie zostało znalezione.\nSprawdź czy kod jest poprawny.";
            }
            else if (errorMessage.Contains("connection") || errorMessage.Contains("Connection"))
            {
                reason = "\n\nPowód: Błąd połączenia z serwerem.\nSprawdź połączenie internetowe.";
            }
            else
            {
                reason = $"\n\nPowód: {errorMessage}";
            }

            popupSystem.ShowMessage(
                "★ NIE UDAŁO SIĘ DOŁĄCZYĆ ★",
                userMessage + reason
            );
        }
    }

    /// <summary>
    /// Callback wywoływany po opuszczeniu lobby
    /// </summary>
    private void OnLobbyLeftSuccessfully()
    {
        GD.Print($"✅ Successfully left lobby");

        leaveTimeoutTimer.Stop();

        // Jeśli mamy zapamiętany kod lobby do dołączenia, dołącz teraz
        if (!string.IsNullOrEmpty(pendingLobbyCodeToJoin))
        {
            string codeToJoin = pendingLobbyCodeToJoin;
            pendingLobbyCodeToJoin = null;

            GD.Print($"➡️ Now joining lobby: {codeToJoin}");
            JoinLobbyByCode(codeToJoin);
        }
    }

    /// <summary>
    /// Callback wywoływany po pomyślnym dołączeniu do lobby (sygnał <see cref="EOSManager.LobbyJoined"/>); zmienia scenę po krótkim opóźnieniu.
    /// </summary>
    /// <param name="lobbyId">Identyfikator lobby, do którego dołączono.</param>
    /// <seealso cref="StopJoiningAnimation"/>
    /// <seealso cref="OnJoinButtonPressed"/>
    private void OnLobbyJoinedSuccessfully(string lobbyId)
    {
        GD.Print($"[LobbySearchMenu] Successfully joined lobby {lobbyId}, changing scene...");

        pendingLobbyCodeToJoin = null;

        // Teraz możemy bezpiecznie zmienić scenę
        // Dodaj małe opóźnienie, aby użytkownik zauważył zmianę stanu
        GetTree().CreateTimer(2.1).Timeout += () =>
        {
            // Zatrzymaj animację i timeout
            StopJoiningAnimation();
            GetTree().ChangeSceneToFile(LobbyScenePath);
        };
    }

    /// <summary>
    /// Czyści timery oraz odłącza sygnały UI i EOSManager przy zamykaniu sceny.
    /// </summary>
    public override void _ExitTree()
    {
        base._ExitTree();

        // Zatrzymaj i usuń timery
        if (animationTimer != null)
        {
            animationTimer.Stop();
            animationTimer.QueueFree();
        }

        if (joinTimeoutTimer != null)
        {
            joinTimeoutTimer.Stop();
            joinTimeoutTimer.QueueFree();
        }

        if (leaveTimeoutTimer != null)
        {
            leaveTimeoutTimer.Stop();
            leaveTimeoutTimer.QueueFree();
        }

        // Odłącz sygnały z przycisków
        if (backButton != null)
        {
            backButton.Pressed -= OnBackButtonPressed;
        }

        if (joinButton != null)
        {
            joinButton.Pressed -= OnJoinButtonPressed;
        }

        if (searchInput != null)
        {
            searchInput.TextSubmitted -= OnSearchInputSubmitted;
        }

        // Odłącz sygnały z EOSManager
        if (eosManager != null)
        {
            eosManager.LobbyJoined -= OnLobbyJoinedSuccessfully;
            eosManager.LobbyJoinFailed -= OnLobbyJoinFailed;
            eosManager.LobbyLeft -= OnLobbyLeftSuccessfully;
        }
    }
}