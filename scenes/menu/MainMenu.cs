using Godot;
using System;

public partial class MainMenu : Node
{
	// --- NAPRAWIONE ŚCIEŻKI (Tego brakowało!) ---
	private const string LobbyMenuString = "res://scenes/lobby/Lobby.tscn";
	private const string LobbySearchMenuString = "res://scenes/lobbysearch/LobbySearch.tscn";

	// SettingsSceneString i HelpSceneString usunęliśmy celowo,
	// bo teraz używamy Overlay (nakładek), a nie zmiany sceny.

	// --- ELEMENTY UI ---
	[ExportGroup("Menu Buttons")]
	[Export] private Button createButton;
	[Export] private Button joinButton;
	[Export] private Button settingsButton;
	[Export] private Button helpButton;
	[Export] private Button quitButton;

	// --- REFERENCJE DO NAKŁADEK (OVERLAYS) ---
	[ExportGroup("Overlays")]
	[Export] private NodePath settingsMenuNodePath; // Tu przypniesz Settings.tscn w Inspektorze
	[Export] private NodePath helpMenuNodePath;     // Tu przypniesz Help.tscn (jeśli masz)
	private Control settingsMenuNode; // Tu przypniesz Settings.tscn w Inspektorze
	private Control helpMenuNode;     // Tu przypniesz Help.tscn (jeśli masz)

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

	private void OnCreateGamePressed()
	{
		if (isCreatingLobby) return;
		if (eosManager != null && !string.IsNullOrEmpty(eosManager.currentLobbyId)) eosManager.LeaveLobby();
		StartCreatingAnimation();
		if (eosManager != null) eosManager.CreateLobby(GenerateLobbyIDCode(), 10, true);
	}

	private void OnLobbyCreated(string lobbyId)
	{
		StopCreatingAnimation();
		// Tutaj używamy LobbyMenuString - teraz już zadziała, bo jest zdefiniowany na górze
		GetTree().CreateTimer(0.5).Timeout += () => GetTree().ChangeSceneToFile(LobbyMenuString);
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
		timeoutTimer.Timeout += () => { StopCreatingAnimation(); };
		AddChild(timeoutTimer);
		timeoutTimer.Start();
		createButton.Text = "Tworzenie";
	}

	private void StopCreatingAnimation()
	{
		isCreatingLobby = false;
		createButton.Disabled = false;
		createButton.Text = "Utwórz grę";
		createButton.CustomMinimumSize = new Vector2(0, 0);
		if (animationTimer != null) { animationTimer.Stop(); animationTimer.QueueFree(); animationTimer = null; }
	}

	private void OnAnimationTimerTimeout()
	{
		dotCount = (dotCount + 1) % 4;
		createButton.Text = "Tworzenie" + new string('.', dotCount);
	}

	private string GenerateLobbyIDCode()
	{
		const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
		var random = new Random();
		char[] code = new char[6];
		for (int i = 0; i < 6; i++) code[i] = chars[random.Next(chars.Length)];
		return new string(code);
	}

	private void OnJoinGamePressed()
	{
		// Tutaj używamy LobbySearchMenuString - teraz zadziała
		GetTree().ChangeSceneToFile(LobbySearchMenuString);
	}

	private void OnQuitPressed()
	{
		GetTree().Quit();
	}

	// --- OBSŁUGA PRZYCISKU SETTINGS (OVERLAY) ---
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

	private void OnHelpPressed()
	{
		GD.Print("Opening Help overlay...");
		if (helpMenuNode != null)
		{
			helpMenuNode.Visible = true;
		}
	}

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
