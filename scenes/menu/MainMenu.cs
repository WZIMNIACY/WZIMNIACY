using Godot;
using System;

public partial class MainMenu : Node
{
	private const string LobbyMenuString = "res://scenes/lobby/Lobby.tscn";
	private const string LobbySearchMenuString = "res://scenes/lobbysearch/LobbySearch.tscn";
	private const string SettingsSceneString = "res://scenes/settings/Settings.tscn";
	private const string HelpSceneString = "res://scenes/help/Help.tscn";

	// --- ELEMENTY UI (Exportowane do Inspektora) ---
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

	public override void _Ready()
	{
		base._Ready();

		// 1. Sprawdź czy przyciski są przypisane w Inspektorze
		if (!AreNodesAssigned())
		{
			GD.PrintErr("❌ MainMenu: Nie przypisano przycisków w Inspektorze!");
			return;
		}

		// 2. Pobierz Managera (Autoload - tego nie eksportujemy, bo jest w /root)
		eosManager = GetNodeOrNull<EOSManager>("/root/EOSManager");

		// 3. Podłącz sygnały
		createButton.Pressed   += OnCreateGamePressed;
		joinButton.Pressed     += OnJoinGamePressed;
		quitButton.Pressed     += OnQuitPressed;
		settingsButton.Pressed += OnSettingsPressed;
		helpButton.Pressed     += OnHelpPressed;

		// Podłącz sygnał LobbyCreated
		if (eosManager != null)
		{
			eosManager.LobbyCreated += OnLobbyCreated;
		}
		else
		{
			GD.PrintErr("⚠ MainMenu: Nie znaleziono EOSManager w /root/EOSManager");
		}
	}

	// Metoda walidująca przypisania
	private bool AreNodesAssigned()
	{
		return createButton != null && 
			   joinButton != null && 
			   quitButton != null && 
			   settingsButton != null && 
			   helpButton != null;
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
		createButton.Text = "Utwórz grę";
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
