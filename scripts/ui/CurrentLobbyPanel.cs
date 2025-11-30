using Godot;

/// <summary>
/// Panel wyświetlający informacje o obecnym lobby (gdy jesteś hostem lub członkiem)
/// </summary>
public partial class CurrentLobbyPanel : VBoxContainer
{
	private Label statusLabel;
	private Label lobbyIdLabel;
	private Label playersLabel;
	private VBoxContainer membersListContainer;
	private Button leaveButton;

	private EOSManager eosManager;

	public override void _Ready()
	{
		base._Ready();

		// Pobierz EOSManager
		eosManager = GetNode<EOSManager>("/root/EOSManager");

		// Stwórz UI
		CreateUI();

		// Połącz sygnały
		eosManager.CurrentLobbyInfoUpdated += OnCurrentLobbyInfoUpdated;
		eosManager.LobbyMembersUpdated += OnLobbyMembersUpdated;

		// Ukryj panel na start
		Visible = false;
	}

	private void CreateUI()
	{
		// Status label (np. "Hostujesz lobby" lub "Jesteś w lobby")
		statusLabel = new Label();
		statusLabel.AddThemeColorOverride("font_color", new Color(0.2f, 1f, 0.2f)); // Zielony
		AddChild(statusLabel);

		// Lobby ID label
		lobbyIdLabel = new Label();
		lobbyIdLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 1f)); // Jasnoniebieski
		AddChild(lobbyIdLabel);

		// Players count label
		playersLabel = new Label();
		AddChild(playersLabel);

		// Separator
		var sep1 = new HSeparator();
		AddChild(sep1);

		// Label "Gracze w lobby:"
		var membersHeaderLabel = new Label();
		membersHeaderLabel.Text = "Gracze w lobby:";
		membersHeaderLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 0.5f)); // Żółty
		AddChild(membersHeaderLabel);

		// Kontener na listę graczy
		membersListContainer = new VBoxContainer();
		AddChild(membersListContainer);

		// Separator
		var sep2 = new HSeparator();
		AddChild(sep2);

		// Leave button
		leaveButton = new Button();
		leaveButton.Text = "Opuść Lobby";
		leaveButton.Pressed += OnLeaveButtonPressed;
		AddChild(leaveButton);
	}

	private void OnCurrentLobbyInfoUpdated(string lobbyId, int currentPlayers, int maxPlayers, bool isOwner)
	{
		// Pokaż panel
		Visible = true;

		// Ustaw status
		if (isOwner)
		{
			statusLabel.Text = "🏠 Hostujesz lobby";
		}
		else
		{
			statusLabel.Text = "👥 Jesteś w lobby";
		}

		// Ustaw ID lobby
		lobbyIdLabel.Text = $"ID Lobby: {lobbyId}";

		// Ustaw licznik graczy
		playersLabel.Text = $"Gracze: {currentPlayers}/{maxPlayers}";

		GD.Print($"📺 Current lobby panel updated: {statusLabel.Text}, {currentPlayers}/{maxPlayers}");
	}

	private void OnLobbyMembersUpdated(Godot.Collections.Array<Godot.Collections.Dictionary> members)
	{
		// Wyczyść obecną listę
		foreach (Node child in membersListContainer.GetChildren())
		{
			child.QueueFree();
		}

		GD.Print($"👥 Updating members list: {members.Count} members");

		// Sprawdź czy jesteśmy hostem
		bool weAreHost = eosManager.isLobbyOwner;

		// Dodaj każdego członka
		foreach (var memberData in members)
		{
			string displayName = (string)memberData["displayName"];
			bool isOwner = (bool)memberData["isOwner"];
			bool isLocalPlayer = (bool)memberData["isLocalPlayer"];
			string userId = (string)memberData["userId"];

			GD.Print($"  📝 Creating member entry: {displayName}, isOwner={isOwner}, isLocal={isLocalPlayer}, weAreHost={weAreHost}");

			// Stwórz kontener dla gracza (potrzebny do detekcji kliknięcia)
			var memberContainer = new PanelContainer();
			memberContainer.CustomMinimumSize = new Vector2(0, 30); // Minimalna wysokość żeby był klikalny!
			memberContainer.SetMeta("userId", userId);
			memberContainer.SetMeta("isLocalPlayer", isLocalPlayer);

			// Dodaj padding
			var marginContainer = new MarginContainer();
			marginContainer.AddThemeConstantOverride("margin_left", 5);
			marginContainer.AddThemeConstantOverride("margin_right", 5);
			marginContainer.AddThemeConstantOverride("margin_top", 2);
			marginContainer.AddThemeConstantOverride("margin_bottom", 2);
			marginContainer.MouseFilter = Control.MouseFilterEnum.Ignore; // Pozwól kontenerowi rodzica złapać klik
			memberContainer.AddChild(marginContainer);

			// Stwórz label dla gracza
			var memberLabel = new Label();
			memberLabel.MouseFilter = Control.MouseFilterEnum.Ignore;

			// Ikona + nazwa
			string icon = isOwner ? "👑" : "👤";
			string nameText = displayName;

			// Jeśli to ty
			if (isLocalPlayer)
			{
				nameText += " (TY)";
			}

			memberLabel.Text = $"{icon} {nameText}";

			// Kolor: host = złoty, ty = zielony, inni = biały
			if (isOwner)
			{
				memberLabel.AddThemeColorOverride("font_color", new Color(1f, 0.84f, 0f)); // Złoty
			}
			else if (isLocalPlayer)
			{
				memberLabel.AddThemeColorOverride("font_color", new Color(0.2f, 1f, 0.2f)); // Zielony
			}

			marginContainer.AddChild(memberLabel);

			// Jeśli jesteśmy hostem i to nie my, dodaj detekcję prawego kliknięcia! >:3
			if (weAreHost && !isLocalPlayer)
			{
				GD.Print($"    ✅ Adding right-click handler for {displayName}");
				memberContainer.MouseFilter = Control.MouseFilterEnum.Stop; // Włącz detekcję myszy
				memberContainer.GuiInput += (inputEvent) => OnMemberGuiInput(inputEvent, userId, displayName);
			}
			else
			{
				memberContainer.MouseFilter = Control.MouseFilterEnum.Ignore; // Nieaktywny dla nie-hosta
			}

			membersListContainer.AddChild(memberContainer);
		}
	}

	private void OnMemberGuiInput(InputEvent @event, string userId, string displayName)
	{
		GD.Print($"⚙️ GUI Input received for {displayName}: {@event.GetType().Name}");

		if (@event is InputEventMouseButton mouseEvent)
		{
			GD.Print($"  🖘️ Mouse button: {mouseEvent.ButtonIndex}, Pressed: {mouseEvent.Pressed}");

			if (mouseEvent.ButtonIndex == MouseButton.Right && mouseEvent.Pressed)
			{
				GD.Print($"🖱️ Right-clicked on player: {displayName} ({userId})");
				ShowKickPopup(userId, displayName, mouseEvent.GlobalPosition);
			}
		}
	}

	private void ShowKickPopup(string userId, string displayName, Vector2 position)
	{
		// Stwórz PopupMenu
		var popup = new PopupMenu();
		popup.AddItem($"👢 Wyrzuc {displayName}", 0);
		popup.IndexPressed += (index) =>
		{
			if (index == 0)
			{
				GD.Print($"👢 Kicking player: {displayName}");
				eosManager.KickPlayer(userId);
			}
			popup.QueueFree();
		};

		// Dodaj do drzewa i pokaż w miejscu kliknięcia
		AddChild(popup);
		Vector2 mousePos = GetViewport().GetMousePosition();
		popup.Position = (Vector2I)mousePos;
		popup.Popup();
	}

	private void OnLeaveButtonPressed()
	{
		GD.Print("🚪 Leave button pressed");
		eosManager.LeaveLobby();

		// Ukryj panel
		Visible = false;
	}

	public override void _ExitTree()
	{
		base._ExitTree();

		// Odłącz sygnały
		if (eosManager != null)
		{
			eosManager.CurrentLobbyInfoUpdated -= OnCurrentLobbyInfoUpdated;
			eosManager.LobbyMembersUpdated -= OnLobbyMembersUpdated;
		}
	}
}
