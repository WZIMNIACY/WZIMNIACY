using Godot;
using System;
public partial class MainMenu : Control
{
    private const string LobbyMenuString = "res://Scenes/Lobby/Lobby.tscn";
    private const string LobbySearchMenuString = "res://Scenes/LobbySearch/LobbySearch.tscn";
    private const string SettingsSceneString = "res://Scenes/Settings/Settings.tscn";
    private const string HelpSceneString = "res://Scenes/Help/Help.tscn";
    private EOSManager eosManager;

    private Button createButton;
    private Button settingsButton;
    private Button helpButton;
    private Timer animationTimer;
    private int dotCount = 0;
    private bool isCreatingLobby = false;
    private const float CreateTimeout = 5.0f; // 5 sekund timeout

    public override void _Ready()
    {
        createButton = GetNode<Button>("Panel/MenuCenter/VMenu/CreateGame/CreateGameButton");
        Button joinButton = GetNode<Button>("Panel/MenuCenter/VMenu/JoinGame/JoinGameButton");
        Button quitButton = GetNode<Button>("Panel/MenuCenter/VMenu/Quit/QuitButton");
        settingsButton = GetNode<Button>("Panel/MenuCenter/VMenu/Settings/SettingsButton");
        helpButton = GetNode<Button>("Panel/MenuCenter/VMenu/Help/HelpButton");

        eosManager = GetNode<EOSManager>("/root/EOSManager");

        createButton.Pressed += OnCreateGamePressed;
        joinButton.Pressed += OnJoinGamePressed;
        quitButton.Pressed += OnQuitPressed;
        settingsButton.Pressed += OnSettingsPressed;
        helpButton.Pressed += OnHelpPressed;

        // Podłącz sygnał LobbyCreated
        if (eosManager != null)
        {
            eosManager.LobbyCreated += OnLobbyCreated;
        }
    }

    private void OnCreateGamePressed()
    {
        if (isCreatingLobby) return; // Zapobiegnij wielokrotnemu klikaniu

        GD.Print("Creating lobby in background...");

        //Opuść obecne lobby jeśli jesteś w jakimś
        if (eosManager != null && !string.IsNullOrEmpty(eosManager.currentLobbyId))
        {
            GD.Print("🚪 Leaving lobby before creating a new one...");
            eosManager.LeaveLobby();
        }

        // Rozpocznij animację przycisku
        StartCreatingAnimation();

        // Utwórz lobby w tle
        if (eosManager != null)
        {
            string lobbyId = GenerateLobbyIDCode();
            eosManager.CreateLobby(lobbyId, 10, true);
        }
    }

    private void OnLobbyCreated(string lobbyId)
    {
        GD.Print($"✅ Lobby created: {lobbyId}, changing scene...");

        // Zatrzymaj animację
        StopCreatingAnimation();

        // Poczekaj chwilę na ustawienie atrybutów (0.5s)
        GetTree().CreateTimer(0.5).Timeout += () =>
        {
            // Przejdź do sceny lobby
            GetTree().ChangeSceneToFile(LobbyMenuString);
        };
    }

    private void StartCreatingAnimation()
    {
        isCreatingLobby = true;
        createButton.Disabled = true;
        dotCount = 0;

        // Zapisz oryginalną wysokość przycisku
        float originalHeight = createButton.Size.Y;
        createButton.CustomMinimumSize = new Vector2(0, originalHeight);

        // Utwórz timer dla animacji
        animationTimer = new Timer();
        animationTimer.WaitTime = 0.5;
        animationTimer.Timeout += OnAnimationTimerTimeout;
        AddChild(animationTimer);
        animationTimer.Start();

        // Utwórz timer dla timeoutu
        Timer timeoutTimer = new Timer();
        timeoutTimer.WaitTime = CreateTimeout;
        timeoutTimer.OneShot = true;
        timeoutTimer.Timeout += () =>
        {
            GD.PrintErr("❌ Lobby creation timed out!");
            StopCreatingAnimation();
        };
        AddChild(timeoutTimer);
        timeoutTimer.Start();

        createButton.Text = "Tworzenie";
    }

    private void StopCreatingAnimation()
    {
        isCreatingLobby = false;
        createButton.Disabled = false;
        createButton.Text = "Utwórz grę";

        // Przywróć automatyczny rozmiar
        createButton.CustomMinimumSize = new Vector2(0, 0);

        if (animationTimer != null)
        {
            animationTimer.Stop();
            animationTimer.QueueFree();
            animationTimer = null;
        }
    }

    private void OnAnimationTimerTimeout()
    {
        dotCount = (dotCount + 1) % 4; // 0, 1, 2, 3, potem znowu 0
        string dots = new string('.', dotCount);
        createButton.Text = "Tworzenie" + dots;
    }

    private string GenerateLobbyIDCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        char[] code = new char[6];

        for (int i = 0; i < 6; i++)
        {
            code[i] = chars[random.Next(chars.Length)];
        }

        return new string(code);
    }

    private void OnJoinGamePressed()
    {
        GD.Print("Loading Lobby Search scene...");
        GetTree().ChangeSceneToFile(LobbySearchMenuString);
    }

    private void OnQuitPressed()
    {
        GD.Print("Quitting game...");
        GetTree().Quit();
    }

    private void OnSettingsPressed()
    {
        GD.Print("Loading Settings scene...");
        GetTree().ChangeSceneToFile(SettingsSceneString);
    }

    private void OnHelpPressed()
    {
        GD.Print("Loading Help scene...");
        GetTree().ChangeSceneToFile(HelpSceneString);
    }

    public override void _ExitTree()
    {
        // Odłącz sygnał przy wyjściu
        if (eosManager != null)
        {
            eosManager.LobbyCreated -= OnLobbyCreated;
        }

        // Wyczyść timer jeśli istnieje
        if (animationTimer != null)
        {
            animationTimer.QueueFree();
        }
    }
}