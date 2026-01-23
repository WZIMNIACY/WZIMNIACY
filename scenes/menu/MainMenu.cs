using Godot;
using System;

/// <summary>
/// Manages the main menu of the game, handling navigation, UI interactions, and overlay menus.
/// </summary>
public partial class MainMenu : Node
{
	// --- NAPRAWIONE ŚCIEŻKI (Tego brakowało!) ---
	/// <summary>
	/// Path to the Lobby scene.
	/// </summary>
	private const string LobbyMenuString = "res://scenes/lobby/Lobby.tscn";

	/// <summary>
	/// Path to the Lobby Search scene.
	/// </summary>
	private const string LobbySearchMenuString = "res://scenes/lobbysearch/LobbySearch.tscn";

	// SettingsSceneString i HelpSceneString usunęliśmy celowo,
	// bo teraz używamy Overlay (nakładek), a nie zmiany sceny.

	// --- ELEMENTY UI ---
	/// <summary>
	/// Button to create a new game lobby.
	/// </summary>
	[ExportGroup("Menu Buttons")]
	[Export] private Button createButton;

	/// <summary>
	/// Button to join an existing game lobby.
	/// </summary>
	[Export] private Button joinButton;

	/// <summary>
	/// Button to open the settings menu.
	/// </summary>
	[Export] private Button settingsButton;

	/// <summary>
	/// Button to open the help menu.
	/// </summary>
	[Export] private Button helpButton;

	/// <summary>
	/// Button to quit the application.
	/// </summary>
	[Export] private Button quitButton;

	// --- REFERENCJE DO NAKŁADEK (OVERLAYS) ---
	/// <summary>
	/// Node path to the Settings menu overlay (Settings.tscn).
	/// </summary>
	[ExportGroup("Overlays")]
	[Export] private NodePath settingsMenuNodePath; // Tu przypniesz Settings.tscn w Inspektorze

	/// <summary>
	/// Node path to the Help menu overlay (Help.tscn).
	/// </summary>
	[Export] private NodePath helpMenuNodePath;     // Tu przypniesz Help.tscn (jeśli masz)

	/// <summary>
	/// Reference to the Settings menu control node.
	/// </summary>
	private Control settingsMenuNode; // Tu przypniesz Settings.tscn w Inspektorze

	/// <summary>
	/// Reference to the Help menu control node.
	/// </summary>
	private Control helpMenuNode;     // Tu przypniesz Help.tscn (jeśli masz)

	// --- MANAGERY ---
	/// <summary>
	/// Reference to the EOS (Epic Online Services) Manager.
	/// </summary>
	private EOSManager eosManager;

	// --- ZMIENNE STANU ---
	/// <summary>
	/// Timer used for the creating lobby animation loops.
	/// </summary>
	private Timer animationTimer;

	/// <summary>
	/// Counter for the number of dots in the creating lobby animation text.
	/// </summary>
	private int dotCount = 0;

	/// <summary>
	/// Flag indicating if a lobby is currently being created.
	/// </summary>
	private bool isCreatingLobby = false;

	/// <summary>
	/// Timeout in seconds before identifying a lobby creation failure/timeout.
	/// </summary>
	private const float CreateTimeout = 5.0f;

	// --- SEKRETNE MENU ADMINA ---
	/// <summary>
	/// Buffer to store keystrokes for detecting secret codes.
	/// </summary>
	private string secretCode = "";

	/// <summary>
	/// The secret code needed to trigger the admin menu ("kakor").
	/// </summary>
	private const string SecretTrigger = "kakor";

	/// <summary>
	/// Reference to the admin popup dialog.
	/// </summary>
	private AcceptDialog adminPopup = null;

	public override void _Ready()
	{
		base._Ready();

        settingsMenuNode = GetNode<Control>(settingsMenuNodePath);
        helpMenuNode = GetNode<Control>(helpMenuNodePath);

		// 1. Walidacja - sprawdzamy też settingsMenuNode
		if (!AreNodesAssigned())
		{
			GD.PrintErr("❌ MainMenu ERROR: Nie przypisano przycisków lub okienek (Settings/Help) w Inspektorze!");
			return;
		}

		// 2. Ukrywamy nakładki na start (żeby nie zasłaniały menu)
		if (settingsMenuNode != null) settingsMenuNode.Visible = false;
		if (helpMenuNode != null)     helpMenuNode.Visible = false;

		// 3. Pobieramy managera
		eosManager = GetNodeOrNull<EOSManager>("/root/EOSManager");

		// 4. Podłączamy sygnały
		createButton.Pressed   += OnCreateGamePressed;
		joinButton.Pressed     += OnJoinGamePressed;
		quitButton.Pressed     += OnQuitPressed;
		settingsButton.Pressed += OnSettingsPressed;
		helpButton.Pressed     += OnHelpPressed;

		if (eosManager != null)
		{
			eosManager.LobbyCreated += OnLobbyCreated;
		}
	}

	public override void _Input(InputEvent @event)
	{
		base._Input(@event);

		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			char key = (char)keyEvent.Unicode;
			if (char.IsLetter(key))
			{
				secretCode += char.ToLower(key);
				if (secretCode.Length > 10) secretCode = secretCode.Substring(secretCode.Length - 10);
				if (secretCode.EndsWith(SecretTrigger))
				{
					ShowAdminMenu();
					secretCode = "";
				}
			}
		}
	}

	/// <summary>
	/// Checks if all necessary nodes and references are assigned.
	/// </summary>
	/// <returns>True if all required nodes are present, otherwise false.</returns>
	private bool AreNodesAssigned()
	{
		// Sprawdzamy czy przypisano Settings w Inspektorze
		bool missing = createButton == null ||
					   joinButton == null ||
					   quitButton == null ||
					   settingsButton == null ||
					   helpButton == null ||
					   settingsMenuNode == null; // <--- Ważne!

		return !missing;
	}

	/// <summary>
	/// Handles the "Create Game" button press event.
	/// Initiates the lobby creation process and animation.
	/// </summary>
	private void OnCreateGamePressed()
	{
		if (isCreatingLobby) return;
		if (eosManager != null && !string.IsNullOrEmpty(eosManager.currentLobbyId)) eosManager.LeaveLobby();
		StartCreatingAnimation();
		if (eosManager != null) eosManager.CreateLobby(GenerateLobbyIDCode(), 10, true);
	}

	/// <summary>
	/// Callback invoked when a lobby is successfully created.
	/// Transitions the scene to the lobby menu.
	/// </summary>
	/// <param name="lobbyId">The ID of the created lobby.</param>
	private void OnLobbyCreated(string lobbyId)
	{
		StopCreatingAnimation();
		// Tutaj używamy LobbyMenuString - teraz już zadziała, bo jest zdefiniowany na górze
		GetTree().CreateTimer(0.5).Timeout += () => GetTree().ChangeSceneToFile(LobbyMenuString);
	}

	/// <summary>
	/// Starts the UI animation indicating that a lobby is being created.
	/// Disables the create button and starts a timer.
	/// </summary>
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
		timeoutTimer.Timeout += () => { StopCreatingAnimation(); };
		AddChild(timeoutTimer);
		timeoutTimer.Start();
		createButton.Text = "Tworzenie";
	}

	/// <summary>
	/// Stops the lobby creation UI animation and resets the create button state.
	/// </summary>
	private void StopCreatingAnimation()
	{
		isCreatingLobby = false;
		createButton.Disabled = false;
		createButton.Text = "Utwórz grę";
		createButton.CustomMinimumSize = new Vector2(0, 0);
		if (animationTimer != null) { animationTimer.Stop(); animationTimer.QueueFree(); animationTimer = null; }
	}

	/// <summary>
	/// Callback for the animation timer timeout.
	/// Updates the "Creating..." text with animating dots.
	/// </summary>
	private void OnAnimationTimerTimeout()
	{
		dotCount = (dotCount + 1) % 4;
		createButton.Text = "Tworzenie" + new string('.', dotCount);
	}

	/// <summary>
	/// Generates a random 6-character alphanumeric code for the lobby ID.
	/// </summary>
	/// <returns>A random lobby ID string.</returns>
	private string GenerateLobbyIDCode()
	{
		const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
		var random = new Random();
		char[] code = new char[6];
		for (int i = 0; i < 6; i++) code[i] = chars[random.Next(chars.Length)];
		return new string(code);
	}

	/// <summary>
	/// Handles the "Join Game" button press event.
	/// Transitions the scene to the lobby search menu.
	/// </summary>
	private void OnJoinGamePressed()
	{
		// Tutaj używamy LobbySearchMenuString - teraz zadziała
		GetTree().ChangeSceneToFile(LobbySearchMenuString);
	}

	/// <summary>
	/// Handles the "Quit" button press event.
	/// Exits the application.
	/// </summary>
	private void OnQuitPressed()
	{
		GetTree().Quit();
	}

	// --- OBSŁUGA PRZYCISKU SETTINGS (OVERLAY) ---
	/// <summary>
	/// Handles the "Settings" button press event.
	/// Opens the Settings overlay menu.
	/// </summary>
	private void OnSettingsPressed()
	{
		GD.Print("Opening Settings overlay...");
		if (settingsMenuNode != null)
		{
			settingsMenuNode.Visible = true;
			// Settings jest teraz "na wierzchu" (Overlay)
		}
		else
		{
			GD.PrintErr("Nie przypisano SettingsMenuNode w Inspektorze!");
		}
	}

	/// <summary>
	/// Handles the "Help" button press event.
	/// Opens the Help overlay menu.
	/// </summary>
	private void OnHelpPressed()
	{
		GD.Print("Opening Help overlay...");
		if (helpMenuNode != null)
		{
			helpMenuNode.Visible = true;
		}
	}

	/// <summary>
	/// Displays the secret admin menu popup.
	/// Allows viewing the device ID and resetting it.
	/// </summary>
	private void ShowAdminMenu()
	{
		if (adminPopup != null) { adminPopup.QueueFree(); adminPopup = null; }
		string currentDeviceId = eosManager != null ? eosManager.GetCurrentDeviceId() : "N/A";
		adminPopup = new AcceptDialog();
		adminPopup.Title = "🔧 Menu Admina";

		VBoxContainer content = new VBoxContainer();
		Label l = new Label(); l.Text = "Sekretne Menu Admina"; content.AddChild(l);
		TextEdit t = new TextEdit(); t.Text = currentDeviceId; content.AddChild(t);
		Button b = new Button(); b.Text = "Resetuj ID";
		b.Pressed += () => { if(eosManager!=null) eosManager.ResetDeviceId(); };
		content.AddChild(b);

		adminPopup.AddChild(content);
		GetTree().Root.AddChild(adminPopup);
		adminPopup.PopupCentered();
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		if (eosManager != null) eosManager.LobbyCreated -= OnLobbyCreated;
		if (animationTimer != null) animationTimer.QueueFree();
	}
}
