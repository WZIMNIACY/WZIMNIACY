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
	[Export] private Button _createButton;
	[Export] private Button _joinButton;
	[Export] private Button _settingsButton;
	[Export] private Button _helpButton;
	[Export] private Button _quitButton;

	// --- MANAGERY ---
	private EOSManager _eosManager;

	// --- ZMIENNE STANU ---
	private Timer _animationTimer;
	private int _dotCount = 0;
	private bool _isCreatingLobby = false;
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
		_eosManager = GetNodeOrNull<EOSManager>("/root/EOSManager");

		// 3. Podłącz sygnały
		_createButton.Pressed   += OnCreateGamePressed;
		_joinButton.Pressed     += OnJoinGamePressed;
		_quitButton.Pressed     += OnQuitPressed;
		_settingsButton.Pressed += OnSettingsPressed;
		_helpButton.Pressed     += OnHelpPressed;

		// Podłącz sygnał LobbyCreated
		if (_eosManager != null)
		{
			_eosManager.LobbyCreated += OnLobbyCreated;
		}
		else
		{
			GD.PrintErr("⚠ MainMenu: Nie znaleziono EOSManager w /root/EOSManager");
		}
	}

	// Metoda walidująca przypisania
	private bool AreNodesAssigned()
	{
		return _createButton != null && 
			   _joinButton != null && 
			   _quitButton != null && 
			   _settingsButton != null && 
			   _helpButton != null;
	}

	private void OnCreateGamePressed()
	{
		if (_isCreatingLobby) return;

		GD.Print("Creating lobby in background...");

		if (_eosManager != null && !string.IsNullOrEmpty(_eosManager.currentLobbyId))
		{
			GD.Print("🚪 Leaving lobby before creating a new one...");
			_eosManager.LeaveLobby();
		}

		StartCreatingAnimation();

		if (_eosManager != null)
		{
			string lobbyId = GenerateLobbyIDCode();
			_eosManager.CreateLobby(lobbyId, 10, true);
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
		_isCreatingLobby = true;
		_createButton.Disabled = true;
		_dotCount = 0;

		float originalHeight = _createButton.Size.Y;
		_createButton.CustomMinimumSize = new Vector2(0, originalHeight);

		_animationTimer = new Timer();
		_animationTimer.WaitTime = 0.5;
		_animationTimer.Timeout += OnAnimationTimerTimeout;
		AddChild(_animationTimer);
		_animationTimer.Start();

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

		_createButton.Text = "Tworzenie";
	}

	private void StopCreatingAnimation()
	{
		_isCreatingLobby = false;
		_createButton.Disabled = false;
		_createButton.Text = "Utwórz grę";
		_createButton.CustomMinimumSize = new Vector2(0, 0);

		if (_animationTimer != null)
		{
			_animationTimer.Stop();
			_animationTimer.QueueFree();
			_animationTimer = null;
		}
	}

	private void OnAnimationTimerTimeout()
	{
		_dotCount = (_dotCount + 1) % 4;
		string dots = new string('.', _dotCount);
		_createButton.Text = "Tworzenie" + dots;
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

		if (_eosManager != null)
		{
			_eosManager.LobbyCreated -= OnLobbyCreated;
		}

		if (_animationTimer != null)
		{
			_animationTimer.QueueFree();
		}
	}
}
