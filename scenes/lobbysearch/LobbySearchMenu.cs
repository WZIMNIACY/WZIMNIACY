using Godot;
using System;

public partial class LobbySearchMenu : Control
{
    private const string LobbyScenePath = "res://Scenes/Lobby/Lobby.tscn";
    private EOSManager eosManager;
    private Button backButton;
    private LineEdit searchInput;
    private Button joinButton;

    // Animacja przycisku
    private Timer animationTimer;
    private int dotCount = 0;
    private bool isJoining = false;

    // Timeout dla dołączania
    private Timer joinTimeoutTimer;
    private const float JoinTimeout = 7.0f; // 7 sekund timeout

    public override void _Ready()
    {
        // Pobierz EOSManager z autoload
        eosManager = GetNode<EOSManager>("/root/EOSManager");

        // Podłącz sygnały z EOSManager
        if (eosManager != null)
        {
            eosManager.LobbyJoined += OnLobbyJoinedSuccessfully;
            eosManager.LobbyJoinFailed += OnLobbyJoinFailed;
            GD.Print("✅ Connected to LobbyJoined and LobbyJoinFailed signals");
        }

        // Podłącz przycisk powrotu
        backButton = GetNode<Button>("Control/BackButton2");
        if (backButton != null)
        {
            backButton.Pressed += OnBackButtonPressed;
        }

        // Pobierz elementy UI do wyszukiwania lobby
        searchInput = GetNode<LineEdit>("Panel/CenterContainer/LobbyConnectPanel/ConnectionContainer/LobbyIDInput");
        joinButton = GetNode<Button>("Panel/CenterContainer/LobbyConnectPanel/ConnectionContainer/ConnectToLobbyButton");

        if (joinButton != null)
        {
            joinButton.Pressed += OnJoinButtonPressed;
            GD.Print("✅ Join button connected successfully");
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
    }

    private void OnBackButtonPressed()
    {
        GD.Print("Returning to main menu...");
        GetTree().ChangeSceneToFile("res://Scenes/MainMenu/main.tscn");
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
    /// Rozpoczyna animację "Dołączanie..." z kolejnymi kropkami
    /// </summary>
    private void StartJoiningAnimation()
    {
        if (joinButton == null) return;

        isJoining = true;
        dotCount = 0;
        joinButton.Disabled = true;
        joinButton.Text = "Dołączanie";

        // Uruchom timer animacji
        animationTimer.Start();
    }

    /// <summary>
    /// Zatrzymuje animację i przywraca przycisk do stanu początkowego
    /// </summary>
    private void StopJoiningAnimation()
    {
        if (joinButton == null) return;

        isJoining = false;
        animationTimer.Stop();
        joinTimeoutTimer.Stop();

        joinButton.Disabled = false;
        joinButton.Text = "Dołącz";
    }

    /// <summary>
    /// Callback dla timera animacji - dodaje kolejne kropki
    /// </summary>
    private void OnAnimationTimerTimeout()
    {
        if (!isJoining || joinButton == null) return;

        dotCount = (dotCount + 1) % 4; // 0, 1, 2, 3, 0, ...

        string dots = new string('.', dotCount);
        joinButton.Text = "Dołączanie" + dots;
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

        // Odłącz sygnały z przycisków
        if (backButton != null)
        {
            backButton.Pressed -= OnBackButtonPressed;
        }

        if (joinButton != null)
        {
            joinButton.Pressed -= OnJoinButtonPressed;
        }

        // Odłącz sygnały z EOSManager
        if (eosManager != null)
        {
            eosManager.LobbyJoined -= OnLobbyJoinedSuccessfully;
            eosManager.LobbyJoinFailed -= OnLobbyJoinFailed;
        }
    }
}