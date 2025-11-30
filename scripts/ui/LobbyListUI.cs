using Godot;

public partial class LobbyListUI : VBoxContainer
{
	private EOSManager eosManager;

	// Scena dla pojedynczego elementu lobby (utworzymy ją programatically)
	private PackedScene lobbyItemScene;

	public override void _Ready()
	{
		base._Ready();

		eosManager = GetNode<EOSManager>("/root/EOSManager");

		// Podłącz sygnały z EOSManager
		eosManager.LobbyListUpdated += OnLobbyListUpdated;

		GD.Print("LobbyListUI ready and listening for lobby updates");
		GD.Print("🦊 Nicknames are now auto-generated from animal list! OwO");
	}

	private void OnLobbyListUpdated(Godot.Collections.Array<Godot.Collections.Dictionary> lobbies)
	{
		GD.Print($"Updating lobby list UI with {lobbies.Count} lobbies");

		// Wyczyść obecną listę
		ClearLobbyList();

		// Dodaj każde lobby do listy
		foreach (var lobbyData in lobbies)
		{
			AddLobbyItem(lobbyData);
		}
	}

	private void ClearLobbyList()
	{
		// Usuń wszystkie dzieci (teraz nie ma już nickname UI ^w^)
		var children = GetChildren();

		foreach (var child in children)
		{
			child.QueueFree();
		}
	}

	private void AddLobbyItem(Godot.Collections.Dictionary lobbyData)
	{
		// Utwórz kontener dla lobby item
		var lobbyItemContainer = new HBoxContainer();
		lobbyItemContainer.SetAnchorsPreset(Control.LayoutPreset.TopWide);

		// Informacje o lobby
		int index = (int)lobbyData["index"];
		string lobbyId = (string)lobbyData["lobbyId"];
		int currentPlayers = (int)lobbyData["currentPlayers"];
		int maxPlayers = (int)lobbyData["maxPlayers"];

		// Label z informacjami
		var lobbyInfoLabel = new Label();
		lobbyInfoLabel.Text = $"Lobby #{index + 1} - Players: {currentPlayers}/{maxPlayers}";
		lobbyInfoLabel.CustomMinimumSize = new Vector2(300, 0);
		lobbyItemContainer.AddChild(lobbyInfoLabel);

		// Przycisk Join
		var lobbyJoinButton = new Button();
		lobbyJoinButton.Text = "Join";
		lobbyJoinButton.CustomMinimumSize = new Vector2(100, 40);

		// Podłącz akcję join
		lobbyJoinButton.Pressed += () => OnJoinButtonPressed(index, lobbyId);

		lobbyItemContainer.AddChild(lobbyJoinButton);

		// Dodaj separator
		var lobbySeparator = new HSeparator();

		// Dodaj do listy
		AddChild(lobbyItemContainer);
		AddChild(lobbySeparator);
	}

	private void OnJoinButtonPressed(int index, string lobbyId)
	{
		GD.Print($"Joining lobby at index {index}: {lobbyId}");
		eosManager.JoinLobbyByIndex(index);
	}

	public override void _ExitTree()
	{
		base._ExitTree();

		// Odłącz sygnał
		if (eosManager != null)
		{
			eosManager.LobbyListUpdated -= OnLobbyListUpdated;
		}
	}
}
