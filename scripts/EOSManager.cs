using Godot;
using System;
using System.Linq;
using System.ComponentModel;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.Platform;
using Epic.OnlineServices.Logging;
using Epic.OnlineServices.Auth;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Lobby;

public partial class EOSManager : Node
{
	// Sygnały dla UI
	[Signal]
	public delegate void LobbyListUpdatedEventHandler(Godot.Collections.Array<Godot.Collections.Dictionary> lobbies);

	[Signal]
	public delegate void LobbyJoinedEventHandler(string lobbyId);

	[Signal]
	public delegate void LobbyJoinFailedEventHandler(string errorMessage);

	[Signal]
	public delegate void LobbyCreatedEventHandler(string lobbyId);

	[Signal]
	public delegate void LobbyCreationFailedEventHandler(string errorMessage);

	[Signal]
	public delegate void LobbyLeftEventHandler();

	[Signal]
	public delegate void CurrentLobbyInfoUpdatedEventHandler(string lobbyId, int currentPlayers, int maxPlayers, bool isOwner);

	[Signal]
	public delegate void LobbyMembersUpdatedEventHandler(Godot.Collections.Array<Godot.Collections.Dictionary> members);

	[Signal]
	public delegate void CustomLobbyIdUpdatedEventHandler(string customLobbyId);

	[Signal]
	public delegate void GameModeUpdatedEventHandler(string gameMode);
	[Signal]
	public delegate void AITypeUpdatedEventHandler(string aiType);

	[Signal]
	public delegate void CheckTeamsBalanceConditionsEventHandler();

	[Signal]
	public delegate void LobbyReadyStatusUpdatedEventHandler(bool isReady);

	// Stałe konfiguracyjne
	private const int MinNicknameLength = 2;
	private const int MaxNicknameLength = 20;
	private const int MaxNicknameGenerationAttempts = 10;
	private const int UserIdDisplayLength = 8;
	private const int RandomSuffixMax = 10000;
	private const int NicknameRandomMax = 99;
	private const int FallbackAnimalRandomMax = 9999;

	// Dane produktu
	private string productName = "WZIMniacy";
	private string productVersion = "1.0";

	// Dane uwierzytelniające EOS
	private string productId = "e0fad88fbfc147ddabce0900095c4f7b";
	private string sandboxId = "ce451c8e18ef4cb3bc7c5cdc11a9aaae";
	private string clientId = "xyza7891eEYHFtDWNZaFlmauAplnUo5H";
	private string clientSecret = "xD8rxykYUyqoaGoYZ5zhK+FD6Kg8+LvkATNkDb/7DPo";
	private string deploymentId = "0e28b5f3257a4dbca04ea0ca1c30f265";

	// Referencje do EOS
	private PlatformInterface platformInterface;
	private AuthInterface authInterface;
	private ConnectInterface connectInterface;
	private LobbyInterface lobbyInterface;

	// ID użytkownika - dla P2P używamy ProductUserId (Connect), dla Epic Account używamy EpicAccountId (Auth)
	private ProductUserId localProductUserId;  // P2P/Connect ID
	public string localProductUserIdString
	{
		get { return localProductUserId.ToString(); }
		set { localProductUserId = ProductUserId.FromString(value); }
	}  // P2P/Connect ID
	private EpicAccountId localEpicAccountId;  // Epic Account ID

	// Przechowywanie znalezionych lobby
	private System.Collections.Generic.List<string> foundLobbyIds = new System.Collections.Generic.List<string>();
	private System.Collections.Generic.Dictionary<string, LobbyDetails> foundLobbyDetails = new System.Collections.Generic.Dictionary<string, LobbyDetails>();

	// Obecne lobby w którym jesteśmy
	public string currentLobbyId = null;
	public bool isLobbyOwner = false;

	// Custom Lobby ID
	public string currentCustomLobbyId = "";

	// Current Game Mode (tryb gry) i AI Type
	public GameMode currentGameMode = GameMode.AIMaster;
	public AIType currentAIType = AIType.API;

	// Aktualna lista członków lobby (cache)
	private Godot.Collections.Array<Godot.Collections.Dictionary> currentLobbyMembers = new Godot.Collections.Array<Godot.Collections.Dictionary>();

	// Prefiks atrybutu lobby służącego do wymuszania drużyn przez hosta
	private const string ForceTeamAttributePrefix = "ForceTeam_";
	private System.Collections.Generic.Dictionary<string, Team> forcedTeamAssignments = new System.Collections.Generic.Dictionary<string, Team>(StringComparer.OrdinalIgnoreCase);

	// Prefiks atrybutu lobby służącego do przechowywania poprzednich drużyn (przed przejściem do Universal)
	private const string PreviousTeamAttributePrefix = "PreviousTeam_";
	private System.Collections.Generic.Dictionary<string, Team> previousTeamAssignments = new System.Collections.Generic.Dictionary<string, Team>(StringComparer.OrdinalIgnoreCase);

	// Nickname ustawiony PRZED wejściem do lobby
	private string pendingNickname = "";

	// Lista zwierzaków wczytana z pliku >w<
	private System.Collections.Generic.List<string> animalNames = new System.Collections.Generic.List<string>();    // Flaga blokująca tworzenie lobby
	private bool isCreatingLobby = false;

	// Kolejkowanie atrybutów lobby - zbieranie zmian i wysyłanie razem
	private System.Collections.Generic.Dictionary<string, string> pendingLobbyAttributes = new System.Collections.Generic.Dictionary<string, string>();
	private System.Collections.Generic.HashSet<string> attributesToRemove = new System.Collections.Generic.HashSet<string>();
	private SceneTreeTimer attributeBatchTimer = null;
	private const float AttributeBatchDelay = 0.1f;

	// Timer do odświeżania lobby
	private Timer lobbyRefreshTimer;
	//Limit graczy w drużynie
	private const int MaxPlayersPerTeam = 5;

	// Enum dla drużyn
	public enum Team
	{
		[Description("None")]
		None,
		[Description("Blue")]
		Blue,
		[Description("Red")]
		Red,
		[Description("Universal")]
		Universal
	}

	// Enum dla trybów gry
	public enum GameMode
	{
		[Description("AI Master")]
		AIMaster,
		[Description("AI vs Human")]
		AIvsHuman
	}

	// Enum dla typów AI
	public enum AIType
	{
		[Description("API")]
		API,
		[Description("Local LLM")]
		LocalLLM
	}

	// Metody do konwersji enum <-> string
	public static string GetEnumDescription(System.Enum value)
	{
		var field = value.GetType().GetField(value.ToString());
		var attribute = (DescriptionAttribute)System.Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute));
		return attribute?.Description ?? value.ToString();
	}

	public static T ParseEnumFromDescription<T>(string description, T defaultValue) where T : System.Enum
	{
		foreach (var field in typeof(T).GetFields())
		{
			if (System.Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) is DescriptionAttribute attribute)
			{
				if (attribute.Description == description)
					return (T)field.GetValue(null);
			}
		}
		return defaultValue;
	}

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		base._Ready();

		// Opcjonalne opóźnienie sieci (do testów)
		// uzycie: --delay-networking=value_in_ms dla kazdej instancji w cmdline
		var args = OS.GetCmdlineArgs();
		string delayArg = args.FirstOrDefault(s => s.StartsWith("--delay-networking="));
		if (delayArg != null)
		{
			string delayValue = delayArg.Substring("--delay-networking=".Length);
			if (int.TryParse(delayValue, out int delayMs))
			{
				if (delayMs > 0)
				{
					OS.DelayMsec(delayMs);
				}
			}
		}

		GD.Print("=== Starting EOS Initialization ===");

		// Wczytaj listę zwierzaków z pliku ^w^
		LoadAnimalNames();

		// Krok 1: Inicjalizacja SDK
		var initializeOptions = new InitializeOptions()
		{
			ProductName = productName,
			ProductVersion = productVersion,
		};

		GD.Print($"Product: {productName} v{productVersion}");

		var initializeResult = PlatformInterface.Initialize(ref initializeOptions);
		if (initializeResult != Result.Success)
		{
			GD.PrintErr("Failed to initialize EOS SDK: " + initializeResult);
			return;
		}

		GD.Print("✅ EOS SDK initialized successfully.");

		// Krok 2: Konfiguracja logowania
		LoggingInterface.SetLogLevel(LogCategory.AllCategories, LogLevel.VeryVerbose);
		LoggingInterface.SetCallback((ref LogMessage logMessage) =>
		{
			GD.Print($"[EOS {logMessage.Category}] {logMessage.Message}");
		});

		GD.Print("✅ Logging configured.");

		// Krok 3: Utworzenie platformy (PlatformHandle)
		var createOptions = new Options()
		{
			ProductId = productId,
			SandboxId = sandboxId,
			ClientCredentials = new ClientCredentials()
			{
				ClientId = clientId,
				ClientSecret = clientSecret
			},
			DeploymentId = deploymentId,
			IsServer = false,
			EncryptionKey = null,
			OverrideCountryCode = null,
			OverrideLocaleCode = null,
			Flags = PlatformFlags.DisableOverlay | PlatformFlags.LoadingInEditor
		};

		GD.Print($"Creating platform with ProductId: {productId}");
		GD.Print($"Sandbox: {sandboxId}, Deployment: {deploymentId}");

		platformInterface = PlatformInterface.Create(ref createOptions);
		if (platformInterface == null)
		{
			GD.PrintErr("❌ Failed to create EOS Platform Interface!");
			return;
		}

		GD.Print("✅ EOS Platform Interface created successfully.");

		// Pobierz Auth Interface
		authInterface = platformInterface.GetAuthInterface();
		if (authInterface == null)
		{
			GD.PrintErr("Failed to get Auth Interface!");
			return;
		}

		// Pobierz Connect Interface (P2P, bez wymagania konta Epic)
		connectInterface = platformInterface.GetConnectInterface();
		if (connectInterface == null)
		{
			GD.PrintErr("Failed to get Connect Interface!");
			return;
		}

		// Pobierz Lobby Interface
		lobbyInterface = platformInterface.GetLobbyInterface();
		if (lobbyInterface == null)
		{
			GD.PrintErr("Failed to get Lobby Interface!");
			return;
		}

		// Dodaj nasłuchiwanie na zmiany w lobby (update członków)
		AddLobbyUpdateNotifications();

		// Stwórz timer do periodycznego odświeżania lobby
		CreateLobbyRefreshTimer();

		// USUWAMY ISTNIEJĄCY DEVICEID ŻEBY MÓGŁ STWORZYĆ FAKTYCZNIE NOWY, IDK CZY TO ABY NA PEWNO DZIAŁA PRAWIDŁOWO
		// W PRZYPADKU TESTÓW NA JEDNYM URZĄDZENIU, ale na nie pozwala chyba także yippee
		GD.Print("Deleting DeviceId...");

		var deleteDeviceIdOptions = new DeleteDeviceIdOptions();

		connectInterface.DeleteDeviceId(ref deleteDeviceIdOptions, null, (ref DeleteDeviceIdCallbackInfo data) =>
		{
			if (data.ResultCode == Result.Success)
			{
				GD.Print("Successfully deleted existing DeviceId, DeviceId login will get a new one");
				LoginWithDeviceId_P2P();
			}
			else if (data.ResultCode == Result.NotFound)
			{
				GD.Print("DeviceId for deletion was not found, DeviceId login will get a new one");
				LoginWithDeviceId_P2P();
			}
			else
			{
				GD.PrintErr("Unexpected error while deleting existing DeviceId, DeviceId login will not be called," + " Result: " + (int)data.ResultCode + ":" + data.ResultCode);
			}
		});

		// Krok 4: Logowanie P2P (anonimowe, bez konta Epic)
		// LoginWithDeviceId_P2P();

		// Krok 4: Logowanie P2P (anonimowe, bez konta Epic)
		// LoginWithDeviceId_P2P();
	}

	private void HandleKickedFromLobby()
	{
		GD.Print("🚪 Player was kicked from lobby - cleaning up and returning to main menu...");

		// Pokaż popup z informacją o wyrzuceniu
		if (GetTree() != null && GetTree().Root != null)
		{
			var popup = new AcceptDialog();
			popup.DialogText = "Zostałeś wyrzucony przez hosta!";
			popup.Title = "Wyrzucony";
			popup.OkButtonText = "OK";

			// Zamknij popup i wróć do menu po kliknięciu OK
			popup.Confirmed += () =>
			{
				popup.QueueFree();
				if (GetTree() != null)
				{
					GetTree().ChangeSceneToFile("res://scenes/menu/main.tscn");
				}
			};

			// Dodaj do root i wyświetl
			GetTree().Root.AddChild(popup);
			popup.PopupCentered();
		}

		// Zatrzymaj timer odświeżania jeśli jeszcze działa
		if (lobbyRefreshTimer != null && lobbyRefreshTimer.TimeLeft > 0)
		{
			lobbyRefreshTimer.Stop();
			GD.Print("🛑 Lobby refresh timer stopped (kicked)");
		}

		// NIE wywołujemy LeaveLobby() - serwer EOS już zamknął połączenie websocket
		// Bezpośrednio czyścimy lokalny stan (tak jak robi OnLeaveLobbyComplete)

		// Wyczyść obecne lobby
		currentLobbyId = null;
		isLobbyOwner = false;

		// Wyczyść CustomLobbyId
		currentCustomLobbyId = "";
		EmitSignal(SignalName.CustomLobbyIdUpdated, "");

		// Wyczyść GameMode
		currentGameMode = GameMode.AIMaster;
		EmitSignal(SignalName.GameModeUpdated, GetEnumDescription(currentGameMode));

		// Wyczyść AIType
		currentAIType = AIType.API;
		EmitSignal(SignalName.AITypeUpdated, GetEnumDescription(currentAIType));

		// Wyczyść cache członków
		currentLobbyMembers.Clear();

		// Wyczyść flagę tworzenia
		isCreatingLobby = false;

		// Wyczyść wymuszone przypisania drużyn
		forcedTeamAssignments.Clear();

		// Wyślij sygnał do UI
		EmitSignal(SignalName.LobbyLeft);
	}

	private void CreateLobbyRefreshTimer()
	{
		// USUNIĘTE: Automatyczne odświeżanie co 3 sekundy
		// Powód: SearchLobbies() zwraca LobbyDetails z pustymi UserID członków
		// Co powoduje błąd "Invalid member UserID!" i znikanie listy graczy
		// Zamiast tego używamy:
		// 1. Notyfikacji EOS (OnLobbyMemberUpdateReceived) - automatyczne aktualizacje gdy ktoś dołączy/wyjdzie
		// 2. Ręczne odświeżanie gdy użytkownik kliknie "Refresh" lub "Join"

		GD.Print("✅ Lobby notifications enabled (auto-refresh timer disabled)");
	}

	private void OnLobbyRefreshTimeout()
	{
		// WYŁĄCZONE - patrz komentarz w CreateLobbyRefreshTimer()
	}

	// Logowanie przez Device ID (Developer Tool - tylko do testów!)
	private void LoginWithDeviceId()
	{
		GD.Print("Starting Developer Auth login...");

		// UWAGA: Developer Auth wymaga Client Policy = "Trusted Server" w Epic Dev Portal
		// Alternatywnie można użyć AccountPortal (otwiera przeglądarkę)

		// Dla Developer Auth:
		// Id = localhost:port (adres DevAuthTool)
		// Token = nazwa użytkownika
		string devToolHost = "localhost:8080";
		string userName = "TestUser1";

		var loginOptions = new Epic.OnlineServices.Auth.LoginOptions()
		{
			Credentials = new Epic.OnlineServices.Auth.Credentials()
			{
				Type = LoginCredentialType.Developer,
				Id = devToolHost,     // Host:Port DevAuthTool
				Token = userName       // Nazwa użytkownika
			},
			ScopeFlags = AuthScopeFlags.BasicProfile | AuthScopeFlags.FriendsList | AuthScopeFlags.Presence
		};

		GD.Print($"Attempting Developer Auth login with DevTool at: {devToolHost}, User: {userName}");
		GD.Print("NOTE: Developer Auth requires Client Policy = 'Trusted Server' in Epic Dev Portal!");
		authInterface.Login(ref loginOptions, null, OnLoginComplete);
	}

	// Logowanie przez Account Portal (otwiera przeglądarkę Epic)
	private void LoginWithAccountPortal()
	{
		GD.Print("Starting Account Portal login...");

		var loginOptions = new Epic.OnlineServices.Auth.LoginOptions()
		{
			Credentials = new Epic.OnlineServices.Auth.Credentials()
			{
				Type = LoginCredentialType.AccountPortal,
				Id = null,
				Token = null
			},
			ScopeFlags = AuthScopeFlags.BasicProfile | AuthScopeFlags.FriendsList | AuthScopeFlags.Presence
		};

		GD.Print("Opening Epic Account login in browser...");
		authInterface.Login(ref loginOptions, null, OnLoginComplete);
	}

	// Logowanie przez Persistent Auth (używa zapamiętanych danych)
	private void LoginWithPersistentAuth()
	{
		GD.Print("Starting Persistent Auth login...");
		GD.Print("Trying to login with cached credentials...");

		var loginOptions = new Epic.OnlineServices.Auth.LoginOptions()
		{
			Credentials = new Epic.OnlineServices.Auth.Credentials()
			{
				Type = LoginCredentialType.PersistentAuth,
				Id = null,
				Token = null
			},
			ScopeFlags = AuthScopeFlags.BasicProfile | AuthScopeFlags.FriendsList | AuthScopeFlags.Presence
		};

		authInterface.Login(ref loginOptions, null, OnLoginComplete);
	}

	// ============================================
	// LOGOWANIE P2P (BEZ KONTA EPIC) - DeviceID
	// ============================================


	private void LoginWithDeviceId_P2P()
	{
		GD.Print("🎮 Starting P2P login (no Epic account required)...");

		// ON TEGO NIGDZIE NIE UŻYWA NAWET ._.
		// Generuj unikalny DeviceID dla tego urządzenia
		string deviceId = GetOrCreateDeviceId();
		GD.Print($"Device ID: {deviceId}");

		var createDeviceIdOptions = new CreateDeviceIdOptions()
		{
			DeviceModel = "PC"
		};

		// Najpierw utwórz DeviceID w systemie EOS
		connectInterface.CreateDeviceId(ref createDeviceIdOptions, null, (ref CreateDeviceIdCallbackInfo data) =>
		{
			if (data.ResultCode == Result.Success || data.ResultCode == Result.DuplicateNotAllowed)
			{
				// DeviceID istnieje lub został utworzony - teraz zaloguj się
				GD.Print("✅ DeviceID ready, logging in...");

				// WAŻNE: Dla DeviceidAccessToken, Token MUSI być null!
				var loginOptions = new Epic.OnlineServices.Connect.LoginOptions()
				{
					Credentials = new Epic.OnlineServices.Connect.Credentials()
					{
						Type = ExternalCredentialType.DeviceidAccessToken,
						Token = null  // MUSI być null dla DeviceID!
					},
					UserLoginInfo = new UserLoginInfo()
					{
						DisplayName = $"Player_{System.Environment.UserName}"
					}
				};

				connectInterface.Login(ref loginOptions, null, OnConnectLoginComplete);
			}
			else
			{
				GD.PrintErr($"❌ Failed to create DeviceID: {data.ResultCode}");
			}
		});
	}

	// Callback dla Connect Login (P2P)
	private void OnConnectLoginComplete(ref Epic.OnlineServices.Connect.LoginCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print($"✅ P2P Login successful! ProductUser ID: {data.LocalUserId}");
			localProductUserId = data.LocalUserId;

			// Gotowe do tworzenia lobby!
			GD.Print("🎮 Ready to create/join lobbies!");

			// Teraz możesz wywołać funkcje lobby
			// Przykład: CreateLobby("MyLobby", 4);
		}
		else
		{
			GD.PrintErr($"❌ P2P Login failed: {data.ResultCode}");
		}
	}

	// Generuj lub odczytaj DeviceID
	private string GetOrCreateDeviceId()
	{
		// Dla testowania wielu instancji na tym samym PC, dodaj losowy suffix
		// W produkcji możesz użyć tylko OS.GetUniqueId()
		string computerName = System.Environment.MachineName;
		string userName = System.Environment.UserName;
		string baseId = OS.GetUniqueId();

		// Dodaj losowy suffix żeby każda instancja miała unikalny ID
		int randomSuffix = (int)(GD.Randi() % RandomSuffixMax);

		return $"{computerName}_{userName}_{baseId}_{randomSuffix}";
	}

	// Callback po zakończeniu logowania
	private void OnLoginComplete(ref Epic.OnlineServices.Auth.LoginCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print($"✅ Login successful! User ID: {data.LocalUserId}");
			localEpicAccountId = data.LocalUserId;

			// Pobierz dodatkowe informacje o użytkowniku
			var copyUserAuthTokenOptions = new CopyUserAuthTokenOptions();
			Result result = authInterface.CopyUserAuthToken(ref copyUserAuthTokenOptions, data.LocalUserId, out Epic.OnlineServices.Auth.Token? authToken);

			if (result == Result.Success && authToken.HasValue)
			{
				GD.Print($"Account ID: {authToken.Value.AccountId}");
			}
		}
		else if (data.ResultCode == Result.InvalidUser)
		{
			// Brak zapisanych danych - przejdź na AccountPortal
			GD.Print($"⚠️ PersistentAuth failed ({data.ResultCode}), trying AccountPortal...");
			LoginWithAccountPortal();
		}
		else
		{
			GD.PrintErr($"❌ Login failed: {data.ResultCode}");
		}
	}

	// Pobierz informacje o zalogowanym użytkowniku
	private void GetUserInfo()
	{
		if (localEpicAccountId == null || !localEpicAccountId.IsValid())
		{
			GD.PrintErr("No valid user ID!");
			return;
		}

		var copyOptions = new CopyUserAuthTokenOptions();
		var result = authInterface.CopyUserAuthToken(ref copyOptions, localEpicAccountId, out var authToken);

		if (result == Result.Success && authToken != null)
		{
			GD.Print("=== User Info ===");
			GD.Print($"Account ID: {localEpicAccountId}");
			GD.Print($"App: {authToken?.App}");
			GD.Print($"Client ID: {authToken?.ClientId}");
			GD.Print("================");
		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		base._Process(delta);

		// Krok 4: Tick platformy - musi być wywoływany regularnie
		if (platformInterface != null)
		{
			platformInterface.Tick();
		}
	}

	// Cleanup przy zamykaniu
	public override void _ExitTree()
	{
		base._ExitTree();

		// Wyloguj użytkownika przed zamknięciem (jeśli używamy Auth)
		if (authInterface != null && localEpicAccountId != null && localEpicAccountId.IsValid())
		{
			GD.Print("Logging out user...");
			var logoutOptions = new Epic.OnlineServices.Auth.LogoutOptions()
			{
				LocalUserId = localEpicAccountId
			};
			authInterface.Logout(ref logoutOptions, null, OnLogoutComplete);
		}

		if (platformInterface != null)
		{
			GD.Print("Releasing EOS Platform Interface...");
			platformInterface.Release();
			platformInterface = null;
		}

		PlatformInterface.Shutdown();
		GD.Print("EOS SDK shutdown complete.");
	}

	// Callback po wylogowaniu
	private void OnLogoutComplete(ref Epic.OnlineServices.Auth.LogoutCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print("✅ Logout successful!");
			localEpicAccountId = null;
		}
		else
		{
			GD.PrintErr($"❌ Logout failed: {data.ResultCode}");
		}
	}

	// ============================================
	// UTILITY METHODS
	// ============================================

	/// <summary>
	/// Sprawdza czy użytkownik jest zalogowany do EOS
	/// </summary>
	public bool IsLoggedIn()
	{
		return localProductUserId != null && localProductUserId.IsValid();
	}

	// ============================================
	// NICKNAME MANAGEMENT
	// ============================================

	/// <summary>
	/// Wczytuje listę zwierzaków z pliku Animals.txt
	/// </summary>
	private void LoadAnimalNames()
	{
		string filePath = "res://assets/nicknames/Animals_Old.txt";

		if (!FileAccess.FileExists(filePath))
		{
			GD.PrintErr($"❌ Nie znaleziono pliku ze zwierzakami: {filePath}");
			return;
		}

		using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			GD.PrintErr($"❌ Nie można otworzyć pliku: {filePath}");
			return;
		}

		animalNames.Clear();
		while (!file.EofReached())
		{
			string line = file.GetLine().Trim();
			if (!string.IsNullOrEmpty(line))
			{
				animalNames.Add(line);
			}
		}

		GD.Print($"🦊 Wczytano {animalNames.Count} zwierzaków z listy! OwO");
	}

	/// <summary>
	/// Losuje unikalny nick zwierzaka (sprawdza duplikaty w lobby) ^w^
	/// </summary>
	private string GenerateUniqueAnimalNickname()
	{
		if (animalNames.Count == 0)
		{
			GD.PrintErr("❌ Brak listy zwierzaków! Używam fallback...");
			return $"Animal_{GD.Randi() % FallbackAnimalRandomMax}";
		}

		// Pobierz listę już zajętych nicków
		var usedNicknames = new System.Collections.Generic.HashSet<string>();
		foreach (var member in currentLobbyMembers)
		{
			if (member.ContainsKey("displayName"))
			{
				usedNicknames.Add(member["displayName"].ToString());
			}
		}

		// Próbuj wylosować unikalny nick (max 10 prób)
		for (int attempt = 0; attempt < MaxNicknameGenerationAttempts; attempt++)
		{
			string randomAnimal = animalNames[(int)(GD.Randi() % animalNames.Count)];

			if (!usedNicknames.Contains(randomAnimal))
			{
				GD.Print($"🎲 Wylosowano zwierzaka: {randomAnimal} (próba {attempt + 1}) >w<");
				return randomAnimal;
			}
		}

		// Jeśli wszystkie próby się nie powiodły, dodaj losowy sufiks
		string fallbackAnimal = animalNames[(int)(GD.Randi() % animalNames.Count)];
		string uniqueNick = $"{fallbackAnimal}_{GD.Randi() % NicknameRandomMax}";
		GD.Print($"⚠️ Nie udało się wylosować unikalnego, używam: {uniqueNick}");
		return uniqueNick;
	}

	/// <summary>
	/// Skraca userId do ostatnich N znaków dla czytelności logów
	/// </summary>
	private string GetShortUserId(string userId)
	{
		if (string.IsNullOrEmpty(userId)) return "null";
		return userId.Length <= UserIdDisplayLength
			? userId
			: userId.Substring(Math.Max(0, userId.Length - UserIdDisplayLength));
	}

	/// <summary>
	/// Ustawia nickname który będzie użyty przy dołączeniu/utworzeniu lobby
	/// </summary>
	/// <param name="nickname">Nickname gracza (2-20 znaków)</param>
	public void SetPendingNickname(string nickname)
	{
		if (string.IsNullOrWhiteSpace(nickname))
		{
			GD.Print("⚠️ Nickname is empty, will use fallback");
			pendingNickname = "";
			return;
		}

		// Sanitizacja
		nickname = nickname.Trim();
		if (nickname.Length < MinNicknameLength) nickname = nickname.PadRight(MinNicknameLength, '_');
		if (nickname.Length > MaxNicknameLength) nickname = nickname.Substring(0, MaxNicknameLength);

		// Filtruj znaki (zostaw tylko litery, cyfry, _, -)
		char[] filtered = Array.FindAll(nickname.ToCharArray(), c => char.IsLetterOrDigit(c) || c == '_' || c == '-');
		string sanitized = new string(filtered);

		if (string.IsNullOrEmpty(sanitized))
		{
			GD.Print("⚠️ Nickname contains only invalid characters, will use fallback");
			pendingNickname = "";
			return;
		}

		pendingNickname = sanitized;
		GD.Print($"✅ Pending nickname set to: {pendingNickname}");
	}

	/// <summary>
	/// Zwraca aktualnie ustawiony pending nickname (dla UI)
	/// </summary>
	public string GetPendingNickname()
	{
		return pendingNickname;
	}

	// ============================================
	// SYSTEM LOBBY - Tworzenie, wyszukiwanie, dołączanie
	// ============================================

	/// <summary>
	/// Tworzy nowe lobby
	/// </summary>
	/// <param name="customLobbyId"> kod lobby do wyszukiwania (np. "V5CGSP")</param>
	/// <param name="maxPlayers">Maksymalna liczba graczy (2-64)</param>
	/// <param name="isPublic">Czy lobby jest publiczne (można wyszukać)?</param>
	public void CreateLobby(string customLobbyId, uint maxPlayers = 10, bool isPublic = true)
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("❌ Cannot create lobby: User not logged in!");
			EmitSignal(SignalName.LobbyCreationFailed, "User not logged in");
			return;
		}

		// Sprawdź czy użytkownik już jest w lobby
		if (!string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintRich("[color=yellow]❌ Cannot create lobby: You are already in a lobby!");
			GD.PrintRich($"[color=yellow]   Current lobby: {currentLobbyId} (Owner: {isLobbyOwner})");
			GD.PrintRich("[color=yellow]   Please leave the current lobby first.");
			EmitSignal(SignalName.LobbyCreationFailed, "Already in a lobby");
			return;
		}

		// NOWE: Sprawdź czy lobby już jest tworzone
		if (isCreatingLobby)
		{
			GD.PrintErr("❌ Cannot create lobby: Lobby creation already in progress!");
			EmitSignal(SignalName.LobbyCreationFailed, "Lobby creation already in progress");
			return;
		}

		// Zapisz custom lobby ID
		currentCustomLobbyId = customLobbyId;
		GD.Print($"🏗️ Creating lobby with custom ID: {customLobbyId}, Max players: {maxPlayers}, Public: {isPublic}");

		// Zablokuj tworzenie lobby
		isCreatingLobby = true;

		// Automatycznie wygeneruj unikalny nick zwierzaka! ^w^
		pendingNickname = GenerateUniqueAnimalNickname();
		GD.Print($"🦊 Twój nick: {pendingNickname}");

		var createLobbyOptions = new CreateLobbyOptions()
		{
			LocalUserId = localProductUserId,
			MaxLobbyMembers = maxPlayers,
			PermissionLevel = isPublic ? LobbyPermissionLevel.Publicadvertised : LobbyPermissionLevel.Inviteonly,
			PresenceEnabled = false, // Wyłączamy presence (nie potrzebujemy Epic Friends)
			AllowInvites = true,
			BucketId = "DefaultBucket", // Bucket do filtrowania lobby
			DisableHostMigration = false,
			EnableRTCRoom = false // Wyłączamy voice chat na razie
		};

		lobbyInterface.CreateLobby(ref createLobbyOptions, null, OnCreateLobbyComplete);
	}

	/// <summary>
	/// Pobiera wszystkie atrybuty lobby
	/// </summary>
	/// <returns>Dictionary z kluczami i wartościami atrybutów</returns>
	public Godot.Collections.Dictionary GetAllLobbyAttributes()
	{
		var attributes = new Godot.Collections.Dictionary();

		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("❌ Cannot get lobby attributes: Not in any lobby!");
			return attributes;
		}

		if (!foundLobbyDetails.ContainsKey(currentLobbyId))
		{
			GD.PrintErr($"❌ Lobby details not found for ID: {currentLobbyId}");
			return attributes;
		}

		LobbyDetails lobbyDetails = foundLobbyDetails[currentLobbyId];

		if (lobbyDetails == null)
		{
			GD.PrintErr("❌ Lobby details is null!");
			return attributes;
		}

		// Pobierz liczbę atrybutów
		var countOptions = new LobbyDetailsGetAttributeCountOptions();
		uint attributeCount = lobbyDetails.GetAttributeCount(ref countOptions);

		GD.Print($"📋 Getting {attributeCount} lobby attributes...");

		// Iteruj po wszystkich atrybutach
		for (uint i = 0; i < attributeCount; i++)
		{
			var copyOptions = new LobbyDetailsCopyAttributeByIndexOptions()
			{
				AttrIndex = i
			};

			Result result = lobbyDetails.CopyAttributeByIndex(ref copyOptions, out Epic.OnlineServices.Lobby.Attribute? attribute);

			if (result == Result.Success && attribute.HasValue && attribute.Value.Data.HasValue)
			{
				string key = attribute.Value.Data.Value.Key;
				string value = attribute.Value.Data.Value.Value.AsUtf8;

				attributes[key] = value;
				GD.Print($"  [{i}] {key} = '{value}'");
			}
		}

		return attributes;
	}

	private void OnCreateLobbyComplete(ref CreateLobbyCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print($"✅ Lobby created successfully! EOS Lobby ID: {data.LobbyId}");
			GD.Print($"✅ Custom Lobby ID: {currentCustomLobbyId}");

			// Zapisz obecne lobby
			currentLobbyId = data.LobbyId.ToString();
			isLobbyOwner = true;
			// NOWE: Natychmiast skopiuj LobbyDetails handle bez wykonywania SearchLobbies()
			CacheCurrentLobbyDetailsHandle("create");

			// WAŻNE: Ustaw custom ID jako atrybut lobby (po krótkiej chwili)
			GetTree().CreateTimer(0.5).Timeout += () =>
			{
				SetLobbyAttribute("CustomLobbyId", currentCustomLobbyId);

				// Wyślij sygnał o aktualizacji CustomLobbyId
				EmitSignal(SignalName.CustomLobbyIdUpdated, currentCustomLobbyId);
			};

			// Wyślij info o obecnym lobby (1 gracz = właściciel, 10 max)

			EmitSignal(SignalName.CurrentLobbyInfoUpdated, currentLobbyId, 1, 10, true);

			// Ustaw nickname i drużynę jako member attributes
			if (!string.IsNullOrEmpty(pendingNickname))
			{
				GD.Print($"📝 Setting host nickname: {pendingNickname}");
				SetMemberAttribute("Nickname", pendingNickname);

				GetTree().CreateTimer(0.8).Timeout += () =>
				{
					GD.Print("🟡 Host assigning to Neutral team...");
					SetMemberAttribute("Team", Team.None.ToString());

					// Po ustawieniu nicku i drużyny, odśwież i DOPIERO zmień scenę
					GetTree().CreateTimer(0.8).Timeout += () =>
					{
						GetLobbyMembers();

						GetTree().CreateTimer(0.3).Timeout += () =>
						{
							GD.Print("✅ Host setup complete, emitting LobbyCreated signal");
							EmitSignal(SignalName.LobbyCreated, currentLobbyId);
						};
					};
				};
			}
			else
			{
				// Bez nicku - ustaw tylko drużynę, potem zmień scenę
				GetTree().CreateTimer(0.5).Timeout += () =>
				{
					GD.Print("🟡 Host assigning to Neutral team...");
					SetMemberAttribute("Team", Team.None.ToString());

					// Po ustawieniu drużyny, odśwież i zmień scenę
					GetTree().CreateTimer(0.8).Timeout += () =>
					{
						GetLobbyMembers();

						GetTree().CreateTimer(0.3).Timeout += () =>
						{
							GD.Print("✅ Host setup complete, emitting LobbyCreated signal");
							EmitSignal(SignalName.LobbyCreated, currentLobbyId);
						};
					};
				};
			}

			// NOWE: Wyślij pustą listę członków najpierw (z fallbackiem)
			// Bo SearchLobbies() zajmuje czas i nie znajdzie naszego lobby od razu
			var tempMembersList = new Godot.Collections.Array<Godot.Collections.Dictionary>();

			string displayName = !string.IsNullOrEmpty(pendingNickname)
			? pendingNickname
			: $"Player_{GetShortUserId(localProductUserId.ToString())}";

			var tempMemberData = new Godot.Collections.Dictionary
			{
				{ "userId", localProductUserId.ToString() },
				{ "displayName", displayName },
				{ "isOwner", true },
				{ "isLocalPlayer", true },
				{ "team", "" } // Jeszcze nie przypisany
			};
			tempMembersList.Add(tempMemberData);

			// Zapisz do cache
			currentLobbyMembers = tempMembersList;

			EmitSignal(SignalName.LobbyMembersUpdated, tempMembersList);
			GD.Print($"👥 Sent initial member list (1 member - you)");
		}
		else
		{
			GD.PrintErr($"❌ Failed to create lobby: {data.ResultCode}");

			// Wyślij sygnał o błędzie do UI
			EmitSignal(SignalName.LobbyCreationFailed, data.ResultCode.ToString());
		}

		// NOWE: Odblokuj tworzenie lobby (niezależnie od sukcesu czy błędu)
		isCreatingLobby = false;
	}

	/// <summary>
	/// Wyszukuje dostępne lobby
	/// </summary>
	public void SearchLobbies()
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("❌ Cannot search lobbies: User not logged in!");
			return;
		}

		GD.Print("🔍 Searching for lobbies...");

		// Utwórz wyszukiwanie
		var createLobbySearchOptions = new CreateLobbySearchOptions()
		{
			MaxResults = 25 // Maksymalnie 25 wyników
		};

		Result result = lobbyInterface.CreateLobbySearch(ref createLobbySearchOptions, out LobbySearch lobbySearch);

		if (result != Result.Success || lobbySearch == null)
		{
			GD.PrintErr($"❌ Failed to create lobby search: {result}");
			return;
		}

		// Ustaw filtr - tylko publiczne lobby
		var searchSetParameterOptions = new LobbySearchSetParameterOptions()
		{
			ComparisonOp = ComparisonOp.Equal,
			Parameter = new AttributeData()
			{
				Key = "bucket",
				Value = new AttributeDataValue() { AsUtf8 = "DefaultBucket" }
			}
		};

		lobbySearch.SetParameter(ref searchSetParameterOptions);

		// Rozpocznij wyszukiwanie
		var findOptions = new LobbySearchFindOptions()
		{
			LocalUserId = localProductUserId
		};

		lobbySearch.Find(ref findOptions, null, (ref LobbySearchFindCallbackInfo findData) =>
		{
			if (findData.ResultCode == Result.Success)
			{
				var countOptions = new LobbySearchGetSearchResultCountOptions();
				uint lobbyCount = lobbySearch.GetSearchResultCount(ref countOptions);
				GD.Print($"✅ Found {lobbyCount} lobbies!");

				// Wyczyść listę przed dodaniem nowych
				foundLobbyIds.Clear();

				// Zwolnij stare LobbyDetails przed dodaniem nowych
				foreach (var details in foundLobbyDetails.Values)
				{
					details.Release();
				}
				foundLobbyDetails.Clear();

				// Lista lobby do wysłania do UI
				var lobbyList = new Godot.Collections.Array<Godot.Collections.Dictionary>();

				// Wyświetl wszystkie znalezione lobby
				for (uint i = 0; i < lobbyCount; i++)
				{
					var copyOptions = new LobbySearchCopySearchResultByIndexOptions() { LobbyIndex = i };
					Result copyResult = lobbySearch.CopySearchResultByIndex(ref copyOptions, out LobbyDetails lobbyDetails);

					if (copyResult == Result.Success && lobbyDetails != null)
					{
						var infoOptions = new LobbyDetailsCopyInfoOptions();
						lobbyDetails.CopyInfo(ref infoOptions, out LobbyDetailsInfo? info);

						if (info != null)
						{
							foundLobbyIds.Add(info.Value.LobbyId);
							foundLobbyDetails[info.Value.LobbyId] = lobbyDetails; // Zapisz LobbyDetails

							// Pobierz rzeczywistą liczbę członków z LobbyDetails
							var memberCountOptions = new LobbyDetailsGetMemberCountOptions();
							uint actualMemberCount = lobbyDetails.GetMemberCount(ref memberCountOptions);
							int currentPlayers = (int)actualMemberCount;

							GD.Print($"  [{i}] Lobby ID: {info.Value.LobbyId}, Players: {currentPlayers}/{info.Value.MaxMembers}");

							// Dodaj do listy dla UI
							var lobbyData = new Godot.Collections.Dictionary
		{
{ "index", (int)i },
{ "lobbyId", info.Value.LobbyId.ToString() },
{ "currentPlayers", currentPlayers },
{ "maxPlayers", (int)info.Value.MaxMembers },
{ "owner", info.Value.LobbyOwnerUserId?.ToString() ?? "Unknown" }
		};
							lobbyList.Add(lobbyData);
						}
						else
						{
							lobbyDetails.Release();
						}
					}
				}

				// Wyślij sygnał do UI z listą lobby
				EmitSignal(SignalName.LobbyListUpdated, lobbyList);
			}
			else
			{
				GD.PrintErr($"❌ Lobby search failed: {findData.ResultCode}");
			}

			lobbySearch.Release();
		});
	}

	/// <summary>
	/// Wyszukuje lobby po custom ID
	/// </summary>
	/// <param name="customLobbyId">Custom ID lobby do wyszukania (np. "V5CGSP")</param>
	/// <param name="onComplete">Callback wywoływany po zakończeniu (success: bool, lobbyId: string)</param>
	public void SearchLobbyByCustomId(string customLobbyId, Action<bool, string> onComplete = null)
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("❌ Cannot search lobby: User not logged in!");
			onComplete?.Invoke(false, "");
			return;
		}

		GD.Print($"🔍 Searching for lobby with custom ID: {customLobbyId}...");

		// Utwórz wyszukiwanie
		var createLobbySearchOptions = new CreateLobbySearchOptions()
		{
			MaxResults = 25
		};

		Result result = lobbyInterface.CreateLobbySearch(ref createLobbySearchOptions, out LobbySearch lobbySearch);

		if (result != Result.Success || lobbySearch == null)
		{
			GD.PrintErr($"❌ Failed to create lobby search: {result}");
			onComplete?.Invoke(false, "");
			return;
		}

		// Filtruj po custom ID
		var searchSetParameterOptions = new LobbySearchSetParameterOptions()
		{
			ComparisonOp = ComparisonOp.Equal,
			Parameter = new AttributeData()
			{
				Key = "CustomLobbyId",
				Value = new AttributeDataValue() { AsUtf8 = customLobbyId }
			}
		};

		lobbySearch.SetParameter(ref searchSetParameterOptions);

		// Rozpocznij wyszukiwanie
		var findOptions = new LobbySearchFindOptions()
		{
			LocalUserId = localProductUserId
		};

		lobbySearch.Find(ref findOptions, null, (ref LobbySearchFindCallbackInfo findData) =>
		{
			if (findData.ResultCode == Result.Success)
			{
				var countOptions = new LobbySearchGetSearchResultCountOptions();
				uint lobbyCount = lobbySearch.GetSearchResultCount(ref countOptions);
				GD.Print($"✅ Found {lobbyCount} lobby with custom ID: {customLobbyId}");

				if (lobbyCount > 0)
				{
					// Pobierz pierwsze znalezione lobby
					var copyOptions = new LobbySearchCopySearchResultByIndexOptions() { LobbyIndex = 0 };
					Result copyResult = lobbySearch.CopySearchResultByIndex(ref copyOptions, out LobbyDetails lobbyDetails);

					if (copyResult == Result.Success && lobbyDetails != null)
					{
						var infoOptions = new LobbyDetailsCopyInfoOptions();
						lobbyDetails.CopyInfo(ref infoOptions, out LobbyDetailsInfo? info);

						if (info != null)
						{
							string foundLobbyId = info.Value.LobbyId;

							// Zapisz LobbyDetails do cache
							if (!foundLobbyDetails.ContainsKey(foundLobbyId))
							{
								foundLobbyDetails[foundLobbyId] = lobbyDetails;
							}
							else
							{
								foundLobbyDetails[foundLobbyId]?.Release();
								foundLobbyDetails[foundLobbyId] = lobbyDetails;
							}

							GD.Print($"✅ Found lobby! EOS ID: {foundLobbyId}");
							onComplete?.Invoke(true, foundLobbyId);
						}
						else
						{
							lobbyDetails.Release();
							onComplete?.Invoke(false, "");
						}
					}
					else
					{
						GD.PrintErr("❌ Failed to copy lobby details");
						onComplete?.Invoke(false, "");
					}
				}
				else
				{
					GD.Print($"⚠️ No lobby found with custom ID: {customLobbyId}");
					onComplete?.Invoke(false, "");
				}
			}
			else
			{
				GD.PrintErr($"❌ Lobby search failed: {findData.ResultCode}");
				onComplete?.Invoke(false, "");
			}

			lobbySearch.Release();
		});
	}

	/// <summary>
	/// Wyszukuje i dołącza do lobby po custom ID
	/// </summary>
	/// <param name="customLobbyId">Custom ID lobby (np. "V5CGSP")</param>
	public void JoinLobbyByCustomId(string customLobbyId)
	{
		SearchLobbyByCustomId(customLobbyId, (success, lobbyId) =>
		{
			if (success && !string.IsNullOrEmpty(lobbyId))
			{
				GD.Print($"🚪 Joining lobby with custom ID: {customLobbyId}");
				JoinLobby(lobbyId);
			}
			else
			{
				GD.PrintErr($"❌ Cannot join: Lobby with custom ID '{customLobbyId}' not found!");

				// Wyślij sygnał o błędzie do UI
				EmitSignal(SignalName.LobbyJoinFailed, $"Lobby '{customLobbyId}' nie istnieje");
			}
		});
	}

	/// <summary>
	/// Dołącza do lobby po indeksie z ostatniego wyszukania
	/// </summary>
	/// <param name="lobbyIndex">Indeks lobby z listy (0, 1, 2...)</param>
	public void JoinLobbyByIndex(int lobbyIndex)
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("❌ Cannot join lobby: User not logged in!");
			return;
		}

		if (lobbyIndex < 0 || lobbyIndex >= foundLobbyIds.Count)
		{
			GD.PrintErr($"❌ Invalid lobby index: {lobbyIndex}. Found lobbies: {foundLobbyIds.Count}");
			return;
		}

		string lobbyId = foundLobbyIds[lobbyIndex];
		JoinLobby(lobbyId);
	}

	/// <summary>
	/// Dołącza do lobby po ID
	/// </summary>
	/// <param name="lobbyId">ID lobby do dołączenia</param>
	public void JoinLobby(string lobbyId)
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("❌ Cannot join lobby: User not logged in!");
			return;
		}

		// Sprawdź czy użytkownik już jest w lobby
		if (!string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("❌ Cannot join lobby: You are already in a lobby!");
			GD.PrintErr($"   Current lobby: {currentLobbyId} (Owner: {isLobbyOwner})");
			GD.PrintErr("   Please leave the current lobby first.");
			return;
		}

		if (!foundLobbyDetails.ContainsKey(lobbyId))
		{
			GD.PrintErr($"❌ Lobby details not found for ID: {lobbyId}. Search for lobbies first!");
			return;
		}

		GD.Print($"🚪 Joining lobby: {lobbyId}");

		// Automatycznie wygeneruj unikalny nick zwierzaka! ^w^
		pendingNickname = GenerateUniqueAnimalNickname();
		GD.Print($"🦊 Twój nick: {pendingNickname}");

		var joinLobbyOptions = new JoinLobbyOptions()
		{
			LobbyDetailsHandle = foundLobbyDetails[lobbyId],
			LocalUserId = localProductUserId,
			PresenceEnabled = false
		};

		lobbyInterface.JoinLobby(ref joinLobbyOptions, null, OnJoinLobbyComplete);
	}

	private void OnJoinLobbyComplete(ref JoinLobbyCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print($"✅ Successfully joined lobby: {data.LobbyId}");

			// Zapisz obecne lobby
			currentLobbyId = data.LobbyId.ToString();
			isLobbyOwner = false;

			// KROK 1: Skopiuj LobbyDetails handle natychmiast
			CacheCurrentLobbyDetailsHandle("join");

			// KROK 2: Poczekaj na synchronizację danych z backendu (0.5s zamiast 1.5s)
			GetTree().CreateTimer(0.5).Timeout += () =>
			{
				GD.Print("🔄 [STEP 1/5] Refreshing lobby info and CustomLobbyId...");

				// Odśwież handle aby mieć najświeższe dane
				CacheCurrentLobbyDetailsHandle("refresh_after_join");

				// Odśwież informacje o lobby (łącznie z CustomLobbyId)
				RefreshCurrentLobbyInfo();

				// KROK 3: Pobierz członków NAJPIERW (żeby AutoAssignMyTeam miał dane)
				GetTree().CreateTimer(0.3).Timeout += () =>
				{
					GD.Print("🔄 [STEP 2/5] Fetching current lobby members...");
					GetLobbyMembers();

					// KROK 4: Ustaw nickname i przypisz drużynę (teraz mamy już listę członków)
					GetTree().CreateTimer(0.3).Timeout += () =>
					{
						GD.Print("🔄 [STEP 3/5] Setting nickname first...");

						// Najpierw ustaw nickname (jeśli został ustawiony)
						if (!string.IsNullOrEmpty(pendingNickname))
						{
							GD.Print($"📝 Setting nickname: {pendingNickname}");
							SetMemberAttribute("Nickname", pendingNickname);

							// Odczekaj na propagację nicku, potem przypisz drużynę
							GetTree().CreateTimer(0.5).Timeout += () =>
							{
								GD.Print("🔄 [STEP 3.5/5] Now assigning to neutral team...");
								if (currentGameMode == GameMode.AIvsHuman)
								{
									AssignToUniversalTeam();
								}
								else
								{
									AssignToNeutralTeam();
								}
							};
						}
						else
						{
							if (currentGameMode == GameMode.AIvsHuman)
							{
								AssignToUniversalTeam();
							}
							else
							{
								AssignToNeutralTeam();
							}
						}

						// KROK 5: Odczekaj na propagację atrybutów, potem pobierz członków ponownie
						GetTree().CreateTimer(1.5).Timeout += () =>
						{
							GD.Print("🔄 [STEP 4/5] Refreshing members with team assignments...");
							GetLobbyMembers();

							// KROK 6: Wyślij sygnał do UI (zmień scenę)
							GetTree().CreateTimer(0.3).Timeout += () =>
							{
								GD.Print("✅ [STEP 5/5] All synchronization complete, emitting LobbyJoined signal");
								EmitSignal(SignalName.LobbyJoined, currentLobbyId);
							};
						};
					};
				};
			};

			// KROK 7: Wykonaj pełne wyszukiwanie w tle (dla synchronizacji)
			CallDeferred(nameof(SearchLobbiesAndRefresh));
		}
		else
		{
			GD.PrintErr($"❌ Failed to join lobby: {data.ResultCode}");

			// Wyślij sygnał o błędzie do UI
			string errorMessage = data.ResultCode switch
			{
				Result.InvalidParameters => "Nieprawidłowe parametry lobby",
				Result.NotFound => "Lobby nie zostało znalezione",
				Result.NoConnection => "Brak połączenia z serwerem",
				_ => $"Błąd: {data.ResultCode}"
			};

			EmitSignal(SignalName.LobbyJoinFailed, errorMessage);
		}
	}

	/// <summary>
	/// Wyszukuje lobby i odświeża info o obecnym lobby
	/// FAKTYCZNIE wykonuje LobbySearch.Find() żeby pobrać świeże dane z backendu
	/// </summary>
	private void SearchLobbiesAndRefresh()
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.Print("⚠️ Cannot refresh - no current lobby ID");
			return;
		}

		// Czekamy chwilę żeby backend zdążył zsynchronizować dane
		GetTree().CreateTimer(1.5).Timeout += () =>
		{
			GD.Print($"🔍 Searching for current lobby {currentLobbyId} to get fresh data...");

			var createLobbySearchOptions = new Epic.OnlineServices.Lobby.CreateLobbySearchOptions
			{
				MaxResults = 100
			};

			var searchResult = lobbyInterface.CreateLobbySearch(ref createLobbySearchOptions, out var lobbySearchHandle);
			if (searchResult != Epic.OnlineServices.Result.Success || lobbySearchHandle == null)
			{
				GD.PrintErr($"❌ Failed to create lobby search: {searchResult}");
				return;
			}

			// Szukaj po konkretnym LobbyId
			var setLobbyIdOptions = new Epic.OnlineServices.Lobby.LobbySearchSetLobbyIdOptions
			{
				LobbyId = currentLobbyId
			};

			var setIdResult = lobbySearchHandle.SetLobbyId(ref setLobbyIdOptions);
			if (setIdResult != Epic.OnlineServices.Result.Success)
			{
				GD.PrintErr($"❌ Failed to set lobby ID filter: {setIdResult}");
				return;
			}

			// Wykonaj search (pobiera dane z backendu!)
			var findOptions = new Epic.OnlineServices.Lobby.LobbySearchFindOptions
			{
				LocalUserId = localProductUserId
			};

			lobbySearchHandle.Find(ref findOptions, null, (ref Epic.OnlineServices.Lobby.LobbySearchFindCallbackInfo data) =>
	{
		if (data.ResultCode != Epic.OnlineServices.Result.Success)
		{
			GD.PrintErr($"❌ Lobby search failed: {data.ResultCode}");
			return;
		}

		var getSearchResultCountOptions = new Epic.OnlineServices.Lobby.LobbySearchGetSearchResultCountOptions();
		uint resultCount = lobbySearchHandle.GetSearchResultCount(ref getSearchResultCountOptions);

		if (resultCount == 0)
		{
			GD.PrintErr("❌ Current lobby not found in search results");
			return;
		}

		GD.Print($"✅ Found current lobby, getting fresh LobbyDetails handle...");

		// Pobierz ŚWIEŻY handle z wyników search
		var copyResultOptions = new Epic.OnlineServices.Lobby.LobbySearchCopySearchResultByIndexOptions
		{
			LobbyIndex = 0
		};

		var copyResult = lobbySearchHandle.CopySearchResultByIndex(ref copyResultOptions, out var freshLobbyDetails);
		if (copyResult != Epic.OnlineServices.Result.Success || freshLobbyDetails == null)
		{
			GD.PrintErr($"❌ Failed to copy search result: {copyResult}");
			return;
		}

		// ⚠️ NIE nadpisuj handle jeśli już działa!
		// Handle z WebSocket (member_update) ma pełne dane, a ten z search może być pusty
		if (!foundLobbyDetails.ContainsKey(currentLobbyId))
		{
			foundLobbyDetails[currentLobbyId] = freshLobbyDetails;
			GD.Print("✅ LobbyDetails handle added from backend!");
		}
		else
		{
			// Sprawdź czy nowy handle ma RZECZYWISTE dane (nie tylko count)
			var testOptions = new LobbyDetailsGetMemberCountOptions();
			uint newCount = freshLobbyDetails.GetMemberCount(ref testOptions);
			uint oldCount = foundLobbyDetails[currentLobbyId].GetMemberCount(ref testOptions);

			GD.Print($"   Comparing handles: Old={oldCount} members, New={newCount} members");

			// Testuj czy GetMemberByIndex działa na NOWYM handle
			bool newHandleValid = false;
			if (newCount > 0)
			{
				var testMemberOptions = new LobbyDetailsGetMemberByIndexOptions() { MemberIndex = 0 };
				ProductUserId testUserId = freshLobbyDetails.GetMemberByIndex(ref testMemberOptions);
				newHandleValid = testUserId != null && testUserId.IsValid();
				GD.Print($"   New handle validity test: UserID={(testUserId != null ? testUserId.ToString() : "NULL")} Valid={newHandleValid}");
			}

			// Tylko zamień jeśli nowy handle FAKTYCZNIE działa
			if (newHandleValid && newCount >= oldCount)
			{
				foundLobbyDetails[currentLobbyId]?.Release();
				foundLobbyDetails[currentLobbyId] = freshLobbyDetails;
				GD.Print("✅ LobbyDetails handle refreshed from backend (validated)!");
			}
			else
			{
				freshLobbyDetails?.Release();
				GD.Print("⚠️ Keeping old handle (new handle invalid or has less data)");
			}
		}

		// Teraz możemy bezpiecznie odczytać członków
		CallDeferred(nameof(RefreshCurrentLobbyInfo));
		CallDeferred(nameof(GetLobbyMembers));
	});
		};
	}

	/// <summary>
	/// Opuszcza obecne lobby
	/// </summary>
	/// <param name="lobbyId">ID lobby do opuszczenia</param>
	/// <summary>
	/// Opuszcza obecne lobby
	/// </summary>
	public void LeaveLobby()
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("❌ Cannot leave lobby: Not in any lobby!");
			return;
		}

		LeaveLobby(currentLobbyId);
	}

	/// <summary>
	/// Opuszcza lobby po ID
	/// </summary>
	public void LeaveLobby(string lobbyId)
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("❌ Cannot leave lobby: User not logged in!");
			return;
		}

		GD.Print($"🚪 Leaving lobby: {lobbyId}");

		var leaveLobbyOptions = new LeaveLobbyOptions()
		{
			LobbyId = lobbyId,
			LocalUserId = localProductUserId
		};

		lobbyInterface.LeaveLobby(ref leaveLobbyOptions, null, OnLeaveLobbyComplete);
	}

	private void OnLeaveLobbyComplete(ref LeaveLobbyCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print($"✅ Successfully left lobby: {data.LobbyId}");

			// Zatrzymaj timer
			if (lobbyRefreshTimer != null && lobbyRefreshTimer.TimeLeft > 0)
			{
				lobbyRefreshTimer.Stop();
				GD.Print("🛑 Lobby refresh timer stopped");
			}

			// Wyczyść obecne lobby
			currentLobbyId = null;
			isLobbyOwner = false;

			// Wyczyść CustomLobbyId
			currentCustomLobbyId = "";
			EmitSignal(SignalName.CustomLobbyIdUpdated, "");

			// Wyczyść GameMode
			currentGameMode = GameMode.AIMaster;
			EmitSignal(SignalName.GameModeUpdated, GetEnumDescription(currentGameMode));

			//Wyczyść AIType
			currentAIType = AIType.API;
			EmitSignal(SignalName.AITypeUpdated, GetEnumDescription(currentAIType));

			// Wyczyść cache członków
			currentLobbyMembers.Clear();            // Wyczyść flagę tworzenia (na wszelki wypadek)
			isCreatingLobby = false;
			forcedTeamAssignments.Clear();

			// Wyślij sygnał do UI
			EmitSignal(SignalName.LobbyLeft);
		}
		else
		{
			GD.PrintErr($"❌ Failed to leave lobby: {data.ResultCode}");
		}
	}

	/// <summary>
	/// Wyrzuca gracza z lobby (tylko host może to zrobić!) >:3
	/// </summary>
	public void KickPlayer(string targetUserId)
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("❌ Cannot kick player: Not in any lobby!");
			return;
		}

		if (!isLobbyOwner)
		{
			GD.PrintErr("❌ Cannot kick player: You are not the host!");
			return;
		}

		if (targetUserId == localProductUserId.ToString())
		{
			GD.PrintErr("❌ Cannot kick yourself!");
			return;
		}

		GD.Print($"👢 Kicking player: {targetUserId} from lobby {currentLobbyId}");

		var kickMemberOptions = new KickMemberOptions()
		{
			LobbyId = currentLobbyId,
			LocalUserId = localProductUserId,
			TargetUserId = ProductUserId.FromString(targetUserId)
		};

		lobbyInterface.KickMember(ref kickMemberOptions, null, OnKickMemberComplete);
	}

	private void OnKickMemberComplete(ref KickMemberCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print($"✅ Successfully kicked player from lobby: {data.LobbyId}");

			// Odśwież cache i listę członków po kicku
			GetTree().CreateTimer(0.3).Timeout += () =>
			{
				CacheCurrentLobbyDetailsHandle("after_kick");
				GetTree().CreateTimer(0.1).Timeout += () =>
				{
					GetLobbyMembers();
				};
			};
		}
		else
		{
			GD.PrintErr($"❌ Failed to kick player: {data.ResultCode}");
		}
	}

	// ============================================
	// NASŁUCHIWANIE NA ZMIANY W LOBBY
	// ============================================

	private ulong lobbyUpdateNotificationId = 0;
	private ulong lobbyMemberUpdateNotificationId = 0;
	private ulong lobbyMemberStatusNotificationId = 0;

	private void AddLobbyUpdateNotifications()
	{
		// Nasłuchuj na zmiany w lobby (np. nowy gracz dołączył)
		var addNotifyOptions = new AddNotifyLobbyUpdateReceivedOptions();
		lobbyUpdateNotificationId = lobbyInterface.AddNotifyLobbyUpdateReceived(ref addNotifyOptions, null, OnLobbyUpdateReceived);

		// Nasłuchuj na zmiany członków lobby (aktualizacje atrybutów)
		var memberUpdateOptions = new AddNotifyLobbyMemberUpdateReceivedOptions();
		lobbyMemberUpdateNotificationId = lobbyInterface.AddNotifyLobbyMemberUpdateReceived(ref memberUpdateOptions, null, OnLobbyMemberUpdateReceived);

		// Nasłuchuj na status członków (dołączenie/opuszczenie)
		var memberStatusOptions = new AddNotifyLobbyMemberStatusReceivedOptions();
		lobbyMemberStatusNotificationId = lobbyInterface.AddNotifyLobbyMemberStatusReceived(ref memberStatusOptions, null, OnLobbyMemberStatusReceived);

		GD.Print("✅ Lobby update notifications added");
	}

	private void OnLobbyUpdateReceived(ref LobbyUpdateReceivedCallbackInfo data)
	{
		GD.Print($"🔔 Lobby updated: {data.LobbyId}");

		// Jeśli to nasze lobby, odśwież info
		if (currentLobbyId == data.LobbyId.ToString())
		{
			CacheCurrentLobbyDetailsHandle("lobby_update");
			RefreshCurrentLobbyInfo();

			// Sprawdź i zastosuj wymuszone przypisania drużyn (dla nie-hostów)
			if (!isLobbyOwner)
			{
				ApplyForcedTeamAssignments();
			}
		}
	}

	private void OnLobbyMemberUpdateReceived(ref LobbyMemberUpdateReceivedCallbackInfo data)
	{
		GD.Print($"🔔 Lobby member updated in: {data.LobbyId}, User: {data.TargetUserId}");
		if (currentLobbyId != data.LobbyId.ToString()) return;

		GD.Print("  ℹ️ Member update detected - refreshing member list");

		// Odśwież LobbyDetails handle i listę członków
		CacheCurrentLobbyDetailsHandle("member_update");

		// Małe opóźnienie na synchronizację EOS
		GetTree().CreateTimer(0.2).Timeout += () =>
		{
			GetLobbyMembers();
		};
	}

	private void OnLobbyMemberStatusReceived(ref LobbyMemberStatusReceivedCallbackInfo data)
	{
		GD.Print($"🔔 Lobby member status changed in: {data.LobbyId}, User: {data.TargetUserId}, Status: {data.CurrentStatus}");

		// NAJPIERW sprawdź czy to MY zostaliśmy wyrzuceni (zanim sprawdzimy currentLobbyId!)
		if (data.CurrentStatus == LobbyMemberStatus.Kicked &&
			data.TargetUserId.ToString() == localProductUserId.ToString())
		{
			GD.Print($"  👢 You have been KICKED from the lobby!");
			CallDeferred(nameof(HandleKickedFromLobby));
			return; // Ignoruj wszystkie dalsze eventy
		}

		// Jeśli to nasze lobby (i nie zostaliśmy wyrzuceni)
		if (!string.IsNullOrEmpty(currentLobbyId) && currentLobbyId == data.LobbyId.ToString())
		{
			string userId = data.TargetUserId.ToString();

			// Obsługa KICKED - ktoś INNY został wyrzucony
			if (data.CurrentStatus == LobbyMemberStatus.Kicked)
			{
				GD.Print($"  👢 Member KICKED: {GetShortUserId(userId)}");
			}

			// Odśwież LobbyDetails handle (tylko jeśli nie zostaliśmy wyrzuceni)
			CacheCurrentLobbyDetailsHandle("member_status");

			// JOINED, LEFT lub KICKED - odśwież całą listę członków
			if (data.CurrentStatus == LobbyMemberStatus.Joined)
			{
				GD.Print($"  ➕ Member JOINED: {GetShortUserId(userId)}");

				// Małe opóźnienie na synchronizację EOS
				GetTree().CreateTimer(0.3).Timeout += () =>
				{
					GetLobbyMembers();
					EmitSignal(SignalName.CurrentLobbyInfoUpdated, currentLobbyId, currentLobbyMembers.Count, 10, isLobbyOwner);
				};
			}
			else if (data.CurrentStatus == LobbyMemberStatus.Left || data.CurrentStatus == LobbyMemberStatus.Kicked)
			{
				GD.Print($"  ➖ Member LEFT/KICKED: {GetShortUserId(userId)}");

				// Małe opóźnienie na pełną synchronizację
				GetTree().CreateTimer(0.3).Timeout += () =>
				{
					GetLobbyMembers();
					EmitSignal(SignalName.CurrentLobbyInfoUpdated, currentLobbyId, currentLobbyMembers.Count, 10, isLobbyOwner);
				};
			}
		}
	}

	/// <summary>
	/// Przypisuje nowego gracza do neutralnej drużyny (NeutralTeam)
	/// Wywoływane przez gracza po dołączeniu do lobby
	/// </summary>
	public void AssignToNeutralTeam()
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("❌ Cannot assign team: Not in any lobby!");
			return;
		}

		GD.Print($"🟡 Assigning new player to NeutralTeam (None)");

		SetMemberAttribute("Team", Team.None.ToString());
	}

	/// <summary>
	/// Przypisuje nowego gracza do uniwersalnej drużyny (UniversalTeam)
	/// Wywoływane przez gracza po dołączeniu do lobby jeśli tryb gry to AIvsHuman
	/// </summary>
	public void AssignToUniversalTeam()
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("❌ Cannot assign team: Not in any lobby!");
			return;
		}

		GD.Print($"🟣 Assigning new player to UniversalTeam (Universal)");

		SetMemberAttribute("Team", Team.Universal.ToString());
	}

	/// <summary>
	/// Ustawia member attribute dla określonego użytkownika
	/// Tylko dla LOKALNEGO gracza - każdy ustawia swoje własne atrybuty
	/// </summary>
	/// <param name="key">Klucz atrybutu</param>
	/// <param name="value">Wartość atrybutu</param>
	public void SetMyTeam(Team teamName)
	{

		if ((teamName == Team.Blue || teamName == Team.Red) && GetTeamPlayerCount(teamName) >= MaxPlayersPerTeam)
		{
			GD.PrintErr($"❌ Cannot join team {teamName}: Team is full ({MaxPlayersPerTeam}/{MaxPlayersPerTeam})");
			return;
		}

		SetMemberAttribute("Team", teamName.ToString());
		GD.Print($"✅ Set my team to: {teamName}");

		//Sprawdzenie warunków dotyczących rozpoczęcia gry
		EmitSignal(SignalName.CheckTeamsBalanceConditions);

	}

	/// <summary>
	/// Odświeża informacje o obecnym lobby i wysyła sygnał do UI
	/// </summary>
	private void RefreshCurrentLobbyInfo()
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			return;
		}

		// Sprawdź czy mamy lobby details
		if (!foundLobbyDetails.ContainsKey(currentLobbyId))
		{
			// Jeśli nie ma w cache, spróbuj skopiować bez wyszukiwania (redukcja zależności od search)
			CacheCurrentLobbyDetailsHandle("refresh_info");
			if (!foundLobbyDetails.ContainsKey(currentLobbyId)) return;
		}

		LobbyDetails lobbyDetails = foundLobbyDetails[currentLobbyId];

		if (lobbyDetails != null)
		{
			var infoOptions = new LobbyDetailsCopyInfoOptions();
			lobbyDetails.CopyInfo(ref infoOptions, out LobbyDetailsInfo? info);

			if (info != null)
			{
				// Pobierz rzeczywistą liczbę członków
				var memberCountOptions = new LobbyDetailsGetMemberCountOptions();
				uint memberCount = lobbyDetails.GetMemberCount(ref memberCountOptions);

				GD.Print($"📊 Lobby info refreshed: {currentLobbyId}, Players: {memberCount}/{info.Value.MaxMembers}");

				// Wyślij sygnał do UI
				EmitSignal(SignalName.CurrentLobbyInfoUpdated,
				currentLobbyId,
				(int)memberCount,
				(int)info.Value.MaxMembers,
				isLobbyOwner);

				// Odśwież atrybuty lobby (CustomLobbyId, GameMode, etc.)
				RefreshLobbyAttributes(lobbyDetails);
			}
		}
		else
		{
			GD.PrintErr($"❌ Failed to refresh lobby info - lobby details is null");
		}
	}

	/// <summary>
	/// Odświeża atrybuty lobby (CustomLobbyId, GameMode, AIType) z EOS
	/// </summary>
	private void RefreshLobbyAttributes(LobbyDetails lobbyDetails)
	{
		if (lobbyDetails == null) return;

		// Pobierz liczbę atrybutów lobby
		var attrCountOptions = new LobbyDetailsGetAttributeCountOptions();
		uint attributeCount = lobbyDetails.GetAttributeCount(ref attrCountOptions);

		GD.Print($"🔄 Refreshing lobby attributes from {attributeCount} attributes...");

		bool customIdFound = false;
		bool gameModeFound = false;
		bool aiTypeFound = false;
		forcedTeamAssignments.Clear();

		// Iteruj po wszystkich atrybutach lobby
		for (uint i = 0; i < attributeCount; i++)
		{
			var attrOptions = new LobbyDetailsCopyAttributeByIndexOptions() { AttrIndex = i };
			Result attrResult = lobbyDetails.CopyAttributeByIndex(ref attrOptions, out Epic.OnlineServices.Lobby.Attribute? attribute);

			if (attrResult == Result.Success && attribute.HasValue && attribute.Value.Data.HasValue)
			{
				string keyStr = attribute.Value.Data.Value.Key;
				string valueStr = attribute.Value.Data.Value.Value.AsUtf8;

				if (keyStr != null && keyStr.Equals("CustomLobbyId", StringComparison.OrdinalIgnoreCase))
				{
					string newCustomLobbyId = !string.IsNullOrEmpty(valueStr) ? valueStr : "Unknown";

					// Tylko zaktualizuj jeśli się zmienił
					if (currentCustomLobbyId != newCustomLobbyId)
					{
						currentCustomLobbyId = newCustomLobbyId;
						GD.Print($"✅ CustomLobbyId refreshed: {currentCustomLobbyId}");
						EmitSignal(SignalName.CustomLobbyIdUpdated, currentCustomLobbyId);
					}
					customIdFound = true;
				}
				else if (keyStr != null && keyStr.Equals("GameMode", StringComparison.OrdinalIgnoreCase))
				{
					string gameModeStr = !string.IsNullOrEmpty(valueStr) ? valueStr : "AI Master";
					GameMode newGameMode = ParseEnumFromDescription<GameMode>(gameModeStr, GameMode.AIMaster);

					// Tylko zaktualizuj jeśli się zmienił
					if (currentGameMode != newGameMode)
					{
						currentGameMode = newGameMode;
						GD.Print($"✅ GameMode refreshed: {GetEnumDescription(currentGameMode)}");
						EmitSignal(SignalName.GameModeUpdated, GetEnumDescription(currentGameMode));
					}
					gameModeFound = true;
				}
				else if (keyStr != null && keyStr.Equals("AIType", StringComparison.OrdinalIgnoreCase))
				{
					string aiTypeStr = !string.IsNullOrEmpty(valueStr) ? valueStr : "API";
					AIType newAIType = ParseEnumFromDescription<AIType>(aiTypeStr, AIType.API);

					// Tylko zaktualizuj jeśli się zmienił
					if (currentAIType != newAIType)
					{
						currentAIType = newAIType;
						GD.Print($"✅ AIType refreshed: {GetEnumDescription(currentAIType)}");
						EmitSignal(SignalName.AITypeUpdated, GetEnumDescription(currentAIType));
					}
					aiTypeFound = true;
				}
				else if (keyStr != null && keyStr.Equals("ReadyToStart", StringComparison.OrdinalIgnoreCase))
				{
					bool isReady = valueStr == "true";
					GD.Print($"✅ ReadyToStart status received: {isReady}");
					EmitSignal(SignalName.LobbyReadyStatusUpdated, isReady);
				}
				else if (keyStr != null && keyStr.StartsWith(ForceTeamAttributePrefix, StringComparison.OrdinalIgnoreCase))
				{
					string targetUserId = keyStr.Substring(ForceTeamAttributePrefix.Length);
					if (!string.IsNullOrEmpty(targetUserId))
					{
						// Pusty valueStr oznacza Team.None
						if (string.IsNullOrEmpty(valueStr))
						{
							GD.Print($"🎯 Found ForceTeam request: {GetShortUserId(targetUserId)} → None");
							forcedTeamAssignments[targetUserId] = Team.None;
						}
						// Parsuj niepusty string na enum
						else if (Enum.TryParse<Team>(valueStr, out Team parsedTeam))
						{
							GD.Print($"🎯 Found ForceTeam request: {GetShortUserId(targetUserId)} → {parsedTeam}");
							forcedTeamAssignments[targetUserId] = parsedTeam;
						}
					}
				}
				else if (keyStr != null && keyStr.StartsWith(PreviousTeamAttributePrefix, StringComparison.OrdinalIgnoreCase))
				{
					string targetUserId = keyStr.Substring(PreviousTeamAttributePrefix.Length);
					if (!string.IsNullOrEmpty(targetUserId) && !string.IsNullOrEmpty(valueStr))
					{
						// Parsuj string na enum
						if (Enum.TryParse<Team>(valueStr, out Team parsedTeam))
						{
							GD.Print($"💾 Found PreviousTeam: {GetShortUserId(targetUserId)} → {parsedTeam}");
							previousTeamAssignments[targetUserId] = parsedTeam;
						}
					}
					else if (!string.IsNullOrEmpty(targetUserId) && string.IsNullOrEmpty(valueStr))
					{
						// Pusty valueStr oznacza usunięcie poprzedniej drużyny
						previousTeamAssignments.Remove(targetUserId);
						GD.Print($"🧹 Cleared PreviousTeam for {GetShortUserId(targetUserId)}");
					}
				}
			}
		}

		// Jeśli nie znaleziono CustomLobbyId
		if (!customIdFound && (string.IsNullOrEmpty(currentCustomLobbyId) || currentCustomLobbyId == "Unknown"))
		{
			GD.PrintErr("⚠️ CustomLobbyId not found in lobby attributes");
		}

		// Jeśli nie znaleziono GameMode, ustaw domyślny
		if (!gameModeFound && currentGameMode != GameMode.AIMaster)
		{
			currentGameMode = GameMode.AIMaster;
			EmitSignal(SignalName.GameModeUpdated, GetEnumDescription(currentGameMode));
			GD.Print("⚠️ GameMode not found, using default: AI Master");
		}
		// Jeśli nie znaleziono AIType, ustaw domyślny
		if (!aiTypeFound && currentAIType != AIType.API)
		{
			currentAIType = AIType.API;
			EmitSignal(SignalName.AITypeUpdated, GetEnumDescription(currentAIType));
			GD.Print("⚠️ AIType not found, using default: API");
		}


		ApplyForcedTeamAssignments();
	}

	/// <summary>
	/// Pobiera rzeczywistą liczbę członków w lobby (użyj po dołączeniu lub przy wyszukiwaniu)
	/// </summary>
	public int GetLobbyMemberCount(string lobbyId)
	{
		if (!foundLobbyDetails.ContainsKey(lobbyId))
		{
			GD.PrintErr($"❌ Lobby details not found for ID: {lobbyId}");
			return 0;
		}

		var countOptions = new LobbyDetailsGetMemberCountOptions();
		uint memberCount = foundLobbyDetails[lobbyId].GetMemberCount(ref countOptions);

		return (int)memberCount;
	}

	public void SetCustomLobbyId(string newCustomId)
	{
		SetLobbyAttribute("CustomLobbyId", newCustomId);

		GD.Print($"🆔 Setting CustomLobbyId to: {newCustomId}");
	}

	public void SetGameMode(GameMode gameMode)
	{
		currentGameMode = gameMode;
		string gameModeStr = GetEnumDescription(gameMode);
		SetLobbyAttribute("GameMode", gameModeStr);

		GD.Print($"🎮 Setting GameMode to: {gameModeStr}");

		EmitSignal(SignalName.GameModeUpdated, gameModeStr);
	}

	public void SetAIType(AIType aiType)
	{
		currentAIType = aiType;
		string aiTypeStr = GetEnumDescription(aiType);
		SetLobbyAttribute("AIType", aiTypeStr);
		GD.Print($"🤖 Setting AIType to: {aiTypeStr}");

		EmitSignal(SignalName.AITypeUpdated, aiTypeStr);
	}

	public void SetLobbyReadyStatus(bool isReady)
	{
		SetLobbyAttribute("ReadyToStart", isReady ? "true" : "false");
		GD.Print($"✅ Setting ReadyToStart to: {isReady}");
	}

	public void SetAPIKey(string apiKey)
	{
		if (string.IsNullOrWhiteSpace(apiKey))
		{
			GD.Print("⚠️ Cannot set empty API Key");
			return;
		}

		SetLobbyAttribute("APIKey", apiKey);
	}

	public string GetAPIKey()
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("❌ Cannot get API Key: Not in any lobby!");
			return "";
		}

		// Sprawdź czy mamy lobby details
		if (!foundLobbyDetails.ContainsKey(currentLobbyId))
		{
			GD.PrintErr("❌ Cannot get API Key: Lobby details not found!");
			return "";
		}

		LobbyDetails lobbyDetails = foundLobbyDetails[currentLobbyId];

		if (lobbyDetails != null)
		{
			var attrCountOptions = new LobbyDetailsGetAttributeCountOptions();
			uint attributeCount = lobbyDetails.GetAttributeCount(ref attrCountOptions);

			for (uint i = 0; i < attributeCount; i++)
			{
				var attrOptions = new LobbyDetailsCopyAttributeByIndexOptions() { AttrIndex = i };
				Result attrResult = lobbyDetails.CopyAttributeByIndex(ref attrOptions, out Epic.OnlineServices.Lobby.Attribute? attribute);

				if (attrResult == Result.Success && attribute.HasValue && attribute.Value.Data.HasValue)
				{
					string keyStr = attribute.Value.Data.Value.Key;
					string valueStr = attribute.Value.Data.Value.Value.AsUtf8;

					if (keyStr != null && keyStr.Equals("APIKey", StringComparison.OrdinalIgnoreCase))
					{
						return valueStr;
					}
				}
			}
		}

		return "";
	}

	/// <summary>
	/// Zapisuje poprzednią drużynę gracza w atrybutach lobby (przed przeniesieniem do Universal)
	/// </summary>
	public void SavePlayerPreviousTeam(string userId, Team previousTeam)
	{
		if (string.IsNullOrEmpty(userId))
		{
			GD.PrintErr("❌ Cannot save previous team: userId is empty");
			return;
		}

		string attributeKey = PreviousTeamAttributePrefix + userId;
		SetLobbyAttribute(attributeKey, previousTeam.ToString());

		// Cache lokalnie
		previousTeamAssignments[userId] = previousTeam;

		GD.Print($"💾 Saved previous team for {GetShortUserId(userId)}: {previousTeam}");
	}

	/// <summary>
	/// Odczytuje poprzednią drużynę gracza z atrybutów lobby
	/// </summary>
	public Team GetPlayerPreviousTeam(string userId)
	{
		if (string.IsNullOrEmpty(userId))
		{
			GD.PrintErr("❌ Cannot get previous team: userId is empty");
			return Team.None;
		}

		// Sprawdź cache
		if (previousTeamAssignments.ContainsKey(userId))
		{
			return previousTeamAssignments[userId];
		}

		return Team.None;
	}

	/// <summary>
	/// Czyści zapisaną poprzednią drużynę gracza
	/// </summary>
	public void ClearPlayerPreviousTeam(string userId)
	{
		if (string.IsNullOrEmpty(userId))
		{
			return;
		}

		string attributeKey = PreviousTeamAttributePrefix + userId;
		SetLobbyAttribute(attributeKey, "");

		// Usuń z cache
		previousTeamAssignments.Remove(userId);

		GD.Print($"🧹 Cleared previous team for {GetShortUserId(userId)}");
	}

	// ============================================
	// MEMBER ATTRIBUTES
	// ============================================

	/// <summary>
	/// Ustawia atrybut lobby (np. CustomLobbyId, LobbyName)
	/// </summary>
	/// <param name="key">Klucz atrybutu</param>
	/// <param name="value">Wartość atrybutu</param>
	private void SetLobbyAttribute(string key, string value)
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("❌ Cannot set lobby attribute: Not in any lobby!");
			return;
		}

		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("❌ Cannot set lobby attribute: User not logged in!");
			return;
		}

		if (!isLobbyOwner)
		{
			GD.PrintErr($"❌ Cannot set lobby attribute '{key}': Not lobby owner!");
			return;
		}

		// Obługa kolejki i wysłanie batch'a
		pendingLobbyAttributes[key] = value;
		attributesToRemove.Remove(key);
		ScheduleAttributeBatchUpdate();
	}

	/// <summary>
	/// Planuje wysłanie batch'a atrybutów lobby po krótkim opóźnieniu
	/// </summary>
	private void ScheduleAttributeBatchUpdate()
	{
		// Jeśli timer już działa, zostaw go (zbieramy więcej zmian)
		if (attributeBatchTimer != null && attributeBatchTimer.TimeLeft > 0)
		{
			return;
		}

		// Uruchom nowy timer
		attributeBatchTimer = GetTree().CreateTimer(AttributeBatchDelay);
		attributeBatchTimer.Timeout += FlushPendingLobbyAttributes;
	}

	/// <summary>
	/// Wysyła wszystkie zebrane zmiany atrybutów lobby w jednym żądaniu
	/// </summary>
	private void FlushPendingLobbyAttributes()
	{
		// Anuluj zaplanowany timer jeśli istnieje
		if (attributeBatchTimer != null)
		{
			attributeBatchTimer.Timeout -= FlushPendingLobbyAttributes;
			attributeBatchTimer = null;
		}

		if (pendingLobbyAttributes.Count == 0 && attributesToRemove.Count == 0)
		{
			return;
		}

		if (string.IsNullOrEmpty(currentLobbyId) || localProductUserId == null || !localProductUserId.IsValid())
		{
			pendingLobbyAttributes.Clear();
			attributesToRemove.Clear();
			return;
		}

		if (!isLobbyOwner)
		{
			pendingLobbyAttributes.Clear();
			attributesToRemove.Clear();
			return;
		}

		var modifyOptions = new UpdateLobbyModificationOptions()
		{
			LobbyId = currentLobbyId,
			LocalUserId = localProductUserId
		};

		Result result = lobbyInterface.UpdateLobbyModification(ref modifyOptions, out LobbyModification lobbyModification);

		if (result != Result.Success || lobbyModification == null)
		{
			pendingLobbyAttributes.Clear();
			attributesToRemove.Clear();
			return;
		}

		// Nowe/zmienione atrybuty
		foreach (var kvp in pendingLobbyAttributes)
		{
			var attributeData = new AttributeData()
			{
				Key = kvp.Key,
				Value = new AttributeDataValue() { AsUtf8 = kvp.Value }
			};

			var addAttrOptions = new LobbyModificationAddAttributeOptions()
			{
				Attribute = attributeData,
				Visibility = LobbyAttributeVisibility.Public
			};

			result = lobbyModification.AddAttribute(ref addAttrOptions);

			if (result != Result.Success)
			{
				GD.PrintErr($"❌ Failed to add lobby attribute '{kvp.Key}': {result}");
			}
		}

		// Atrybuty do usunięcia
		foreach (var key in attributesToRemove)
		{
			var removeAttrOptions = new LobbyModificationRemoveAttributeOptions()
			{
				Key = key
			};

			result = lobbyModification.RemoveAttribute(ref removeAttrOptions);

			if (result != Result.Success)
			{
				GD.PrintErr($"❌ Failed to remove lobby attribute '{key}': {result}");
			}
		}

		// Wyczyść kolejki
		var updatedKeys = new List<string>(pendingLobbyAttributes.Keys);
		var removedKeys = new List<string>(attributesToRemove);
		pendingLobbyAttributes.Clear();
		attributesToRemove.Clear();

		// Wyślij modyfikację
		var updateOptions = new UpdateLobbyOptions()
		{
			LobbyModificationHandle = lobbyModification
		};

		lobbyInterface.UpdateLobby(ref updateOptions, null, (ref UpdateLobbyCallbackInfo data) =>
		{
			if (data.ResultCode == Result.Success)
			{
				GD.Print($"✅ Lobby batch update successful ({updatedKeys.Count} updates, {removedKeys.Count} removals)");
			}
			else
			{
				GD.PrintErr($"❌ Failed to update lobby attributes batch: {data.ResultCode}");
			}

			lobbyModification.Release();
		});
	}

	private void RemoveLobbyAttribute(string key)
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("❌ Cannot remove lobby attribute: Not in any lobby!");
			return;
		}

		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("❌ Cannot remove lobby attribute: User not logged in!");
			return;
		}

		if (!isLobbyOwner)
		{
			GD.PrintErr($"❌ Cannot remove lobby attribute '{key}': Not lobby owner!");
			return;
		}

		// Obługa kolejki
		attributesToRemove.Add(key);
		pendingLobbyAttributes.Remove(key);
		ScheduleAttributeBatchUpdate();
	}

	/// <summary>
	/// Ustawia member attribute dla lokalnego gracza w obecnym lobby
	/// </summary>
	/// <param name="key">Klucz atrybutu (np. "Nickname")</param>
	/// <param name="value">Wartość atrybutu</param>
	private void SetMemberAttribute(string key, string value)
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("❌ Cannot set member attribute: Not in any lobby!");
			return;
		}

		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("❌ Cannot set member attribute: User not logged in!");
			return;
		}

		GD.Print($"📝 Setting member attribute: {key} = '{value}'");

		var modifyOptions = new UpdateLobbyModificationOptions()
		{
			LobbyId = currentLobbyId,
			LocalUserId = localProductUserId
		};

		Result result = lobbyInterface.UpdateLobbyModification(ref modifyOptions, out LobbyModification lobbyModification);

		if (result != Result.Success || lobbyModification == null)
		{
			GD.PrintErr($"❌ Failed to create lobby modification: {result}");
			return;
		}

		var attributeData = new AttributeData()
		{
			Key = key,
			Value = new AttributeDataValue() { AsUtf8 = value }
		};

		var addMemberAttrOptions = new LobbyModificationAddMemberAttributeOptions()
		{
			Attribute = attributeData,
			Visibility = LobbyAttributeVisibility.Public
		};

		result = lobbyModification.AddMemberAttribute(ref addMemberAttrOptions);

		if (result != Result.Success)
		{
			GD.PrintErr($"❌ Failed to add member attribute '{key}': {result}");
			lobbyModification.Release();
			return;
		}

		var updateOptions = new UpdateLobbyOptions()
		{
			LobbyModificationHandle = lobbyModification
		};

		lobbyInterface.UpdateLobby(ref updateOptions, null, (ref UpdateLobbyCallbackInfo data) =>
		{
			if (data.ResultCode == Result.Success)
			{
				GD.Print($"✅ Member attribute '{key}' set successfully: '{value}'");

				// Natychmiastowe odświeżenie lokalnego cache i listy członków
				GetTree().CreateTimer(0.1).Timeout += () =>
				{
					CacheCurrentLobbyDetailsHandle("member_attr_set");
					GetTree().CreateTimer(0.1).Timeout += () =>
					{
						GetLobbyMembers();
					};
				};
			}
			else
			{
				GD.PrintErr($"❌ Failed to update member attribute '{key}': {data.ResultCode}");
			}

			lobbyModification.Release();
		});
	}

	public void MovePlayerToTeam(string targetUserId, Team teamName)
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("❌ Cannot move player: Not in any lobby!");
			return;
		}

		if (!isLobbyOwner)
		{
			GD.PrintErr("❌ Cannot move player: Only lobby owner can change other players' teams!");
			return;
		}

		if (string.IsNullOrEmpty(targetUserId))
		{
			GD.PrintErr("❌ Cannot move player: Target userId is empty!");
			return;
		}

		if ((teamName == Team.Blue || teamName == Team.Red) && GetTeamPlayerCount(teamName) >= MaxPlayersPerTeam)
		{
			GD.PrintErr($"❌ Cannot move player: Team {teamName} is full ({MaxPlayersPerTeam}/{MaxPlayersPerTeam})");
			return;
		}

		if (targetUserId == localProductUserId.ToString())
		{
			GD.Print("ℹ️ Host requested to move themselves, delegating to SetMyTeam");
			SetMyTeam(teamName);
			return;
		}

		GD.Print($"🔀 Host requesting player {GetShortUserId(targetUserId)} to join {teamName} team");
		forcedTeamAssignments[targetUserId] = teamName;
		SetLobbyAttribute($"{ForceTeamAttributePrefix}{targetUserId}", teamName.ToString());
	}

	/// <summary>
	/// Przenosi wszystkich graczy z Blue/Red do Universal i zapisuje ich poprzednie drużyny
	/// Wywoływane gdy host zmienia tryb gry na AI vs Human
	/// </summary>
	public void MoveAllPlayersToUniversal()
	{
		if (!isLobbyOwner)
		{
			GD.PrintErr("❌ Only host can move all players to Universal team");
			return;
		}

		GD.Print("🔄 Moving all players to Universal team...");

		int movedCount = 0;
		foreach (var member in currentLobbyMembers)
		{
			if (!member.ContainsKey("userId") || !member.ContainsKey("team"))
				continue;

			string userId = member["userId"].ToString();
			string teamStr = member["team"].ToString();
			string displayName = member.ContainsKey("displayName") ? member["displayName"].ToString() : "Unknown";

			// Jeśli team jest pusty, traktuj jako None
			Team currentTeam = Team.None;
			if (!string.IsNullOrEmpty(teamStr))
			{
				if (!Enum.TryParse<Team>(teamStr, out currentTeam))
				{
					currentTeam = Team.None;
				}
			}

			// Przenieś tylko graczy z Blue, Red lub None (nie Universal)
			if (currentTeam == Team.Blue || currentTeam == Team.Red || currentTeam == Team.None)
			{
				// Zapisz poprzednią drużynę
				SavePlayerPreviousTeam(userId, currentTeam);

				// Przenieś do Universal
				forcedTeamAssignments[userId] = Team.Universal;
				SetLobbyAttribute($"{ForceTeamAttributePrefix}{userId}", Team.Universal.ToString());

				// Jeśli to host - ustaw też jego MEMBER attribute
				bool isLocalPlayer = userId == localProductUserId.ToString();
				if (isLocalPlayer)
				{
					SetMemberAttribute("Team", Team.Universal.ToString());
					GD.Print($"✅ Host moved to Universal team");
				}

				movedCount++;
			}
		}

		GD.Print($"✅ Moved {movedCount} players to Universal team");

		// Wyślij wszystkie zmiany atrybutów
		FlushPendingLobbyAttributes();
		GetTree().CreateTimer(0.3).Timeout += () =>
		{
			GetLobbyMembers();
		};
	}

	/// <summary>
	/// Przywraca wszystkich graczy z Universal do ich poprzednich drużyn
	/// Wywoływane gdy host zmienia tryb gry z AI vs Human na AI Master
	/// </summary>
	public void RestorePlayersFromUniversal()
	{
		if (!isLobbyOwner)
		{
			GD.PrintErr("❌ Only host can restore players from Universal team");
			return;
		}

		GD.Print("🔄 Restoring players from Universal...");

		int restoredCount = 0;
		foreach (var member in currentLobbyMembers)
		{
			if (!member.ContainsKey("userId") || !member.ContainsKey("team"))
				continue;

			string userId = member["userId"].ToString();
			string teamStr = member["team"].ToString();

			// Jeśli team jest pusty, traktuj jako None
			Team currentTeam = Team.None;
			if (!string.IsNullOrEmpty(teamStr))
			{
				if (!Enum.TryParse<Team>(teamStr, out currentTeam))
				{
					currentTeam = Team.None;
				}
			}

			bool isLocalPlayer = userId == localProductUserId.ToString();

			// Przywróć tylko graczy z Universal
			if (currentTeam == Team.Universal)
			{
				// Odczytaj poprzednią drużynę
				Team previousTeam = GetPlayerPreviousTeam(userId);

				// Jeśli nie ma zapisanej poprzedniej drużyny lub była None/Universal
				if (previousTeam == Team.None || previousTeam == Team.Universal)
				{
					// Ustaw ForceTeam_ na None dla wszystkich graczy
					forcedTeamAssignments[userId] = Team.None;
					SetLobbyAttribute($"{ForceTeamAttributePrefix}{userId}", "");

					// Dodatkowo dla hosta - ustaw MEMBER attribute bezpośrednio
					if (isLocalPlayer)
					{
						SetMemberAttribute("Team", "");
						GD.Print($"✅ Host restored to None team (ForceTeam_ set)");
					}
					else
					{
						GD.Print($"📋 Player {GetShortUserId(userId)} ForceTeam_ set to None");
					}

					ClearPlayerPreviousTeam(userId);
					restoredCount++;
					continue;
				}

				// Przenieś do poprzedniej drużyny (Blue lub Red)
				forcedTeamAssignments[userId] = previousTeam;
				SetLobbyAttribute($"{ForceTeamAttributePrefix}{userId}", previousTeam.ToString());

				// Jeśli to host - ustaw też jego MEMBER attribute
				if (isLocalPlayer)
				{
					SetMemberAttribute("Team", previousTeam.ToString());
					GD.Print($"✅ Host restored to {previousTeam} team");
				}

				// Wyczyść zapisaną poprzednią drużynę
				ClearPlayerPreviousTeam(userId);

				restoredCount++;
			}
		}
		GD.Print($"✅ Restored {restoredCount} players from Universal team");

		// Wyślij wszystkie zmiany atrybutów
		FlushPendingLobbyAttributes();
		GetTree().CreateTimer(0.3).Timeout += () =>
		{
			GetLobbyMembers();
		};
	}

	private Team GetTeamForUser(string userId)
	{
		foreach (var member in currentLobbyMembers)
		{
			if (member.ContainsKey("userId") && member["userId"].ToString() == userId)
			{
				if (member.ContainsKey("team"))
				{
					string teamStr = member["team"].ToString();
					if (Enum.TryParse<Team>(teamStr, out Team parsedTeam))
					{
						return parsedTeam;
					}
				}
				return Team.None;
			}
		}

		return Team.None;
	}

	/// <summary>
	/// Zlicza ile graczy jest w danej drużynie
	/// </summary>
	private int GetTeamPlayerCount(Team team)
	{
		int count = 0;
		foreach (var member in currentLobbyMembers)
		{
			if (member.ContainsKey("userId") && member.ContainsKey("team"))
			{
				string teamStr = member["team"].ToString();
				if (Enum.TryParse<Team>(teamStr, out Team memberTeam) && memberTeam == team)
				{
					count++;
				}
			}
		}
		return count;
	}

	private void ApplyForcedTeamAssignments()
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			return;
		}

		string localUserId = localProductUserId.ToString();

		if (forcedTeamAssignments.TryGetValue(localUserId, out Team forcedTeam))
		{
			Team currentTeam = GetTeamForUser(localUserId);

			if (currentTeam != forcedTeam)
			{
				GD.Print($"🎯 Host forced you to switch to {forcedTeam}");
				// Gdy forcedTeam == None, ustaw pusty string (nie "None")
				string teamValue = (forcedTeam == Team.None) ? "" : forcedTeam.ToString();
				SetMemberAttribute("Team", teamValue);
			}
		}

		if (isLobbyOwner)
		{
			TryResolveForcedTeamRequests();
		}
	}

	private void TryResolveForcedTeamRequests()
	{
		if (!isLobbyOwner || forcedTeamAssignments.Count == 0)
		{
			return;
		}

		// W trybie AI vs Human NIE czyścimy ForceTeam_ atrybutów!
		// Te atrybuty są potrzebne przez cały czas, bo gracze mogą dołączać/odłączać się
		// i muszą wiedzieć że są w Universal team
		if (currentGameMode == GameMode.AIvsHuman)
		{
			return;
		}

		var keysToClear = new System.Collections.Generic.List<string>();
		foreach (var kvp in forcedTeamAssignments)
		{
			string userId = kvp.Key;
			Team forcedTeam = kvp.Value;

			// Pobierz aktualną drużynę z MEMBER attribute
			Team actualTeam = GetTeamForUser(userId);

			// Wyczyść jeśli gracz FAKTYCZNIE zmienił drużynę na wymuszoną
			// Dla Team.None porównujemy bezpośrednio (actualTeam może być None)
			if (actualTeam == forcedTeam)
			{
				// Gracz jest już w wymuszanej drużynie, możemy wyczyścić
				keysToClear.Add(userId);
			}
		}

		foreach (var userId in keysToClear)
		{
			ClearForcedTeamAttribute(userId);
		}
	}

	private void ClearForcedTeamAttribute(string userId)
	{
		if (string.IsNullOrEmpty(userId))
		{
			return;
		}

		forcedTeamAssignments.Remove(userId);
		string attributeKey = $"{ForceTeamAttributePrefix}{userId}";
		GD.Print($"🧹 Clearing forced team attribute for {GetShortUserId(userId)}");
		RemoveLobbyAttribute(attributeKey);
	}

	/// <summary>
	/// Pobiera listę członków obecnego lobby i wysyła sygnał do UI
	/// </summary>
	/// <summary>
	/// Zwraca aktualną listę członków lobby (cache)
	/// </summary>
	public Godot.Collections.Array<Godot.Collections.Dictionary> GetCurrentLobbyMembers()
	{
		return currentLobbyMembers;
	}

	public void GetLobbyMembers()
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("❌ Cannot get lobby members: Not in any lobby!");
			return;
		}

		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("❌ Cannot get lobby members: User not logged in!");
			return;
		}

		// Sprawdź czy mamy lobby details w cache
		if (!foundLobbyDetails.ContainsKey(currentLobbyId))
		{
			GD.PrintErr($"❌ Lobby details not found in cache for ID: {currentLobbyId}");
			GD.Print($"   Available lobbies in cache: {string.Join(", ", foundLobbyDetails.Keys)}");
			return;
		}

		LobbyDetails lobbyDetails = foundLobbyDetails[currentLobbyId];

		if (lobbyDetails == null)
		{
			GD.PrintErr("❌ Lobby details is null!");
			return;
		}

		// Pobierz liczbę członków
		var countOptions = new LobbyDetailsGetMemberCountOptions();
		uint memberCount = lobbyDetails.GetMemberCount(ref countOptions);

		GD.Print($"👥 Getting {memberCount} lobby members from lobby {currentLobbyId}...");

		// Lista członków do wysłania do UI
		var membersList = new Godot.Collections.Array<Godot.Collections.Dictionary>();

		// Iteruj po wszystkich członkach
		for (uint i = 0; i < memberCount; i++)
		{
			var memberByIndexOptions = new LobbyDetailsGetMemberByIndexOptions() { MemberIndex = i };
			ProductUserId memberUserId = lobbyDetails.GetMemberByIndex(ref memberByIndexOptions);

			if (memberUserId != null && memberUserId.IsValid())
			{
				// Pobierz informacje o członku
				var memberInfoOptions = new LobbyDetailsGetMemberAttributeCountOptions() { TargetUserId = memberUserId };
				uint attributeCount = lobbyDetails.GetMemberAttributeCount(ref memberInfoOptions);

				// Pobierz Nickname i Team z atrybutów członka
				string displayName = null;
				string team = ""; // "Blue", "Red", lub pusty string (nie przypisany)
				bool foundNickname = false;

				// Iteruj po wszystkich atrybutach członka
				for (uint j = 0; j < attributeCount; j++)
				{
					var attrOptions = new LobbyDetailsCopyMemberAttributeByIndexOptions()
					{
						TargetUserId = memberUserId,
						AttrIndex = j
					};

					Result attrResult = lobbyDetails.CopyMemberAttributeByIndex(ref attrOptions, out Epic.OnlineServices.Lobby.Attribute? attribute);

					if (attrResult == Result.Success && attribute.HasValue && attribute.Value.Data.HasValue)
					{
						string keyStr = attribute.Value.Data.Value.Key;
						string valueStr = attribute.Value.Data.Value.Value.AsUtf8;

						// Pobierz Nickname
						if (keyStr != null && keyStr.Equals("Nickname", StringComparison.OrdinalIgnoreCase))
						{
							displayName = valueStr;
							foundNickname = true;
						}

						// Pobierz Team
						if (keyStr != null && keyStr.Equals("Team", StringComparison.OrdinalIgnoreCase))
						{
							team = valueStr;
						}
					}
				}

				// Jeśli nie znaleziono Nickname, użyj fallback (skrócony ProductUserId)
				if (!foundNickname)
				{
					string userId = memberUserId.ToString();
					displayName = $"Player_{GetShortUserId(userId)}";
				}

				// Sprawdź czy to właściciel lobby
				var infoOptions = new LobbyDetailsCopyInfoOptions();
				lobbyDetails.CopyInfo(ref infoOptions, out LobbyDetailsInfo? lobbyInfo);
				bool isOwner = lobbyInfo.HasValue && lobbyInfo.Value.LobbyOwnerUserId.ToString() == memberUserId.ToString();

				// Sprawdź czy to lokalny gracz
				bool isLocalPlayer = memberUserId.ToString() == localProductUserId.ToString();

				// To zapobiega pokazywaniu graczy z fallback nickiem
				if (!foundNickname && !isLocalPlayer)
				{
					continue;
				}

				// Sprawdź czy istnieje wymuszenie drużyny (ForceTeam_ to atrybut LOBBY, nie MEMBER)
				string memberUserIdStr = memberUserId.ToString();

				if (forcedTeamAssignments.ContainsKey(memberUserIdStr))
				{
					Team forcedTeam = forcedTeamAssignments[memberUserIdStr];
					team = forcedTeam.ToString();
				}

				// Dodaj do listy
				var memberData = new Godot.Collections.Dictionary
				{
					{ "userId", memberUserId.ToString() },
					{ "displayName", displayName },
					{ "isOwner", isOwner },
					{ "isLocalPlayer", isLocalPlayer },
					{ "team", team }
				};

				membersList.Add(memberData);
			}
		}

		GD.Print($"👥 Total members added to list: {membersList.Count}");

		// SORTOWANIE: Posortuj po userId (Product User ID) aby wszyscy widzieli tę samą kolejność
		// Host ma zawsze pierwszy/najniższy ID w lobby, więc będzie na górze
		// Kolejni gracze będą dodawani w kolejności ich Product User ID

		// Przekonwertuj Godot.Collections.Array na List<>
		var sortedMembers = new List<Godot.Collections.Dictionary>();
		foreach (var member in membersList)
		{
			sortedMembers.Add(member);
		}

		// Sortuj po userId
		sortedMembers.Sort((a, b) =>
			string.Compare(a["userId"].ToString(), b["userId"].ToString(), System.StringComparison.Ordinal)
		);

		// Wyczyść i przepisz posortowane elementy
		membersList.Clear();
		foreach (var member in sortedMembers)
		{
			membersList.Add(member);
		}

		// Zapisz do cache
		currentLobbyMembers = membersList;

		// Sprawdź czy lokalny gracz jest właścicielem (dla automatycznej promocji)
		bool wasOwner = isLobbyOwner;
		isLobbyOwner = false; // Najpierw resetuj

		foreach (var member in membersList)
		{
			bool isLocalPlayer = (bool)member["isLocalPlayer"];
			bool isOwner = (bool)member["isOwner"];

			if (isLocalPlayer && isOwner)
			{
				isLobbyOwner = true;

				// Jeśli staliśmy się właścicielem (awans po opuszczeniu przez hosta)
				if (!wasOwner)
				{
					GD.Print("👑 ✅ You have been promoted to lobby owner!");
				}
				break;
			}
		}

		// Wyślij sygnał do UI
		EmitSignal(SignalName.LobbyMembersUpdated, membersList);

		// Aktualizuj licznik graczy
		EmitSignal(SignalName.CurrentLobbyInfoUpdated, currentLobbyId, membersList.Count, 10, isLobbyOwner);

		TryResolveForcedTeamRequests();
	}   /// <summary>
		/// Ustawia DisplayName dla lokalnego gracza jako MEMBER ATTRIBUTE
		/// Player A ustawia swoje atrybuty → Player B je odczytuje → wyświetla nick A
	// ============================================
	// NOWE: Bezpośrednie kopiowanie LobbyDetails handle
	// ============================================
	private void CacheCurrentLobbyDetailsHandle(string reason)
	{
		if (string.IsNullOrEmpty(currentLobbyId)) return;
		if (localProductUserId == null || !localProductUserId.IsValid()) return;
		// Pozwól na odświeżenie w określonych przypadkach (update/status/ensure/refresh) – czasem stary handle może nie mieć nowych atrybutów
		bool allowRefresh = reason == "member_update"
			|| reason == "member_status"
			|| reason == "lobby_update"
			|| reason == "ensure_sync"
			|| reason == "refresh_info"
			|| reason == "status"
			|| reason == "member_attr_set"
			|| reason == "after_kick"
			|| reason == "refresh_after_join";
		if (foundLobbyDetails.ContainsKey(currentLobbyId) && foundLobbyDetails[currentLobbyId] != null && !allowRefresh) return;
		// Jeśli odświeżamy – zwolnij poprzedni handle aby uniknąć wycieków
		if (allowRefresh && foundLobbyDetails.ContainsKey(currentLobbyId) && foundLobbyDetails[currentLobbyId] != null)
		{
			foundLobbyDetails[currentLobbyId].Release();
			foundLobbyDetails.Remove(currentLobbyId);
		}
		var copyOpts = new CopyLobbyDetailsHandleOptions()
		{
			LobbyId = currentLobbyId,
			LocalUserId = localProductUserId
		};
		Result r = lobbyInterface.CopyLobbyDetailsHandle(ref copyOpts, out LobbyDetails detailsHandle);
		if (r == Result.Success && detailsHandle != null)
		{
			foundLobbyDetails[currentLobbyId] = detailsHandle;
			GD.Print($"🔒 Cached LobbyDetails handle for lobby {currentLobbyId} (reason={reason})");
		}
		else
		{
			GD.Print($"❌ Failed to copy LobbyDetails handle (reason={reason}): {r}");
		}
	}
}





