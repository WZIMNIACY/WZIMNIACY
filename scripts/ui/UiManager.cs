using Godot;

public partial class UiManager : Node
{
	private EOSManager eosManager;
	private CurrentLobbyPanel currentLobbyPanel;
	private Button createLobbyButton;

	public override void _Ready()
	{
		base._Ready();

		eosManager = ((EOSManager)GetNode("/root/EOSManager"));

		// Pobierz referencję do przycisku Create Lobby (scena LobbyCreate)
		var parent = GetParent();
		if (parent is Control control)
		{
			createLobbyButton = control.GetNodeOrNull<Button>("CreateLobby");
		}
	}

	public void OnCreateLobbyButtonPressed()
	{
		// Zablokuj przycisk podczas tworzenia
		if (createLobbyButton != null)
		{
			createLobbyButton.Disabled = true;
			createLobbyButton.Text = "Creating...";
		}

		eosManager.CreateLobby("Moje Lobby", 4, true);
	}

	public void OnJoinLobbyButtonPressed()
	{
		// Wyszukaj lobby - lista zostanie zaktualizowana przez sygnał LobbyListUpdated
		eosManager.SearchLobbies();
	}

	// Nowa funkcja do dołączania po indeksie
	public void JoinFirstLobby()
	{
		eosManager.JoinLobbyByIndex(0); // Dołącz do pierwszego lobby z listy
	}

	// Callback gdy lobby zostało utworzone
	private void OnLobbyCreated(string lobbyId)
	{
		GD.Print($"[UI] Lobby created: {lobbyId}");

		// Odblokuj przycisk i przywróć tekst
		if (createLobbyButton != null)
		{
			createLobbyButton.Disabled = false;
			createLobbyButton.Text = "create lobby";
		}
	}

	// Callback gdy tworzenie lobby się nie powiodło
	private void OnLobbyCreationFailed(string errorMessage)
	{
		GD.PrintRich($"[color=yellow][UI] Lobby creation failed: {errorMessage}");

		// Odblokuj przycisk i przywróć tekst
		if (createLobbyButton != null)
		{
			createLobbyButton.Disabled = false;
			createLobbyButton.Text = "create lobby";
		}
	}

	// Callback gdy dołączono do lobby
	private void OnLobbyJoined(string lobbyId)
	{
		GD.Print($"[UI] Joined lobby: {lobbyId}");
		// Możesz tu np. przejść do lobby scene
	}

	public override void _ExitTree()
	{
		base._ExitTree();

		if (eosManager != null)
		{
			eosManager.LobbyCreated -= OnLobbyCreated;
			eosManager.LobbyJoined -= OnLobbyJoined;
			eosManager.LobbyCreationFailed -= OnLobbyCreationFailed;
		}
	}
}

