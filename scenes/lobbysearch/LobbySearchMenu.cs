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

        string customId = searchInput.Text.Trim().ToUpper();

        if (string.IsNullOrEmpty(customId))
        {
            GD.Print("⚠️ Please enter a lobby ID");
            return;
        }

        GD.Print($"🚀 Attempting to join lobby: {customId}");

        // Rozpocznij animację dołączania
        StartJoiningAnimation();

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
    }

    /// <summary>
    /// Zatrzymuje animację i przywraca przycisk do stanu początkowego
    /// </summary>
    private void StopJoiningAnimation()
    {
        if (joinButton == null) return;

        isJoining = false;
        joinTimeoutTimer.Stop();

        joinButton.Disabled = false;
        joinButton.Text = "Dołącz";
        joinButton.CustomMinimumSize = new Vector2(0, 0);

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

        // Przywróć przycisk
        StopJoiningAnimation();

        // Możesz tu dodać komunikat dla użytkownika
        GD.Print("⚠️ Nie udało się dołączyć do lobby. Spróbuj ponownie.");
    }

    /// <summary>
    /// Callback wywoływany gdy dołączenie do lobby się NIE POWIODŁO
    /// </summary>
    private void OnLobbyJoinFailed(string errorMessage)
    {
        GD.PrintErr($"❌ Failed to join lobby: {errorMessage}");

        // Przywróć przycisk
        StopJoiningAnimation();

        // Możesz tu wyświetlić komunikat użytkownikowi
        GD.Print($"⚠️ {errorMessage}");
    }

    /// <summary>
    /// Callback wywoływany po POMYŚLNYM dołączeniu do lobby
    /// </summary>
    private void OnLobbyJoinedSuccessfully(string lobbyId)
    {
        GD.Print($"✅ Successfully joined lobby {lobbyId}, changing scene...");

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
        }
    }
}