using Godot;
using System;

public partial class MainMenu : Node
{
	// --- ŚCIEŻKI ---
	private const string LobbyMenuString = "res://scenes/lobby/Lobby.tscn";
	private const string LobbySearchMenuString = "res://scenes/lobbysearch/LobbySearch.tscn";
	private const string SettingsSceneString = "res://scenes/settings/Settings.tscn";
	private const string HelpSceneString = "res://scenes/help/Help.tscn";

	// --- ELEMENTY UI (Exportowane do Inspektora - Ustaw je w edytorze!) ---
	[ExportGroup("Menu Buttons")]
	[Export] private Button createButton;
	[Export] private Button joinButton;
	[Export] private Button settingsButton;
	[Export] private Button helpButton;
	[Export] private Button quitButton;

	// --- MANAGERY ---
	private EOSManager eosManager;

	// --- ZMIENNE STANU ---
	private Timer animationTimer;
	private int dotCount = 0;
	private bool isCreatingLobby = false;
	private const float CreateTimeout = 5.0f;

	// --- SEKRETNE MENU ADMINA ---
	private string secretCode = "";
	private const string SecretTrigger = "kakor";
	private AcceptDialog adminPopup = null;

	public override void _Ready()
	{
		base._Ready();

		// 1. RYGORYSTYCZNE SPRAWDZANIE: Czy przyciski są przypisane?
		// Jeśli nie -> Krzyczymy błędem i przerywamy, żadnego naprawiania w tle.
		if (!AreNodesAssigned())
		{
			GD.PrintErr("❌❌❌ MainMenu CRITICAL ERROR ❌❌❌");
			GD.PrintErr("Nie przypisano przycisków w Inspektorze! Uzupełnij pola [Export].");
			GD.PrintErr("Gra nie będzie działać poprawnie bez tych referencji.");
			return; // Przerywamy działanie, nie podłączamy nic dalej
		}

		// 2. Pobierz Managera (Autoload)
		eosManager = GetNodeOrNull<EOSManager>("/root/EOSManager");

		// 3. Podłącz sygnały przycisków (teraz mamy pewność, że nie są null)
		createButton.Pressed   += OnCreateGamePressed;
		joinButton.Pressed     += OnJoinGamePressed;
		quitButton.Pressed     += OnQuitPressed;
		settingsButton.Pressed += OnSettingsPressed;
		helpButton.Pressed     += OnHelpPressed;

		// 4. Podłącz sygnał LobbyCreated
		if (eosManager != null)
		{
			eosManager.LobbyCreated += OnLobbyCreated;
		}
		else
		{
			GD.PrintErr("⚠ MainMenu: Nie znaleziono EOSManager w /root/EOSManager");
		}
	}

	// Obsługa wejścia dla sekretnego menu
	public override void _Input(InputEvent @event)
	{
		base._Input(@event);

		// Sprawdź czy to zdarzenie klawiatury
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			// Pobierz znak Unicode
			char key = (char)keyEvent.Unicode;

			// Jeśli to litera, dodaj do sekretnego kodu
			if (char.IsLetter(key))
			{
				secretCode += char.ToLower(key);

				// Ogranicz długość do 10 znaków
				if (secretCode.Length > 10)
				{
					secretCode = secretCode.Substring(secretCode.Length - 10);
				}

				// Sprawdź czy wpisano sekretny kod
				if (secretCode.EndsWith(SecretTrigger))
				{
					GD.Print("🔓 Secret admin menu triggered!");
					ShowAdminMenu();
					secretCode = ""; // Resetuj kod
				}
			}
		}
	}

	// Metoda walidująca przypisania
	private bool AreNodesAssigned()
	{
		// Sprawdzamy czy którykolwiek jest nullem
		bool missing = createButton == null || 
					   joinButton == null || 
					   quitButton == null || 
					   settingsButton == null || 
					   helpButton == null;

		return !missing;
	}

	private void OnCreateGamePressed()
	{
		if (isCreatingLobby) return;

		GD.Print("Creating lobby in background...");

		if (eosManager != null && !string.IsNullOrEmpty(eosManager.currentLobbyId))
		{
			GD.Print("🚪 Leaving lobby before creating a new one...");
			eosManager.LeaveLobby();
		}

		StartCreatingAnimation();

		if (eosManager != null)
		{
			string lobbyId = GenerateLobbyIDCode();
			eosManager.CreateLobby(lobbyId, 10, true);
		}
	}

	private void OnLobbyCreated(string lobbyId)
	{
		GD.Print($"✅ Lobby created: {lobbyId}, changing scene...");

		StopCreatingAnimation();

		GetTree().CreateTimer(0.5).Timeout += () =>
		{
			GetTree().ChangeSceneToFile(LobbyMenuString);
		};
	}

	private void StartCreatingAnimation()
	{
		isCreatingLobby = true;
		createButton.Disabled = true;
		dotCount = 0;

		float originalHeight = createButton.Size.Y;
		createButton.CustomMinimumSize = new Vector2(0, originalHeight);

		animationTimer = new Timer();
		animationTimer.WaitTime = 0.5;
		animationTimer.Timeout += OnAnimationTimerTimeout;
		AddChild(animationTimer);
		animationTimer.Start();

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
		createButton.Text = "Utwórz grę"; // Upewnij się, że ten tekst pasuje do Twojego UI
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
		dotCount = (dotCount + 1) % 4;
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

	// --- LOGIKA ADMIN MENU ---

	private void ShowAdminMenu()
	{
		// Zamknij poprzedni popup jeśli istnieje
		if (adminPopup != null)
		{
			adminPopup.QueueFree();
			adminPopup = null;
		}

		// Pobierz obecne Device ID
		string currentDeviceId = eosManager != null ? eosManager.GetCurrentDeviceId() : "N/A";

		// Utwórz popup
		adminPopup = new AcceptDialog();
		adminPopup.Title = "🔧 Menu Admina";
		adminPopup.OkButtonText = "Zamknij";
		adminPopup.DialogText = "";

		// Utwórz kontener dla zawartości
		VBoxContainer content = new VBoxContainer();
		content.AddThemeConstantOverride("separation", 10);

		// Label z tytułem
		Label titleLabel = new Label();
		titleLabel.Text = "Sekretne Menu Admina";
		titleLabel.AddThemeColorOverride("font_color", new Color(0, 1, 0.8f));
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		content.AddChild(titleLabel);

		// Separator
		HSeparator separator1 = new HSeparator();
		content.AddChild(separator1);

		// Label z Device ID
		Label deviceIdLabel = new Label();
		deviceIdLabel.Text = "Obecne Device ID:";
		content.AddChild(deviceIdLabel);

		// TextEdit z Device ID (tylko do odczytu)
		TextEdit deviceIdText = new TextEdit();
		deviceIdText.Text = currentDeviceId;
		deviceIdText.Editable = false;
		deviceIdText.CustomMinimumSize = new Vector2(400, 60);
		deviceIdText.WrapMode = TextEdit.LineWrappingMode.Boundary;
		content.AddChild(deviceIdText);

		// Separator
		HSeparator separator2 = new HSeparator();
		content.AddChild(separator2);

		// Przycisk do resetowania Device ID
		Button resetButton = new Button();
		resetButton.Text = "🔄 Resetuj Device ID";
		resetButton.CustomMinimumSize = new Vector2(0, 40);
		resetButton.Pressed += () =>
		{
			GD.Print("🔄 Resetting Device ID from admin menu...");
			if (eosManager != null)
			{
				eosManager.ResetDeviceId();

				// Zaktualizuj wyświetlane ID po krótkiej chwili
				GetTree().CreateTimer(0.5).Timeout += () =>
				{
					string newDeviceId = eosManager.GetCurrentDeviceId();
					deviceIdText.Text = newDeviceId;
					GD.Print($"✅ New Device ID: {newDeviceId}");
				};
			}
		};
		content.AddChild(resetButton);

		// Ostrzeżenie
		Label warningLabel = new Label();
		warningLabel.Text = "⚠️ Resetowanie Device ID wymaga ponownego logowania!";
		warningLabel.AddThemeColorOverride("font_color", new Color(1, 0.5f, 0));
		warningLabel.HorizontalAlignment = HorizontalAlignment.Center;
		warningLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(warningLabel);

		// Dodaj zawartość do popupu
		adminPopup.AddChild(content);

		// Wyświetl popup
		GetTree().Root.AddChild(adminPopup);
		adminPopup.PopupCentered();

		GD.Print($"📋 Admin menu opened. Current Device ID: {currentDeviceId}");
	}

	public override void _ExitTree()
	{
		base._ExitTree();

		if (eosManager != null)
		{
			eosManager.LobbyCreated -= OnLobbyCreated;
		}

		if (animationTimer != null)
		{
			animationTimer.QueueFree();
		}
	}
}
