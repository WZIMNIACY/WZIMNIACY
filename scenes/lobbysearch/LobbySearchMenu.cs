using Godot;

public partial class LobbySearchMenu : Node
{
    private const string LobbyScenePath = "res://scenes/lobby/Lobby.tscn";

    private EOSManager eosManager;

    [Export] private Button backButton;
    [Export] private LineEdit searchInput;
    [Export] private Button joinButton;

    private PasteDetector pasteDetector;

    // Animacja przycisku
    private ColorRect loadingOverlay;
    private Tween loadingTween;
    private bool isJoining = false;

    // Timeout dla dołączania
    private Timer joinTimeoutTimer;
    private const float JoinTimeout = 7.0f; // 7 sekund timeout

    // Timer dla timeoutu opuszczania lobby
    private Timer leaveTimeoutTimer;
    private const float LeaveTimeout = 3.0f; // 3 sekund timeout na opuszczenie

    // Zabezpieczenie przed wielokrotnym wywołaniem
    private bool isPending = false;

    // Zapamietany kod lobby do dołączenia po opuszczeniu obecnego
    private string pendingLobbyCodeToJoin = null;

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
            loadingOverlay = joinButton.GetNode<ColorRect>("LoadingOverlay");
            GD.Print("✅ Join button connected successfully");
        }

        // Podłącz Enter w polu wpisywania
        if (searchInput != null)
        {
            searchInput.TextSubmitted += OnSearchInputSubmitted;
            GD.Print("✅ Search input Enter handler connected");
        }

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
    }

    /// <summary>
    /// Wywoływane gdy użytkownik wklei tekst do pola lobby ID
    /// </summary>
    private void OnLobbyIdPasted(string pastedText)
    {
        GD.Print($"📋 Lobby ID pasted: {pastedText}");

        // Wywołaj tę samą funkcję co przycisk "Dołącz"
        OnJoinButtonPressed();
        joinButton.GrabFocus();
    }

    /// <summary>
    /// Wywoływane gdy użytkownik naciśnie Enter w polu lobby ID
    /// </summary>
    private void OnSearchInputSubmitted(string text)
    {
        GD.Print($"⏎ Enter pressed in search input: {text}");
        OnJoinButtonPressed();
        joinButton.GrabFocus();
    }

    private void OnBackButtonPressed()
    {
        GD.Print("Returning to main menu...");
        GetTree().ChangeSceneToFile("res://scenes/menu/main.tscn");
    }

    private void OnJoinButtonPressed()
    {
        if (searchInput == null || eosManager == null)
        {
            GD.PrintErr("❌ Search input or EOSManager is null!");
            return;
        }
        if (isPending)
        {
            return;
        }

        string customId = searchInput.Text.Trim().ToUpper();

        if (string.IsNullOrEmpty(customId))
        {
            GD.Print("⚠️ Please enter a lobby ID");
            return;
        }

        GD.Print($"🚀 Attempting to join lobby: {customId}");

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
    /// Rozpoczyna animację ładowania z gradientem
    /// </summary>
    private void StartJoiningAnimation()
    {
        if (joinButton == null) return;

        isJoining = true;
        joinButton.Disabled = true;
        joinButton.Text = "Ładowanie";

        float originalHeight = joinButton.Size.Y;
        joinButton.CustomMinimumSize = new Vector2(0, originalHeight);

        if (loadingOverlay != null)
        {
            loadingOverlay.Visible = true;
            loadingOverlay.Size = new Vector2(0, joinButton.Size.Y);
            
            // Animacja wypełniania trwa 7 sekund (cały timeout)
            loadingTween = CreateTween();
            loadingTween.TweenProperty(loadingOverlay, "size", new Vector2(joinButton.Size.X, joinButton.Size.Y), JoinTimeout)
                .SetTrans(Tween.TransitionType.Linear)
                .SetEase(Tween.EaseType.InOut);
        }
        // Zablokuj również przycisk Menu
        if (backButton != null)
        {
            backButton.Disabled = true;
            backButton.FocusMode = Control.FocusModeEnum.None;
            backButton.MouseDefaultCursorShape = Control.CursorShape.Forbidden;
        }
    }

    /// <summary>
    /// Zatrzymuje animację i przywraca przycisk do stanu początkowego
    /// </summary>
    private void StopJoiningAnimation()
    {
        if (joinButton == null) return;

        isJoining = false;
        isPending = false;
        joinTimeoutTimer.Stop();
        leaveTimeoutTimer.Stop();

        joinButton.Disabled = false;
        joinButton.Text = "Dołącz";
        joinButton.CustomMinimumSize = new Vector2(0, 0);

        // Odblokuj przycisk Menu
        if (backButton != null)
        {
            backButton.Disabled = false;
            backButton.FocusMode = Control.FocusModeEnum.All;
            backButton.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        }
    }

    /// <summary>
    /// Callback dla timera animacji - dodaje kolejne kropki
    /// </summary>
    private void OnAnimationTimerTimeout()
    {
        if (!isJoining || joinButton == null) return;

        if (loadingOverlay != null)
        {
            loadingOverlay.Visible = false;
            loadingOverlay.Size = new Vector2(0, loadingOverlay.Size.Y);
        }

        if (loadingTween != null)
        {
            loadingTween.Kill();
            loadingTween = null;
        }
    }

    /// <summary>
    /// Callback gdy przekroczono timeout dołączania
    /// </summary>
    private void OnJoinTimeout()
    {
        GD.PrintErr("❌ Join timeout - lobby not found or connection failed");

        pendingLobbyCodeToJoin = null;

        // Przywróć przycisk
        StopJoiningAnimation();

        // Możesz tu dodać komunikat dla użytkownika
        GD.Print("⚠️ Nie udało się dołączyć do lobby. Spróbuj ponownie.");
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

        // Możesz tu dodać komunikat dla użytkownika
        GD.Print("⚠️ Nie udało się opuścić poprzedniego lobby. Spróbuj ponownie.");
    }

    /// <summary>
    /// Callback wywoływany gdy dołączenie do lobby się NIE POWIODŁO
    /// </summary>
    private void OnLobbyJoinFailed(string errorMessage)
    {
        GD.PrintErr($"❌ Failed to join lobby: {errorMessage}");

        pendingLobbyCodeToJoin = null;

        // Przywróć przycisk
        StopJoiningAnimation();

        // Możesz tu wyświetlić komunikat użytkownikowi
        GD.Print($"⚠️ {errorMessage}");
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
    /// Callback wywoływany po POMYŚLNYM dołączeniu do lobby
    /// </summary>
    private void OnLobbyJoinedSuccessfully(string lobbyId)
    {
        GD.Print($"✅ Successfully joined lobby {lobbyId}, changing scene...");

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

    public override void _ExitTree()
    {
        base._ExitTree();

        // Zatrzymaj i usuń timer
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