using Godot;
using System;

public partial class LobbyMenu : Control
{
    private EOSManager eosManager;
    private Button setNickButton;
    private LineEdit nicknameEdit;
    private Button backButton;
    private Button leaveLobbyButton;
    private ItemList blueTeamList;
    private ItemList redTeamList;
    private LineEdit lobbyIdInput;
    private Button copyIdButton;
    private Button generateNewIdButton;
    private Button startGameButton;
    private OptionButton gameModeList;
    private Label gameModeSelectedLabel;
    private string currentLobbyCode = "";
    private const int MaxRetryAttempts = 10;
    private const float RetryDelay = 0.5f;

    public override void _Ready()
    {
        // Pobierz EOSManager z autoload
        eosManager = GetNode<EOSManager>("/root/EOSManager");

        // Podłącz przycisk ustawiania nicku
        setNickButton = GetNode<Button>("Panel/NicknamePanel/SetNicknameButton");
        nicknameEdit = GetNode<LineEdit>("Panel/NicknamePanel/NicknameEdit");

        if (setNickButton != null)
        {
            setNickButton.Pressed += OnSetNicknamePressed;
        }

        // Podłącz przyciski nawigacji
        backButton = GetNode<Button>("Control/BackButton");
        if (backButton != null)
        {
            backButton.Pressed += OnBackButtonPressed;
        }

        leaveLobbyButton = GetNode<Button>("Panel/CenterContainer/LobbyMainContainer/LobbyContentContainer/LobbyFuncButtonsContainer/LeaveLobby");
        if (leaveLobbyButton != null)
        {
            leaveLobbyButton.Pressed += OnLeaveLobbyPressed;
        }

        // Pobierz elementy UI dla Lobby ID
        lobbyIdInput = GetNode<LineEdit>("Panel/CenterContainer/LobbyMainContainer/LobbyContentContainer/LobbyIDContainer/InputHolders/LobbyIDInput");
        copyIdButton = GetNode<Button>("Panel/CenterContainer/LobbyMainContainer/LobbyContentContainer/LobbyIDContainer/ActionButtons/HBoxContainer/CopyIDButton");
        generateNewIdButton = GetNode<Button>("Panel/CenterContainer/LobbyMainContainer/LobbyContentContainer/LobbyIDContainer/ActionButtons/HBoxContainer/GenerateNewIDButton");
        startGameButton = GetNode<Button>("Panel/CenterContainer/LobbyMainContainer/LobbyContentContainer/LobbyFuncButtonsContainer/StartGame");
        gameModeList = GetNode<OptionButton>("Panel/CenterContainer/LobbyMainContainer/LobbyContentContainer/LobbySettingsContainer/LobbyGameMode/GameModeList");
        gameModeSelectedLabel = GetNode<Label>("Panel/CenterContainer/LobbyMainContainer/LobbyContentContainer/LobbySettingsContainer/LobbyGameMode/GameModeSelected");

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

        // Pobierz listy drużyn
        blueTeamList = GetNode<ItemList>("Panel/CenterContainer/LobbyMainContainer/LobbyContentContainer/LobbyTeamsContainer/BlueTeamPanel/BlueTeamContainer/BlueTeamsMembers");
        redTeamList = GetNode<ItemList>("Panel/CenterContainer/LobbyMainContainer/LobbyContentContainer/LobbyTeamsContainer/RedTeamPanel/RedTeamContainer/RedTeamMembers");

        // WAŻNE: Podłącz sygnał z EOSManager do aktualizacji drużyn
        if (eosManager != null)
        {
            eosManager.LobbyMembersUpdated += OnLobbyMembersUpdated;
            eosManager.CustomLobbyIdUpdated += OnCustomLobbyIdUpdated;
            eosManager.GameModeUpdated += OnGameModeUpdated;
            GD.Print("✅ Connected to LobbyMembersUpdated, CustomLobbyIdUpdated and GameModeUpdated signals");

            // Sprawdź obecną wartość CustomLobbyId
            if (!string.IsNullOrEmpty(eosManager.currentCustomLobbyId))
            {
                GD.Print($"🆔 Current CustomLobbyId in EOSManager: '{eosManager.currentCustomLobbyId}'");
                OnCustomLobbyIdUpdated(eosManager.currentCustomLobbyId);
            }

            // Sprawdź obecną wartość GameMode
            if (!string.IsNullOrEmpty(eosManager.currentGameMode))
            {
                OnGameModeUpdated(eosManager.currentGameMode);
            }
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
        }
        else
        {
            GD.PrintErr("⚠️ Entered lobby scene but not in any lobby!");
        }
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
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        char[] code = new char[6];

        for (int i = 0; i < 6; i++)
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
        if (blueTeamList == null || redTeamList == null)
        {
            GD.PrintErr("❌ Team lists not found!");
            return;
        }

        GD.Print($"🔄 Updating team lists with {members.Count} members");

        // Wyczyść obie drużyny
        blueTeamList.Clear();
        redTeamList.Clear();

        // Rozdziel graczy na drużyny WEDŁUG ATRYBUTU "team"
        foreach (var member in members)
        {
            string displayName = member["displayName"].ToString();
            bool isOwner = (bool)member["isOwner"];
            bool isLocalPlayer = (bool)member["isLocalPlayer"];
            string team = member.ContainsKey("team") ? member["team"].ToString() : "";

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
            if (team == "Blue")
            {
                blueTeamList.AddItem(displayName);
                GD.Print($"  ➕ Blue: {displayName}");
            }
            else if (team == "Red")
            {
                redTeamList.AddItem(displayName);
                GD.Print($"  ➕ Red: {displayName}");
            }
            else
            {
                // Jeśli nie ma przypisanej drużyny, dodaj do niebieskiej jako tymczasowe
                GD.Print($"  ⚠️ No team assigned for {displayName}, waiting...");
            }
        }

        GD.Print($"✅ Teams updated: Blue={blueTeamList.ItemCount}, Red={redTeamList.ItemCount}");

        // Zaktualizuj widoczność przycisków dla hosta/gracza
        UpdateUIVisibility();
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

        // Zaktualizuj label (dla graczy)
        if (gameModeSelectedLabel != null)
        {
            gameModeSelectedLabel.Text = gameMode;
            GD.Print($"✅ GameMode label updated to: {gameMode}");
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

    private void OnSetNicknamePressed()
    {
        if (nicknameEdit == null) return;

        string nickname = nicknameEdit.Text.Trim();
        if (!string.IsNullOrEmpty(nickname))
        {
            eosManager.SetPendingNickname(nickname);
            GD.Print($"✅ Nickname set: {nickname}");
        }
        else
        {
            GD.Print("⚠️ Nickname is empty");
        }
    }

    private void OnSelectedGameModeChanged(long index)
    {
        if (gameModeList == null || eosManager == null) return;

        string selectedMode = gameModeList.GetItemText((int)index);

        // Ustaw tryb gry w EOSManager - zostanie zsynchronizowany z innymi graczami
        eosManager.SetGameMode(selectedMode);
        GD.Print($"✅ Game mode changed to: {selectedMode} (index: {index})");
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

        GetTree().ChangeSceneToFile("res://Scenes/MainMenu/main.tscn");
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

        GetTree().ChangeSceneToFile("res://Scenes/MainMenu/main.tscn");
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

        eosManager.CreateLobby(lobbyIdCode, 10, true);
        GD.Print("✅ EOS logged in, creating lobby. Lobby ID: " + lobbyIdCode);
    }

    public override void _ExitTree()
    {
        // Odłącz sygnały przy wyjściu
        if (eosManager != null)
        {
            eosManager.LobbyMembersUpdated -= OnLobbyMembersUpdated;
            eosManager.CustomLobbyIdUpdated -= OnCustomLobbyIdUpdated;
            eosManager.GameModeUpdated -= OnGameModeUpdated;
        }
    }
}
