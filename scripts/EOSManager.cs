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

	//Sygnał emitowany, gdy lobby wykryje rozpoczęcie sesji gry (dla hosta i klientów)
	[Signal]
	public delegate void GameSessionStartRequestedEventHandler(string sessionId, string hostUserId, ulong seed);

	[Signal]
	public delegate void LobbyOwnerChangedEventHandler();

	// Stałe konfiguracyjne
	/// <summary>Minimalna długość nicku gracza.</summary>
	private const int MinNicknameLength = 2;
	/// <summary>Maksymalna długość nicku gracza.</summary>
	private const int MaxNicknameLength = 20;
	/// <summary>Ilość znaków wyświetlana w skróconym identyfikatorze użytkownika.</summary>
	private const int UserIdDisplayLength = 8;
	/// <summary>Maksymalna wartość losowego sufiksu identyfikatora urządzenia.</summary>
	private const int RandomSuffixMax = 10000;
	/// <summary>Maksymalna wartość losowego sufiksu dla fallbackowych nicków.</summary>
	private const int NicknameRandomMax = 99;
	/// <summary>Maksymalna wartość losowego sufiksu dla fallbackowego nicku zwierzaka.</summary>
	private const int FallbackAnimalRandomMax = 9999;
	// Klucze atrybutów lobby używane do synchronizacji startu sesji gry
	/// <summary>Nazwa atrybutu lobby przechowującego identyfikator sesji.</summary>
	private const string ATTR_SESSION_ID = "GameSessionId";
	/// <summary>Nazwa atrybutu lobby przechowującego ziarno sesji.</summary>
	private const string ATTR_SESSION_SEED = "GameSeed";
	/// <summary>Nazwa atrybutu lobby przechowującego identyfikator hosta sesji.</summary>
	private const string ATTR_SESSION_HOST = "GameHostId";
	/// <summary>Nazwa atrybutu lobby przechowującego stan sesji gry.</summary>
	private const string ATTR_SESSION_STATE = "GameSessionState"; // None / Starting / InGame

	// Dane produktu
	/// <summary>Nazwa produktu wykorzystywana przy inicjalizacji EOS.</summary>
	private string productName = "WZIMniacy";
	/// <summary>Wersja produktu przekazywana do EOS.</summary>
	private string productVersion = "1.0";

	// Dane uwierzytelniające EOS
	/// <summary>Identyfikator produktu EOS.</summary>
	private string productId = "e0fad88fbfc147ddabce0900095c4f7b";
	/// <summary>Identyfikator sandboxu EOS.</summary>
	private string sandboxId = "ce451c8e18ef4cb3bc7c5cdc11a9aaae";
	/// <summary>Identyfikator klienta dla aplikacji EOS.</summary>
	private string clientId = "xyza7891eEYHFtDWNZaFlmauAplnUo5H";
	/// <summary>Klucz tajny klienta używany do inicjalizacji EOS.</summary>
	private string clientSecret = "xD8rxykYUyqoaGoYZ5zhK+FD6Kg8+LvkATNkDb/7DPo";
	/// <summary>Identyfikator deploymentu EOS.</summary>
	private string deploymentId = "0e28b5f3257a4dbca04ea0ca1c30f265";

	// Referencje do EOS
	/// <summary>Interfejs platformy EOS po inicjalizacji SDK.</summary>
	private PlatformInterface platformInterface;
	/// <summary>Interfejs Auth EOS dla kont Epic.</summary>
	private AuthInterface authInterface;
	/// <summary>Interfejs Connect EOS dla P2P.</summary>
	private ConnectInterface connectInterface;
	/// <summary>Interfejs lobby EOS.</summary>
	private LobbyInterface lobbyInterface;

	// ID użytkownika - dla P2P używamy ProductUserId (Connect), dla Epic Account używamy EpicAccountId (Auth)
	/// <summary>Identyfikator ProductUserId lokalnego gracza (Connect).</summary>
	private ProductUserId localProductUserId;  // P2P/Connect ID
	public string localProductUserIdString
	{
		get { return localProductUserId.ToString(); }
		set { localProductUserId = ProductUserId.FromString(value); }
	}  // P2P/Connect ID
	/// <summary>Identyfikator EpicAccountId lokalnego gracza (Auth).</summary>
	private EpicAccountId localEpicAccountId;  // Epic Account ID

	// Lokalny cache danych sesji gry odczytanych z atrybtów lobby
	/// <summary>Bieżący cache danych sesji gry synchronizowany z atrybutami lobby.</summary>
	public GameSessionData CurrentGameSession { get; private set; } = new GameSessionData();

	// Nie wiem dlaczego projekt nie buduje się przez jakiś błąd związany z apikey więc daje quick fix, nie koniecznie poprawny
	/// <summary>Klucz API używany przez integracje pomocnicze.</summary>
	private string apiKey = "";
	/// <summary>Publiczny odczyt klucza API.</summary>
	public string ApiKey => apiKey;
	/// <summary>
	/// Ustawia klucz API dla integracji z zewnętrznymi usługami pomocniczymi.
	/// </summary>
	/// <param name="newApiKey">Nowy klucz API; null zostanie zamieniony na pusty ciąg.</param>
	/// <remarks>Klucz jest przechowywany lokalnie w pamięci; metoda nie waliduje formatu klucza.</remarks>
	public void SetAPIKey(string newApiKey)
	{
		apiKey = newApiKey ?? "";
		GD.Print("[EOSManager:APIKey] API key set");
	}


	// Właśiwość platforminterface
	/// <summary>Publiczny dostęp do interfejsu platformy EOS.</summary>
	public PlatformInterface PlatformInterface => platformInterface;

	// chroni przed wielokrotnym przejściem do sceny gry przy wielu update’ach lobby
	/// <summary>Flaga zabezpieczająca przed wielokrotnym startem sesji gry.</summary>
	private bool sessionStartHandled = false;

	/// <summary>
	/// Zwraca identyfikator ProductUserId właściciela bieżącego lobby lub pusty ciąg, gdy brak danych.
	/// </summary>
	/// <returns>Id właściciela lobby w formie string lub pusty ciąg.</returns>
	/// <remarks>Zwraca pusty string, gdy nie jesteśmy w lobby albo brak uchwytu LobbyDetails.</remarks>
	public string GetLobbyOwnerPuidString()
	{
		if (string.IsNullOrEmpty(currentLobbyId))
			return "";

		if (!foundLobbyDetails.ContainsKey(currentLobbyId) || foundLobbyDetails[currentLobbyId] == null)
			return "";

		var infoOptions = new LobbyDetailsCopyInfoOptions();
		foundLobbyDetails[currentLobbyId].CopyInfo(ref infoOptions, out LobbyDetailsInfo? info);

		if (info == null)
			return "";

		return info.Value.LobbyOwnerUserId?.ToString() ?? "";
	}


	// Wywoływane przez hosta - zapisuje dane sesji do lobby i inicjuje start gry
	/// <summary>
	/// Wywoływane przez hosta: zapisuje parametry sesji w atrybutach lobby i rozpoczyna synchronizację startu gry.
	/// </summary>
	/// <remarks>Metoda blokuje lobby przez <see cref="LockLobby"/> i aktualizuje lokalny cache <see cref="CurrentGameSession"/>.</remarks>
	/// <seealso cref="GenerateSessionId"/>
	/// <seealso cref="SetLobbyAttribute(string, string)"/>
	/// <seealso cref="LockLobby"/>
	/// <remarks>Gdy gracz nie jest w lobby, nie jest hostem lub nie ma ważnego ProductUserId.</remarks>
	public void RequestStartGameSession()
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("[EOSManager:GameSession] Cannot start session: not in lobby");
			return;
		}

		if (!isLobbyOwner)
		{
			GD.PrintErr("[EOSManager:GameSession] Only host can start session");
			return;
		}
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("[EOSManager:GameSession] Cannot start session: localProductUserId invalid (not logged in yet)");
			return;
		}

		// Zamnij lobby
		LockLobby();

		// 1) Generowanie danych
		string sessionId = GenerateSessionId();
		ulong seed = (ulong)GD.Randi(); // na razie proste; potem można rozszerzyć
		if (seed == 0) seed = 1;

		// 2) Zapis danych sesji do lobby EOS - uruchamia synchronizację u wszystkich graczy
		SetLobbyAttribute(ATTR_SESSION_ID, sessionId);
		SetLobbyAttribute(ATTR_SESSION_SEED, seed.ToString());
		SetLobbyAttribute(ATTR_SESSION_HOST, localProductUserId.ToString());
		SetLobbyAttribute(ATTR_SESSION_STATE, GameSessionState.Starting.ToString());

		// 3) lokalnie też ustaw cache
		CurrentGameSession.SessionId = sessionId;
		CurrentGameSession.LobbyId = currentLobbyId;
		CurrentGameSession.Seed = seed;
		CurrentGameSession.HostUserId = localProductUserId.ToString();
		CurrentGameSession.State = GameSessionState.Starting;

		// host też powinien przejść dopiero po update lobby,
		// więc NIE robimy tu ChangeScene.
		GD.Print($"[EOSManager:GameSession] Host requested session start: {sessionId}, seed={seed}");
	}

	/// <summary>
	/// Ustawia atrybut członka lobby informujący czy gracz jest w widoku lobby
	/// Wywoływane przy wejściu do lobby (true) i wejściu do gry (false)
	/// </summary>
	/// <param name="inLobby">Czy gracz znajduje się w widoku lobby.</param>
	/// <remarks>Metoda aktualizuje atrybut członka i lokalny cache stanu.</remarks>
	/// <seealso cref="SetMemberAttribute"/>
	public void SetPlayerInLobbyView(bool inLobby)
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.Print("[EOSManager:LobbyView] Cannot set InLobbyView: not in lobby");
			return;
		}

		isLocalPlayerInLobbyView = inLobby;
		string value = inLobby ? "true" : "false";
		SetMemberAttribute("InLobbyView", value);
	}

	/// <summary>
	/// Sprawdza czy wszyscy gracze w lobby są w widoku lobby (nie w grze)
	/// </summary>
	/// <returns>True, gdy każdy członek ma atrybut InLobbyView ustawiony na true.</returns>
	/// <remarks>Metoda bazuje na lokalnym cache <see cref="currentLobbyMembers"/>.</remarks>
	/// <seealso cref="SetPlayerInLobbyView"/>
	public bool AreAllPlayersInLobbyView()
	{
		if (currentLobbyMembers == null || currentLobbyMembers.Count == 0)
		{
			GD.Print("[EOSManager:LobbyView] AreAllPlayersInLobbyView: no lobby members");
			return true;
		}

		foreach (var member in currentLobbyMembers)
		{
			string inLobbyView = "true"; // Domyślnie true

			if (member.ContainsKey("inLobbyView"))
			{
				inLobbyView = member["inLobbyView"].ToString().ToLower();
			}

			if (inLobbyView != "true")
			{
				string displayName = member.ContainsKey("displayName") ? member["displayName"].ToString() : "Unknown";
				GD.Print($"[EOSManager:LobbyView] Player {displayName} is not in lobby view yet (InLobbyView={inLobbyView})");
				return false;
			}
		}

		GD.Print("[EOSManager:LobbyView] All players are in lobby view");
		return true;
	}

	/// <summary>
	/// Resetuje stan sesji gry w lobby - używane po zakończeniu gry i powrocie do lobby
	/// Tylko host może wywołać tę metodę
	/// </summary>
	/// <remarks>Resetuje atrybuty sesji w lobby oraz lokalny cache <see cref="CurrentGameSession"/>.</remarks>
	/// <seealso cref="SetLobbyAttribute(string, string)"/>
	public void ResetGameSession()
	{
		if (!isLobbyOwner)
		{
			GD.Print("[EOSManager:GameSession] Only host can reset game session");
			return;
		}

		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.Print("[EOSManager:GameSession] Cannot reset session: not in lobby");
			return;
		}

		// Wyczyść atrybuty sesji w lobby
		SetLobbyAttribute(ATTR_SESSION_STATE, GameSessionState.None.ToString());
		SetLobbyAttribute(ATTR_SESSION_ID, "");
		SetLobbyAttribute(ATTR_SESSION_SEED, "");
		SetLobbyAttribute(ATTR_SESSION_HOST, "");

		// Wyczyść lokalny cache sesji
		CurrentGameSession.SessionId = "";
		CurrentGameSession.LobbyId = "";
		CurrentGameSession.Seed = 0;
		CurrentGameSession.HostUserId = "";
		CurrentGameSession.State = GameSessionState.None;

		GD.Print("[EOSManager:GameSession] Game session reset - ready for new game");
	}

	//Generuje krótki, czytelny identyfikator sesji gry (debug/ logi/ recconect) 
	/// <summary>
	/// Generuje 8-znakowy, czytelny identyfikator sesji gry używany w logach i debugowaniu.
	/// </summary>
	/// <returns>Losowy identyfikator sesji złożony z wielkich liter i cyfr.</returns>
	private string GenerateSessionId()
	{
		const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";
		return new string(Enumerable.Range(0, 8)
			.Select(_ => chars[Random.Shared.Next(chars.Length)])
			.ToArray());
	}

	// Przechowywanie znalezionych lobby
	/// <summary>Lista identyfikatorów lobby znalezionych w ostatnim wyszukiwaniu.</summary>
	private System.Collections.Generic.List<string> foundLobbyIds = new System.Collections.Generic.List<string>();
	/// <summary>Cache uchwytów LobbyDetails dla znalezionych lobby.</summary>
	private System.Collections.Generic.Dictionary<string, LobbyDetails> foundLobbyDetails = new System.Collections.Generic.Dictionary<string, LobbyDetails>();

	// Obecne lobby w którym jesteśmy
	/// <summary>Identyfikator bieżącego lobby lub null, gdy brak.</summary>
	public string currentLobbyId = null;
	/// <summary>Flaga informująca, czy lokalny gracz jest właścicielem lobby.</summary>
	public bool isLobbyOwner = false;
	/// <summary>Flaga informująca, czy lokalny gracz jest w widoku lobby.</summary>
	public bool isLocalPlayerInLobbyView = true;

	// Czy trwa proces dołączania do lobby
	/// <summary>Flaga sygnalizująca, że trwa dołączanie do lobby.</summary>
	public bool isJoiningLobby = false;

	// Custom Lobby ID
	/// <summary>Bieżący kod lobby synchronizowany z atrybutami.</summary>
	public string currentCustomLobbyId = "";

	// Current Game Mode (tryb gry) i AI Type
	/// <summary>Bieżący tryb gry obowiązujący w lobby.</summary>
	public GameMode currentGameMode = GameMode.AIMaster;
	/// <summary>Bieżący typ AI wybrany w lobby.</summary>
	public AIType currentAIType = AIType.API;

	// Aktualna lista członków lobby (cache)
	/// <summary>Cache aktualnych członków lobby (lista słowników atrybutów).</summary>
	private Godot.Collections.Array<Godot.Collections.Dictionary> currentLobbyMembers = new Godot.Collections.Array<Godot.Collections.Dictionary>();
	/// <summary>Publiczny odczyt cache członków lobby.</summary>
	public Godot.Collections.Array<Godot.Collections.Dictionary> CurrentLobbyMembers
	{
		get { return currentLobbyMembers; }
	}

	// Prefiks atrybutu lobby służącego do wymuszania drużyn przez hosta
	/// <summary>Prefiks atrybutu lobby dla wymuszonych drużyn.</summary>
	private const string ForceTeamAttributePrefix = "ForceTeam_";
	/// <summary>Mapowanie wymuszonych drużyn per gracz.</summary>
	private System.Collections.Generic.Dictionary<string, Team> forcedTeamAssignments = new System.Collections.Generic.Dictionary<string, Team>(StringComparer.OrdinalIgnoreCase);

	// Prefiks atrybutu lobby służącego do wymuszania ikon przez hosta (przy zmianie trybu)
	/// <summary>Prefiks atrybutu lobby dla wymuszonych ikon profilowych.</summary>
	private const string ForceIconAttributePrefix = "ForceIcon_";
	/// <summary>Mapowanie wymuszonych ikon profilowych per gracz.</summary>
	private System.Collections.Generic.Dictionary<string, int> forcedIconAssignments = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	// Prefiks atrybutu lobby służącego do przechowywania poprzednich drużyn (przed przejściem do Universal)
	/// <summary>Prefiks atrybutu lobby dla zapisu poprzedniej drużyny.</summary>
	private const string PreviousTeamAttributePrefix = "PreviousTeam_";
	/// <summary>Cache poprzednich drużyn graczy.</summary>
	private System.Collections.Generic.Dictionary<string, Team> previousTeamAssignments = new System.Collections.Generic.Dictionary<string, Team>(StringComparer.OrdinalIgnoreCase);

	// Nickname ustawiony PRZED wejściem do lobby
	/// <summary>Nick ustawiony przed wejściem do lobby.</summary>
	private string pendingNickname = "";

	// Lista zwierzaków wczytana z pliku >w<
	/// <summary>Lista nazw zwierząt używana do generowania nicków.</summary>
	private System.Collections.Generic.List<string> animalNames = new System.Collections.Generic.List<string>();

	// System ikon profilowych
	/// <summary>Zajęte ikony drużyny niebieskiej.</summary>
	private System.Collections.Generic.HashSet<int> usedBlueIcons = new System.Collections.Generic.HashSet<int>();
	/// <summary>Zajęte ikony drużyny czerwonej.</summary>
	private System.Collections.Generic.HashSet<int> usedRedIcons = new System.Collections.Generic.HashSet<int>();
	/// <summary>Maksymalna liczba ikon profilowych na drużynę.</summary>
	private const int MaxProfileIconsPerTeam = 5;

	// Flaga blokująca tworzenie lobby
	/// <summary>Flaga blokująca równoległe tworzenie lobby.</summary>
	private bool isCreatingLobby = false;

	// Kolejkowanie atrybutów lobby - zbieranie zmian i wysyłanie razem
	/// <summary>Kolejka zmian atrybutów lobby do wysłania w paczce.</summary>
	private System.Collections.Generic.Dictionary<string, string> pendingLobbyAttributes = new System.Collections.Generic.Dictionary<string, string>();
	/// <summary>Kolejka atrybutów lobby do usunięcia.</summary>
	private System.Collections.Generic.HashSet<string> attributesToRemove = new System.Collections.Generic.HashSet<string>();
	/// <summary>Timer do opóźnionego wysyłania paczki zmian atrybutów.</summary>
	private SceneTreeTimer attributeBatchTimer = null;
	/// <summary>Opóźnienie (s) przed wysłaniem paczki atrybutów.</summary>
	private const float AttributeBatchDelay = 0.1f;

	// Timer do odświeżania lobby
	/// <summary>Timer okresowego odświeżania danych lobby.</summary>
	private Timer lobbyRefreshTimer;
	//Limit graczy w drużynie
	/// <summary>Maksymalna liczba graczy w drużynie Blue/Red.</summary>
	private const int MaxPlayersPerTeam = 5;
	//Limit graczy w trybie AI vs Human (Universal Team)
	/// <summary>Maksymalna liczba graczy w trybie AI vs Human.</summary>
	private const int MaxPlayersInAIvsHuman = 5;
	// Custom popup system
	/// <summary>System popupów do komunikatów w UI.</summary>
	public PopupSystem popupSystem { get; private set; }

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
	/// <summary>
	/// Zwraca opis z atrybutu <see cref="DescriptionAttribute"/> lub nazwę enum, gdy brak atrybutu.
	/// </summary>
	/// <param name="value">Wartość typu wyliczeniowego.</param>
	/// <returns>Tekstowy opis wartości enum.</returns>
	/// <remarks>W przypadku braku pola lub atrybutu zwracana jest nazwa enum.</remarks>
	/// <seealso cref="ParseEnumFromDescription{T}(string, T)"/>
	public static string GetEnumDescription(System.Enum value)
	{
		var field = value.GetType().GetField(value.ToString());
		var attribute = (DescriptionAttribute)System.Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute));
		return attribute?.Description ?? value.ToString();
	}

	/// <summary>
	/// Parsuje opis (DescriptionAttribute) na wartość enum lub zwraca domyślną wartość.
	/// </summary>
	/// <typeparam name="T">Typ wyliczeniowy.</typeparam>
	/// <param name="description">Tekst opisu z atrybutu Description.</param>
	/// <param name="defaultValue">Wartość domyślna zwracana, gdy parsing się nie powiedzie.</param>
	/// <returns>Wartość enum odpowiadająca opisowi lub domyślna.</returns>
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
	/// <summary>
	/// Inicjalizuje SDK EOS, konfiguruje logowanie, tworzy interfejsy i rozpoczyna logowanie P2P.
	/// </summary>
	/// <remarks>Wywoływane przy wejściu do drzewa sceny; powinno działać w wątku głównym Godota.</remarks>
	/// <seealso cref="LoadAnimalNames"/>
	/// <seealso cref="AddLobbyUpdateNotifications"/>
	/// <seealso cref="LoginWithDeviceId_P2P"/>
	/// <remarks>Gdy inicjalizacja SDK, stworzenie interfejsów lub usuwanie DeviceId zwróci błąd.</remarks>
	public override void _Ready()
	{
		base._Ready();

		// Załaduj custom popup system
		LoadPopupSystem();

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

		GD.Print("[EOSManager:Initialization] EOS Initialization");

		// Wczytaj listę zwierzaków z pliku ^w^
		LoadAnimalNames();

		// Krok 1: Inicjalizacja SDK
		var initializeOptions = new InitializeOptions()
		{
			ProductName = productName,
			ProductVersion = productVersion,
		};

		GD.Print($"[EOSManager:Initialization] Product: {productName} v{productVersion}");

		var initializeResult = PlatformInterface.Initialize(ref initializeOptions);
		if (initializeResult != Result.Success)
		{
			GD.PrintErr("[EOSManager:Initialization] Failed to initialize EOS SDK: " + initializeResult);
			return;
		}

		GD.Print("[EOSManager:Initialization] EOS SDK initialized successfully.");
		// Krok 2: Konfiguracja logowania
		LoggingInterface.SetLogLevel(LogCategory.AllCategories, LogLevel.VeryVerbose);
		LoggingInterface.SetCallback((ref LogMessage logMessage) =>
		{
			GD.Print($"[EOSManager:Logging {logMessage.Category}] {logMessage.Message}");
		});

		GD.Print("[EOSManager:Initialization] Logging configured.");
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

		GD.Print($"[EOSManager:Initialization] Creating platform with ProductId: {productId}");
		GD.Print($"[EOSManager:Initialization] Sandbox: {sandboxId}, Deployment: {deploymentId}");

		platformInterface = PlatformInterface.Create(ref createOptions);
		if (platformInterface == null)
		{
			GD.PrintErr("[EOSManager:Initialization] ❌ Failed to create EOS Platform Interface!");
			return;
		}

		GD.Print("[EOSManager:Initialization] EOS Platform Interface created successfully.");
		// Pobierz Auth Interface
		authInterface = platformInterface.GetAuthInterface();
		if (authInterface == null)
		{
			GD.PrintErr("[EOSManager] Failed to get Auth Interface!");
			return;
		}

		// Pobierz Connect Interface (P2P, bez wymagania konta Epic)
		connectInterface = platformInterface.GetConnectInterface();
		if (connectInterface == null)
		{
			GD.PrintErr("[EOSManager:Initialization] Failed to get Connect Interface!");
			return;
		}

		// Pobierz Lobby Interface
		lobbyInterface = platformInterface.GetLobbyInterface();
		if (lobbyInterface == null)
		{
			GD.PrintErr("[EOSManager:Initialization] Failed to get Lobby Interface!");
			return;
		}

		// Dodaj nasłuchiwanie na zmiany w lobby (update członków)
		AddLobbyUpdateNotifications();

		// USUWAMY ISTNIEJĄCY DEVICEID ŻEBY MÓGŁ STWORZYĆ FAKTYCZNIE NOWY, IDK CZY TO ABY NA PEWNO DZIAŁA PRAWIDŁOWO
		// W PRZYPADKU TESTÓW NA JEDNYM URZĄDZENIU, ale na nie pozwala chyba także yippee
		GD.Print("[EOSManager:Initialization] Deleting DeviceId...");

		var deleteDeviceIdOptions = new DeleteDeviceIdOptions();

		connectInterface.DeleteDeviceId(ref deleteDeviceIdOptions, null, (ref DeleteDeviceIdCallbackInfo data) =>
		{
			if (data.ResultCode == Result.Success)
			{
				GD.Print("[EOSManager:Initialization] Successfully deleted existing DeviceId");
				LoginWithDeviceId_P2P();
			}
			else if (data.ResultCode == Result.NotFound)
			{
				GD.Print("[EOSManager:Initialization] DeviceId for deletion was not found");
				LoginWithDeviceId_P2P();
			}
			else
			{
				GD.PrintErr("[EOSManager:Initialization] Unexpected error while deleting existing DeviceId, Result: " + (int)data.ResultCode + ":" + data.ResultCode);
			}
		});
	}

	/// <summary>
	/// Ładuje custom popup system ze sceny
	/// </summary>
	private void LoadPopupSystem()
	{
		var popupScene = GD.Load<PackedScene>("res://scenes/popup/PopupSystem.tscn");
		if (popupScene != null)
		{
			popupSystem = popupScene.Instantiate<PopupSystem>();
			AddChild(popupSystem);
		}
		else
		{
			GD.PrintErr("[EOSManager:Popup] Failed to load PopupSystem scene");
		}
	}

	/// <summary>
	/// Czyści lokalny stan i prezentuje komunikat, gdy gracz zostaje wyrzucony z lobby przez hosta.
	/// </summary>
	private void HandleKickedFromLobby()
	{
		GD.Print("[EOSManager:Lobby] Player was kicked from lobby");

		// Zatrzymaj timer odświeżania jeśli jeszcze działa
		if (lobbyRefreshTimer != null && lobbyRefreshTimer.TimeLeft > 0)
		{
			lobbyRefreshTimer.Stop();
			GD.Print("[EOSManager:Lobby] Lobby refresh timer stopped (kicked)");
		}

		// NIE wywołujemy LeaveLobby() - serwer EOS już zamknął połączenie websocket
		// Bezpośrednio czyścimy lokalny stan (tak jak robi OnLeaveLobbyComplete)

		// Wyczyść obecne lobby
		currentLobbyId = null;
		isLobbyOwner = false;
		isLocalPlayerInLobbyView = true; // Reset

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

		// Pokaż popup z informacją o wyrzuceniu
		if (popupSystem != null)
		{
			popupSystem.ShowMessage(
				"WYRZUCONY Z LOBBY",
				"Zostałeś wyrzucony przez hosta!",
				() =>
				{
					if (GetTree() != null)
					{
						GetTree().ChangeSceneToFile("res://scenes/menu/main.tscn");
					}
				}
			);
		}
		else
		{
			GD.PrintErr("[EOSManager:Popup] PopupSystem is null, cannot show kicked message");
			// Fallback - wróć do menu nawet bez popupu
			if (GetTree() != null)
			{
				GetTree().ChangeSceneToFile("res://scenes/menu/main.tscn");
			}
		}
	}

	// Logowanie przez Device ID (Developer Tool - tylko do testów!)
	/// <summary>
	/// Loguje użytkownika przy użyciu Developer Auth (Device ID) – opcja testowa z DevAuthTool.
	/// </summary>
	/// <seealso cref="OnLoginComplete"/>
	/// <seealso cref="LoginWithAccountPortal"/>
	private void LoginWithDeviceId()
	{
		GD.Print("[EOSManager:Login] Starting Developer Auth login...");

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

		GD.Print($"[EOSManager:Login] Attempting Developer Auth login with DevTool at: {devToolHost}, User: {userName}");
		authInterface.Login(ref loginOptions, null, OnLoginComplete);
	}

	// Logowanie przez Account Portal (otwiera przeglądarkę Epic)
	/// <summary>
	/// Rozpoczyna logowanie przez Epic Account Portal (otwiera przeglądarkę).
	/// </summary>
	/// <seealso cref="OnLoginComplete"/>
	private void LoginWithAccountPortal()
	{
		GD.Print("[EOSManager] Starting Account Portal login...");

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

		GD.Print("[EOSManager:Login] Opening Epic Account login in browser...");
		authInterface.Login(ref loginOptions, null, OnLoginComplete);
	}

	// Logowanie przez Persistent Auth (używa zapamiętanych danych)
	/// <summary>
	/// Loguje użytkownika korzystając z zapisanych danych (Persistent Auth).
	/// </summary>
	/// <seealso cref="OnLoginComplete"/>
	private void LoginWithPersistentAuth()
	{
		GD.Print("[EOSManager:Login] Starting Persistent Auth login...");

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


	/// <summary>
	/// Loguje użytkownika do warstwy Connect przy użyciu DeviceID (scenariusz P2P bez konta Epic).
	/// </summary>
	/// <seealso cref="GetOrCreateDeviceId"/>
	/// <seealso cref="OnConnectLoginComplete"/>
	/// <remarks>Gdy utworzenie DeviceID lub logowanie Connect zwróci błąd.</remarks>
	private void LoginWithDeviceId_P2P()
	{
		GD.Print("[EOSManager:Login] Starting P2P login with DeviceID...");

		// ON TEGO NIGDZIE NIE UŻYWA NAWET ._.
		// Generuj unikalny DeviceID dla tego urządzenia
		string deviceId = GetOrCreateDeviceId();
		GD.Print($"[EOSManager:Login] Device ID: {deviceId}");

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
				GD.Print("[EOSManager:Login] DeviceID ready, logging in...");

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
				GD.PrintErr($"[EOSManager:Login] Failed to create DeviceID: {data.ResultCode}");
			}
		});
	}

	// Callback dla Connect Login (P2P)
	/// <summary>
	/// Callback logowania Connect (P2P); zapisuje ProductUserId i raportuje status logowania.
	/// </summary>
	/// <param name="data">Informacje zwrotne z logowania Connect.</param>
	/// <seealso cref="LoginWithDeviceId_P2P"/>
	/// <remarks>Gdy logowanie Connect zakończy się błędem.</remarks>
	private void OnConnectLoginComplete(ref Epic.OnlineServices.Connect.LoginCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print($"[EOSManager:Login] P2P Login successful! ProductUser ID: {data.LocalUserId}");
			localProductUserId = data.LocalUserId;

			// Gotowe do tworzenia lobby!
			GD.Print("[EOSManager:Login] EOS READY");
			// Teraz możesz wywołać funkcje lobby
			// Przykład: CreateLobby("MyLobby", 4);
		}
		else
		{
			GD.PrintErr($"[EOSManager:Login] P2P Login failed: {data.ResultCode}");
		}
	}

	// Generuj lub odczytaj DeviceID
	/// <summary>
	/// Generuje unikalny identyfikator urządzenia (z losowym sufiksem) lub zwraca istniejący.
	/// </summary>
	/// <returns>Identyfikator urządzenia używany do logowania DeviceID.</returns>
	/// <seealso cref="LoginWithDeviceId_P2P"/>
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

	/// <summary>
	/// Pobiera obecne Device ID używane do logowania DeviceID.
	/// </summary>
	/// <returns>Identyfikator urządzenia dla bieżącej instancji.</returns>
	/// <remarks>Metoda generuje identyfikator na podstawie danych maszyny i losowego sufiksu.</remarks>
	/// <seealso cref="GetOrCreateDeviceId"/>
	public string GetCurrentDeviceId()
	{
		return GetOrCreateDeviceId();
	}

	/// <summary>
	/// Resetuje Device ID - usuwa obecne i tworzy nowe
	/// </summary>
	/// <remarks>Po udanym usunięciu ponawia logowanie Connect (P2P).</remarks>
	/// <seealso cref="LoginWithDeviceId_P2P"/>
	/// <remarks>Gdy usuwanie istniejącego DeviceId zwróci błąd.</remarks>
	public void ResetDeviceId()
	{
		GD.Print("[EOSManager:Login] Resetting Device ID...");

		var deleteDeviceIdOptions = new DeleteDeviceIdOptions();

		connectInterface.DeleteDeviceId(ref deleteDeviceIdOptions, null, (ref DeleteDeviceIdCallbackInfo data) =>
		{
			if (data.ResultCode == Result.Success)
			{
				GD.Print("[EOSManager:Login] Successfully deleted existing DeviceId.");
				LoginWithDeviceId_P2P();
			}
			else if (data.ResultCode == Result.NotFound)
			{
				GD.Print("[EOSManager:Login] DeviceId for deletion was not found");
				LoginWithDeviceId_P2P();
			}
			else
			{
				GD.PrintErr($"[EOSManager:Login] Unexpected error while deleting DeviceId: {data.ResultCode}");
			}
		});
	}

	// Callback po zakończeniu logowania
	/// <summary>
	/// Callback logowania Auth; zapisuje EpicAccountId i pobiera token użytkownika.
	/// </summary>
	/// <param name="data">Informacje zwrotne z procesu logowania Auth.</param>
	/// <seealso cref="LoginWithPersistentAuth"/>
	/// <seealso cref="LoginWithAccountPortal"/>
	/// <remarks>Gdy logowanie Auth kończy się błędem innym niż InvalidUser.</remarks>
	private void OnLoginComplete(ref Epic.OnlineServices.Auth.LoginCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print($"[EOSManager:Login] Login successful! User ID: {data.LocalUserId}");
			localEpicAccountId = data.LocalUserId;

			// Pobierz dodatkowe informacje o użytkowniku
			var copyUserAuthTokenOptions = new CopyUserAuthTokenOptions();
			Result result = authInterface.CopyUserAuthToken(ref copyUserAuthTokenOptions, data.LocalUserId, out Epic.OnlineServices.Auth.Token? authToken);

			if (result == Result.Success && authToken.HasValue)
			{
				GD.Print($"[EOSManager:Login] Account ID: {authToken.Value.AccountId}");
			}
		}
		else if (data.ResultCode == Result.InvalidUser)
		{
			// Brak zapisanych danych - przejdź na AccountPortal
			GD.Print($"[EOSManager:Login] PersistentAuth failed ({data.ResultCode}), trying AccountPortal...");
			LoginWithAccountPortal();
		}
		else
		{
			GD.PrintErr($"[EOSManager:Login] Login failed: {data.ResultCode}");
		}
	}

	// Pobierz informacje o zalogowanym użytkowniku
	/// <summary>
	/// Kopiuje i wypisuje podstawowe informacje o zalogowanym użytkowniku Auth.
	/// </summary>
	/// <seealso cref="OnLoginComplete"/>
	/// <remarks>Gdy lokalne EpicAccountId jest nieważne.</remarks>
	private void GetUserInfo()
	{
		if (localEpicAccountId == null || !localEpicAccountId.IsValid())
		{
			GD.PrintErr("[EOSManager:Login] No valid user ID!");
			return;
		}

		var copyOptions = new CopyUserAuthTokenOptions();
		var result = authInterface.CopyUserAuthToken(ref copyOptions, localEpicAccountId, out var authToken);

		if (result == Result.Success && authToken != null)
		{

			GD.Print("[EOSManager:Login] === User Info ===");
			GD.Print($"[EOSManager:Login] Account ID: {localEpicAccountId}");
			GD.Print($"[EOSManager:Login] App: {authToken?.App}");
			GD.Print($"[EOSManager:Login] Client ID: {authToken?.ClientId}");
			GD.Print("[EOSManager:Login] =================");
		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	/// <summary>
	/// Główny tick sceny – wywołuje Platform.Tick, aby obsłużyć sieć i callbacki EOS.
	/// </summary>
	/// <param name="delta">Czas od ostatniej klatki w sekundach.</param>
	/// <remarks>Wywoływane co klatkę; brak wywołania może wstrzymać przetwarzanie callbacków EOS.</remarks>
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
	/// <summary>
	/// Zamyka sesję EOS: wylogowuje użytkownika, zwalnia PlatformInterface i wywołuje shutdown SDK.
	/// </summary>
	/// <remarks>Wywoływane przy usuwaniu węzła; zamyka połączenia i zwalnia zasoby EOS.</remarks>
	/// <seealso cref="OnLogoutComplete"/>
	public override void _ExitTree()
	{
		base._ExitTree();

		// Wyloguj użytkownika przed zamknięciem (jeśli używamy Auth)
		if (authInterface != null && localEpicAccountId != null && localEpicAccountId.IsValid())
		{
			GD.Print("[EOSManager:Logout] Logging out user...");
			var logoutOptions = new Epic.OnlineServices.Auth.LogoutOptions()
			{
				LocalUserId = localEpicAccountId
			};
			authInterface.Logout(ref logoutOptions, null, OnLogoutComplete);
		}

		if (platformInterface != null)
		{
			GD.Print("[EOSManager] Releasing EOS Platform Interface...");
			platformInterface.Release();
			platformInterface = null;
		}

		PlatformInterface.Shutdown();
		GD.Print("[EOSManager] EOS SDK shutdown complete.");
	}

	// Callback po wylogowaniu
	/// <summary>
	/// Callback wylogowania z Auth, czyści lokalne ID konta po sukcesie.
	/// </summary>
	/// <param name="data">Informacje zwrotne z procesu wylogowania.</param>
	/// <remarks>Gdy wylogowanie nie powiedzie się.</remarks>
	private void OnLogoutComplete(ref Epic.OnlineServices.Auth.LogoutCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print("[EOSManager:Logout] Logout successful!");
			localEpicAccountId = null;
		}
		else
		{
			GD.PrintErr($"[EOSManager:Logout] Logout failed: {data.ResultCode}");
		}
	}

	// ============================================
	// UTILITY METHODS
	// ============================================

	/// <summary>
	/// Sprawdza czy użytkownik jest zalogowany do EOS.
	/// </summary>
	/// <returns>True, gdy lokalny ProductUserId istnieje i jest ważny.</returns>
	/// <remarks>Nie sprawdza stanu połączenia sieciowego ani sesji Auth.</remarks>
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
	/// <remarks>Gdy plik listy zwierząt nie istnieje.</remarks>
	/// <remarks>Gdy pliku nie można otworzyć do odczytu.</remarks>
	private void LoadAnimalNames()
	{
		string filePath = "res://assets/nicknames/Animals_Old.txt";

		if (!FileAccess.FileExists(filePath))
		{
			GD.PrintErr($"[EOSManager:Nicknames] File not found: {filePath}");
			return;
		}

		using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			GD.PrintErr($"[EOSManager:Nicknames] Cannot open file: {filePath}");
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

		GD.Print($"[EOSManager:Nicknames] Loaded {animalNames.Count} animal names from list");
	}

	/// <summary>
	/// Losuje unikalny nick zwierzaka (sprawdza duplikaty w lobby) ^w^
	/// </summary>
	/// <returns>Wygenerowany, unikalny pseudonim; fallback przy braku dostępnych nazw.</returns>
	/// <remarks>Gdy lista zwierzaków jest pusta i używany jest fallback.</remarks>
	private string GenerateUniqueAnimalNickname()
	{
		if (animalNames.Count == 0)
		{
			GD.PrintErr("[EOSManager:Nicknames] No animal names list found! Using fallback...");
			return $"Animal_{GD.Randi() % FallbackAnimalRandomMax}";
		}

		// Pobierz listę już zajętych nicków
		var usedNicknames = currentLobbyMembers
			.Where(m => m.ContainsKey("displayName"))
			.Select(m => m["displayName"].ToString())
			.ToHashSet();

		// Znajdź dostępne nicki
		var availableNicknames = animalNames
			.Where(name => !usedNicknames.Contains(name))
			.ToList();

		if (availableNicknames.Count > 0)
		{
			string randomAnimal = availableNicknames[(int)(GD.Randi() % availableNicknames.Count)];
			GD.Print($"[EOSManager:Nicknames] Rolled animal nickname: {randomAnimal} (available: {availableNicknames.Count}/{animalNames.Count})");
			return randomAnimal;
		}

		// Jeśli wszystkie próby się nie powiodły, dodaj losowy sufiks
		string fallbackAnimal = animalNames[(int)(GD.Randi() % animalNames.Count)];
		string uniqueNick = $"{fallbackAnimal}_{GD.Randi() % NicknameRandomMax}";
		GD.Print($"[EOSManager:Nicknames] Failed to roll a unique nickname, using fallback: {uniqueNick}");
		return uniqueNick;
	}

	/// <summary>
	/// Skraca userId do ostatnich N znaków dla czytelności logów.
	/// </summary>
	/// <param name="userId">Pełny identyfikator użytkownika.</param>
	/// <returns>Skrócony identyfikator lub "null" gdy przekazano pusty string.</returns>
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
	/// <remarks>Metoda sanitizuje wejście i przycina do dozwolonej długości.</remarks>
	public void SetPendingNickname(string nickname)
	{
		if (string.IsNullOrWhiteSpace(nickname))
		{
			GD.Print("[EOSManager:Nicknames] Nickname is empty, will use fallback");
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
			pendingNickname = "";
			return;
		}

		pendingNickname = sanitized;
		GD.Print($"[EOSManager:Nicknames] Pending nickname set to: {pendingNickname}");
	}

	/// <summary>
	/// Zwraca aktualnie ustawiony pending nickname (dla UI).
	/// </summary>
	/// <returns>Nickname, który zostanie użyty przy dołączeniu lub tworzeniu lobby.</returns>
	/// <remarks>Wartość może być pusta, jeśli nie ustawiono nickname.</remarks>
	/// <seealso cref="SetPendingNickname"/>
	public string GetPendingNickname()
	{
		return pendingNickname;
	}

	// ============================================
	// PROFILE ICONS MANAGEMENT
	// ============================================

	/// <summary>
	/// Przypisuje unikalną ikonę profilową dla gracza w zależności od drużyny
	/// </summary>
	/// <param name="team">Drużyna gracza</param>
	/// <returns>Numer ikony (1-5) lub 0 jeśli brak dostępnych</returns>
	private int AssignProfileIcon(Team team)
	{
		if (team == Team.None)
		{
			return 0; // Brak ikony dla neutralnej drużyny
		}

		// Upewnij się, że mamy aktualną listę używanych ikon
		RebuildUsedIcons();

		// Universal team używa niebieskich ikon (AI vs Human mode)
		var usedIcons = (team == Team.Blue || team == Team.Universal) ? usedBlueIcons : usedRedIcons;

		GD.Print($"[EOSManager:ProfileIcons] AssignProfileIcon for {team}: usedIcons = [{string.Join(", ", usedIcons)}]");

		// Znajdź pierwszą wolną ikonę
		for (int i = 1; i <= MaxProfileIconsPerTeam; i++)
		{
			if (!usedIcons.Contains(i))
			{
				usedIcons.Add(i);
				GD.Print($"[EOSManager:ProfileIcons] Assigned profile icon {i} for {team} team (verified no duplicates)");
				return i;
			}
		}

		GD.PrintErr($"[EOSManager:ProfileIcons] No available profile icons for {team} team! All icons used: [{string.Join(", ", usedIcons)}]");
		return 0;
	}

	/// <summary>
	/// Zwalnia ikonę profilową gracza
	/// </summary>
	/// <param name="team">Drużyna gracza</param>
	/// <param name="iconNumber">Numer ikony do zwolnienia</param>
	private void ReleaseProfileIcon(Team team, int iconNumber)
	{
		if (iconNumber == 0 || team == Team.None)
			return;

		// Universal team używa niebieskich ikon (AI vs Human mode)
		var usedIcons = (team == Team.Blue || team == Team.Universal) ? usedBlueIcons : usedRedIcons;
		if (usedIcons.Remove(iconNumber))
		{
			GD.Print($"[EOSManager:ProfileIcons] Released profile icon {iconNumber} for {team} team");
		}
	}

	/// <summary>
	/// Pobiera ścieżkę do tekstury ikony profilowej
	/// </summary>
	/// <param name="team">Drużyna</param>
	/// <param name="iconNumber">Numer ikony (1-5)</param>
	/// <returns>Ścieżka do pliku tekstury</returns>
	/// <remarks>Zwraca pusty string dla wartości 0 lub drużyny None.</remarks>
	/// <seealso cref="GetProfileIconPathForUser"/>
	public string GetProfileIconPath(Team team, int iconNumber)
	{
		if (iconNumber == 0 || team == Team.None)
			return "";

		// Universal team używa niebieskich ikon (AI vs Human mode)
		string colorPrefix = (team == Team.Blue || team == Team.Universal) ? "blue" : "red";
		return $"res://assets/profilePictures/Prof_{colorPrefix}_{iconNumber}.png";
	}
	/// <summary>
	/// Zwraca ścieżkę ikony profilowej przypisanej do wskazanego użytkownika w lobby.
	/// </summary>
	/// <param name="userId">Identyfikator użytkownika (ProductUserId).</param>
	/// <returns>Ścieżka do ikony lub pusty string, jeśli brak danych.</returns>
	/// <remarks>Wykorzystuje cache <see cref="currentLobbyMembers"/> oraz przydział drużyn.</remarks>
	/// <seealso cref="GetProfileIconPath"/>
	public string GetProfileIconPathForUser(string userId)
	{
		foreach (var member in currentLobbyMembers)
		{
			if (member.ContainsKey("userId") && member["userId"].ToString() == userId)
			{
				if (member.ContainsKey("profileIcon") && member.ContainsKey("team"))
				{
					int iconNumber = member["profileIcon"].As<int>();
					string teamStr = member["team"].ToString();
					if (!string.IsNullOrEmpty(teamStr) && Enum.TryParse<Team>(teamStr, out Team team))
					{
						// Universal używa niebieskich ikon
						if (team == Team.Blue || team == Team.Universal)
						{
							return GetProfileIconPath(Team.Blue, iconNumber);
						}
						else if (team == Team.Red)
						{
							return GetProfileIconPath(Team.Red, iconNumber);
						}
					}
				}
			}
		}
		return "";
	}

	/// <summary>
	/// Odbudowuje listę używanych ikon na podstawie obecnych członków lobby
	/// </summary>
	private void RebuildUsedIcons()
	{
		usedBlueIcons.Clear();
		usedRedIcons.Clear();

		try
		{
			foreach (var member in currentLobbyMembers)
			{
				if (!member.ContainsKey("profileIcon") || !member.ContainsKey("team"))
					continue;

				int iconNumber = 0;
				try
				{
					iconNumber = member["profileIcon"].As<int>();
				}
				catch
				{
					// Spróbuj parsować jako string
					string iconStr = member["profileIcon"].ToString();
					if (!string.IsNullOrEmpty(iconStr))
					{
						int.TryParse(iconStr, out iconNumber);
					}
				}

				if (iconNumber > 0 && member.ContainsKey("team"))
				{
					string teamStr = member["team"].ToString();
					if (!string.IsNullOrEmpty(teamStr) && Enum.TryParse<Team>(teamStr, out Team team))
					{
						// Universal używa niebieskich ikon
						if (team == Team.Blue || team == Team.Universal)
						{
							usedBlueIcons.Add(iconNumber);
						}
						else if (team == Team.Red)
						{
							usedRedIcons.Add(iconNumber);
						}
					}
				}
			}

			GD.Print($"[EOSManager:ProfileIcons] Rebuilt used icons: Blue={string.Join(",", usedBlueIcons)}, Red={string.Join(",", usedRedIcons)}");
		}
		catch (System.Exception e)
		{
			GD.PrintErr($"[EOSManager:ProfileIcons] Error in RebuildUsedIcons: {e.Message}");
		}
	}



	/// <summary>
	/// Tworzy nowe lobby z opcjonalnym custom ID i ustawia podstawowe atrybuty.
	/// </summary>
	/// <param name="customLobbyId">Custom ID lobby używany w wyszukiwaniu.</param>
	/// <param name="maxPlayers">Maksymalna liczba graczy (2-64).</param>
	/// <param name="isPublic">Czy lobby ma być publiczne i wyszukiwalne.</param>
	/// <remarks>Metoda wymaga zalogowanego użytkownika i braku aktywnego lobby.</remarks>
	/// <seealso cref="OnCreateLobbyComplete"/>
	/// <seealso cref="SetLobbyAttribute(string, string)"/>
	/// <remarks>Gdy użytkownik nie jest zalogowany, już jest w lobby lub tworzenie lobby jest w toku.</remarks>
	public void CreateLobby(string customLobbyId, uint maxPlayers = 10, bool isPublic = true)
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("[EOSManager:LobbyCreate] Cannot create lobby: User not logged in!");
			EmitSignal(SignalName.LobbyCreationFailed, "User not logged in");
			return;
		}

		// Sprawdź czy użytkownik już jest w lobby
		if (!string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintRich("[color=yellow][EOSManager:LobbyCreate] Cannot create lobby: You are already in a lobby!");
			GD.PrintRich($"[color=yellow][EOSManager:LobbyCreate] Current lobby: {currentLobbyId} (Owner: {isLobbyOwner})");
			GD.PrintRich("[color=yellow][EOSManager:LobbyCreate] Please leave the current lobby first.");
			EmitSignal(SignalName.LobbyCreationFailed, "Already in a lobby");
			return;
		}

		// NOWE: Sprawdź czy lobby już jest tworzone
		if (isCreatingLobby)
		{
			GD.PrintErr("[EOSManager:LobbyCreate] Cannot create lobby: Lobby creation already in progress!");
			EmitSignal(SignalName.LobbyCreationFailed, "Lobby creation already in progress");
			return;
		}

		// Zapisz custom lobby ID
		currentCustomLobbyId = customLobbyId;
		GD.Print($"[EOSManager:LobbyCreate] Creating lobby with custom ID: {customLobbyId}, Max players: {maxPlayers}, Public: {isPublic}");

		// Zablokuj tworzenie lobby
		isCreatingLobby = true;

		// Automatycznie wygeneruj unikalny nick zwierzaka! ^w^
		pendingNickname = GenerateUniqueAnimalNickname();
		GD.Print($"[EOSManager:Nicknames] Your nickname: {pendingNickname}");

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
	/// <remarks>Gdy nie ma aktywnego lobby, brak LobbyDetails lub uchwyt jest null.</remarks>
	public Godot.Collections.Dictionary GetAllLobbyAttributes()
	{
		var attributes = new Godot.Collections.Dictionary();

		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("[EOSManager:LobbyAttributes] Cannot get lobby attributes: Not in any lobby!");
			return attributes;
		}

		if (!foundLobbyDetails.ContainsKey(currentLobbyId))
		{
			GD.PrintErr($"[EOSManager:LobbyAttributes] Lobby details not found for ID: {currentLobbyId}");
			return attributes;
		}

		LobbyDetails lobbyDetails = foundLobbyDetails[currentLobbyId];

		if (lobbyDetails == null)
		{
			GD.PrintErr("[EOSManager:LobbyAttributes] Lobby details is null!");
			return attributes;
		}

		// Pobierz liczbę atrybutów
		var countOptions = new LobbyDetailsGetAttributeCountOptions();
		uint attributeCount = lobbyDetails.GetAttributeCount(ref countOptions);

		GD.Print($"[EOSManager:LobbyAttributes] Getting {attributeCount} lobby attributes...");

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
				GD.Print($"[EOSManager:LobbyAttributes]  [{i}] {key} = '{value}'");
			}
		}

		return attributes;
	}

	/// <summary>
	/// Callback tworzenia lobby – zapisuje stan bieżącego lobby, ustawia atrybuty i emituje sygnały UI.
	/// </summary>
	/// <param name="data">Informacje zwrotne z operacji tworzenia lobby.</param>
	/// <seealso cref="CacheCurrentLobbyDetailsHandle(string)"/>
	/// <seealso cref="SetLobbyAttribute(string, string)"/>
	/// <seealso cref="SetMemberAttribute"/>
	/// <seealso cref="GetLobbyMembers"/>
	/// <remarks>Gdy tworzenie lobby nie powiedzie się.</remarks>
	private void OnCreateLobbyComplete(ref CreateLobbyCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print($"[EOSManager:LobbyCreate] Lobby created successfully! EOS Lobby ID: {data.LobbyId}");
			GD.Print($"[EOSManager:LobbyCreate] Custom Lobby ID: {currentCustomLobbyId}");

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
				GD.Print($"[EOSManager:Nicknames] Setting host nickname: {pendingNickname}");
				SetMemberAttribute("Nickname", pendingNickname);

				GetTree().CreateTimer(0.8).Timeout += () =>
				{
					GD.Print("[EOSManager:Nicknames] Assigning to Neutral team...");
					SetMemberAttribute("Team", Team.None.ToString());

					// Po ustawieniu nicku i drużyny, odśwież i DOPIERO zmień scenę
					GetTree().CreateTimer(0.8).Timeout += () =>
					{
						GetLobbyMembers();

						GetTree().CreateTimer(0.3).Timeout += () =>
						{
							GD.Print("[EOSManager:LobbyCreate] Host setup complete, emitting LobbyCreated signal");
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
					GD.Print("[EOSManager:Nicknames] Assigning to Neutral team...");
					SetMemberAttribute("Team", Team.None.ToString());

					// Po ustawieniu drużyny, odśwież i zmień scenę
					GetTree().CreateTimer(0.8).Timeout += () =>
					{
						GetLobbyMembers();

						GetTree().CreateTimer(0.3).Timeout += () =>
						{
							GD.Print("[EOSManager:LobbyCreate] Host setup complete, emitting LobbyCreated signal");
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
				{ "team", "" }, // Jeszcze nie przypisany
				{ "profileIcon", 0 } // Brak ikony na początku
			};
			tempMembersList.Add(tempMemberData);

			// Zapisz do cache
			currentLobbyMembers = tempMembersList;

			EmitSignal(SignalName.LobbyMembersUpdated, tempMembersList);
		}
		else
		{
			GD.PrintErr($"[EOSManager:LobbyCreate] Failed to create lobby: {data.ResultCode}");
			// Wyślij sygnał o błędzie do UI
			EmitSignal(SignalName.LobbyCreationFailed, data.ResultCode.ToString());
		}

		// NOWE: Odblokuj tworzenie lobby (niezależnie od sukcesu czy błędu)
		isCreatingLobby = false;
	}

	/// <summary>
	/// Wyszukuje dostępne lobby
	/// </summary>
	/// <remarks>Wysyła wynik przez sygnał <see cref="LobbyListUpdated"/>.</remarks>
	/// <remarks>Gdy użytkownik nie jest zalogowany lub utworzenie wyszukiwania się nie powiedzie.</remarks>
	public void SearchLobbies()
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("[EOSManager:LobbySearch] Cannot search lobbies: User not logged in!");
			return;
		}

		GD.Print("[EOSManager:LobbySearch] Searching for lobbies...");
		// Utwórz wyszukiwanie
		var createLobbySearchOptions = new CreateLobbySearchOptions()
		{
			MaxResults = 25 // Maksymalnie 25 wyników
		};

		Result result = lobbyInterface.CreateLobbySearch(ref createLobbySearchOptions, out LobbySearch lobbySearch);

		if (result != Result.Success || lobbySearch == null)
		{
			GD.PrintErr($"[EOSManager:LobbySearch] Failed to create lobby search: {result}");
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
				GD.PrintErr($"[EOSManager:LobbySearch] Lobby search failed: {findData.ResultCode}");
			}

			lobbySearch.Release();
		});
	}

	/// <summary>
	/// Wyszukuje lobby po custom ID
	/// </summary>
	/// <param name="customLobbyId">Custom ID lobby do wyszukania (np. "V5CGSP")</param>
	/// <param name="onComplete">Callback wywoływany po zakończeniu (success: bool, lobbyId: string)</param>
	/// <remarks>Callback zwraca wynik niezależnie od powodzenia, aby UI mogło zareagować.</remarks>
	/// <seealso cref="SearchLobbies"/>
	/// <remarks>Gdy użytkownik nie jest zalogowany lub wyszukiwanie/copy LobbyDetails zwraca błąd.</remarks>
	public void SearchLobbyByCustomId(string customLobbyId, Action<bool, string> onComplete = null)
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("[EOSManager:LobbySearch] Cannot search lobby: User not logged in!");
			onComplete?.Invoke(false, "");
			return;
		}

		GD.Print($"[EOSManager:LobbySearch] Searching for lobby with custom ID: {customLobbyId}...");

		// Utwórz wyszukiwanie
		var createLobbySearchOptions = new CreateLobbySearchOptions()
		{
			MaxResults = 25
		};

		Result result = lobbyInterface.CreateLobbySearch(ref createLobbySearchOptions, out LobbySearch lobbySearch);

		if (result != Result.Success || lobbySearch == null)
		{
			GD.PrintErr($"[EOSManager:LobbySearch] Failed to create lobby search: {result}");
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
						GD.PrintErr("[EOSManager:LobbySearch] Failed to copy lobby details");
						onComplete?.Invoke(false, "");
					}
				}
				else
				{
					GD.Print($"[EOSManager:LobbySearch] No lobby found with custom ID: {customLobbyId}");
					onComplete?.Invoke(false, "");
				}
			}
			else
			{
				GD.PrintErr($"[EOSManager:LobbySearch] Lobby search failed: {findData.ResultCode}");
				onComplete?.Invoke(false, "");
			}

			lobbySearch.Release();
		});
	}

	/// <summary>
	/// Wyszukuje i dołącza do lobby po custom ID
	/// </summary>
	/// <param name="customLobbyId">Custom ID lobby (np. "V5CGSP")</param>
	/// <remarks>W przypadku niepowodzenia emituje <see cref="LobbyJoinFailed"/>.</remarks>
	/// <seealso cref="SearchLobbyByCustomId"/>
	/// <seealso cref="JoinLobby(string)"/>
	/// <remarks>Gdy lobby o podanym Custom ID nie zostanie znalezione.</remarks>
	public void JoinLobbyByCustomId(string customLobbyId)
	{
		SearchLobbyByCustomId(customLobbyId, (success, lobbyId) =>
		{
			if (success && !string.IsNullOrEmpty(lobbyId))
			{
				GD.Print($"[EOSManager:LobbyJoin] Joining lobby with custom ID: {customLobbyId}");
				JoinLobby(lobbyId);
			}
			else
			{
				GD.PrintErr($"[EOSManager:LobbyJoin] Cannot join: Lobby with custom ID '{customLobbyId}' not found!");

				// Wyślij sygnał o błędzie do UI
				EmitSignal(SignalName.LobbyJoinFailed, $"Lobby '{customLobbyId}' nie istnieje");
			}
		});
	}

	/// <summary>
	/// Dołącza do lobby po indeksie z ostatniego wyszukania
	/// </summary>
	/// <param name="lobbyIndex">Indeks lobby z listy (0, 1, 2...)</param>
	/// <remarks>Korzysta z cache wyszukiwania lobby w <see cref="foundLobbyIds"/>.</remarks>
	/// <seealso cref="JoinLobby(string)"/>
	/// <remarks>Gdy użytkownik nie jest zalogowany lub indeks lobby jest nieprawidłowy.</remarks>
	public void JoinLobbyByIndex(int lobbyIndex)
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("[EOSManager:LobbyJoin] Cannot join lobby: User not logged in!");
			return;
		}

		if (lobbyIndex < 0 || lobbyIndex >= foundLobbyIds.Count)
		{
			GD.PrintErr($"[EOSManager:LobbyJoin] Invalid lobby index: {lobbyIndex}. Found lobbies: {foundLobbyIds.Count}");
			return;
		}

		string lobbyId = foundLobbyIds[lobbyIndex];
		JoinLobby(lobbyId);
	}

	/// <summary>
	/// Dołącza do lobby po ID
	/// </summary>
	/// <param name="lobbyId">ID lobby do dołączenia</param>
	/// <remarks>Wymaga aktualnego <see cref="foundLobbyDetails"/> lub odświeża dane wyszukiwania.</remarks>
	/// <seealso cref="JoinLobbyByIndex"/>
	/// <seealso cref="JoinLobbyByCustomId"/>
	/// <seealso cref="OnJoinLobbyComplete"/>
	/// <remarks>Gdy użytkownik nie jest zalogowany, jest już w lobby lub brak LobbyDetails w cache.</remarks>
	public void JoinLobby(string lobbyId)
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("[EOSManager:LobbyJoin] Cannot join lobby: User not logged in!");
			return;
		}

		// Sprawdź czy użytkownik już jest w lobby
		if (!string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("[EOSManager:LobbyJoin] Cannot join lobby: You are already in a lobby!");
			return;
		}

		if (!foundLobbyDetails.ContainsKey(lobbyId))
		{
			GD.PrintErr($"[EOSManager:LobbyJoin] Lobby details not found for ID: {lobbyId}");
			return;
		}

		GD.Print($"[EOSManager:LobbyJoin] Joining lobby: {lobbyId}");

		// Ustaw flagę że trwa dołączanie do lobby
		isJoiningLobby = true;

		// Automatycznie wygeneruj unikalny nick zwierzaka! ^w^
		pendingNickname = GenerateUniqueAnimalNickname();
		GD.Print($"[EOSManager:Nickname] Your nickname: {pendingNickname}");

		var joinLobbyOptions = new JoinLobbyOptions()
		{
			LobbyDetailsHandle = foundLobbyDetails[lobbyId],
			LocalUserId = localProductUserId,
			PresenceEnabled = false
		};

		lobbyInterface.JoinLobby(ref joinLobbyOptions, null, OnJoinLobbyComplete);
	}

	/// <summary>
	/// Callback dołączenia do lobby – aktualizuje bieżący stan, synchronizuje atrybuty oraz uruchamia sekwencję inicjalizacji gracza.
	/// </summary>
	/// <param name="data">Informacje zwrotne z operacji JoinLobby.</param>
	/// <seealso cref="CacheCurrentLobbyDetailsHandle(string)"/>
	/// <seealso cref="RefreshCurrentLobbyInfo"/>
	/// <seealso cref="GetLobbyMembers"/>
	/// <seealso cref="SetMemberAttribute"/>
	/// <seealso cref="AssignToNeutralTeam"/>
	/// <seealso cref="AssignToUniversalTeam"/>
	/// <remarks>Gdy JoinLobby zwróci błąd.</remarks>
	private void OnJoinLobbyComplete(ref JoinLobbyCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print($"[EOSManager:LobbyJoin] Successfully joined lobby: {data.LobbyId}");

			// Zapisz obecne lobby
			currentLobbyId = data.LobbyId.ToString();
			isLobbyOwner = false;

			// KROK 1: Skopiuj LobbyDetails handle natychmiast
			CacheCurrentLobbyDetailsHandle("join");

			// KROK 2: Poczekaj na synchronizację danych z backendu (0.5s zamiast 1.5s)
			GetTree().CreateTimer(0.5).Timeout += () =>
			{
				GD.Print("[EOSManager:LobbyJoin] [STEP 1/5] Refreshing lobby info and CustomLobbyId...");

				// Odśwież handle aby mieć najświeższe dane
				CacheCurrentLobbyDetailsHandle("refresh_after_join");

				// Odśwież informacje o lobby (łącznie z CustomLobbyId)
				RefreshCurrentLobbyInfo();

				// KROK 3: Pobierz członków NAJPIERW (żeby AutoAssignMyTeam miał dane)
				GetTree().CreateTimer(0.3).Timeout += () =>
				{
					GD.Print("[EOSManager:LobbyJoin] [STEP 2/5] Fetching current lobby members...");
					GetLobbyMembers();

					// KROK 4: Ustaw nickname i przypisz drużynę (teraz mamy już listę członków)
					GetTree().CreateTimer(0.3).Timeout += () =>
					{
						GD.Print("[EOSManager:LobbyJoin] [STEP 3/5] Setting nickname first...");

						// Najpierw ustaw nickname (jeśli został ustawiony)
						if (!string.IsNullOrEmpty(pendingNickname))
						{
							GD.Print($"[EOSManager:LobbyJoin] Setting nickname: {pendingNickname}");
							SetMemberAttribute("Nickname", pendingNickname);

							// Odczekaj na propagację nicku, potem przypisz drużynę
							GetTree().CreateTimer(0.5).Timeout += () =>
							{
								GD.Print("[EOSManager:LobbyJoin] [STEP 3.5/5] Now assigning to neutral team...");
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
							GD.Print("[EOSManager:LobbyJoin] [STEP 4/5] Refreshing members with team assignments...");
							GetLobbyMembers();

							// KROK 6: Wyślij sygnał do UI (zmień scenę)
							GetTree().CreateTimer(0.3).Timeout += () =>
							{
								GD.Print("[EOSManager:LobbyJoin] [STEP 5/5] All synchronization complete, emitting LobbyJoined signal");
								isJoiningLobby = false; // Zakończono dołączanie
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
			GD.PrintErr($"[EOSManager:LobbyJoin] Failed to join lobby: {data.ResultCode}");

			// Wyczyść flagę dołączania
			isJoiningLobby = false;

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
	/// <seealso cref="RefreshCurrentLobbyInfo"/>
	/// <seealso cref="GetLobbyMembers"/>
	/// <seealso cref="CacheCurrentLobbyDetailsHandle(string)"/>
	/// <remarks>Gdy utworzenie wyszukiwania, ustawienie filtra lub kopia wyników się nie powiedzie.</remarks>
	private void SearchLobbiesAndRefresh()
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.Print("[EOSManager:LobbySearch] Cannot refresh - no current lobby ID");
			return;
		}

		// Czekamy chwilę żeby backend zdążył zsynchronizować dane
		GetTree().CreateTimer(1.5).Timeout += () =>
		{
			GD.Print($"[EOSManager:LobbySearch] Searching for current lobby {currentLobbyId} to get fresh data...");

			var createLobbySearchOptions = new Epic.OnlineServices.Lobby.CreateLobbySearchOptions
			{
				MaxResults = 100
			};

			var searchResult = lobbyInterface.CreateLobbySearch(ref createLobbySearchOptions, out var lobbySearchHandle);
			if (searchResult != Epic.OnlineServices.Result.Success || lobbySearchHandle == null)
			{
				GD.PrintErr($"[EOSManager:LobbySearch] Failed to create lobby search: {searchResult}");
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
				GD.PrintErr($"[EOSManager:LobbySearch] Failed to set lobby ID filter: {setIdResult}");
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
			GD.PrintErr($"[EOSManager:LobbySearch] Lobby search failed: {data.ResultCode}");
			return;
		}

		var getSearchResultCountOptions = new Epic.OnlineServices.Lobby.LobbySearchGetSearchResultCountOptions();
		uint resultCount = lobbySearchHandle.GetSearchResultCount(ref getSearchResultCountOptions);

		if (resultCount == 0)
		{
			GD.PrintErr("[EOSManager:LobbySearch] Current lobby not found in search results");
			return;
		}

		GD.Print("[EOSManager:LobbySearch] Found current lobby, getting fresh LobbyDetails handle...");
		// Pobierz ŚWIEŻY handle z wyników search
		var copyResultOptions = new Epic.OnlineServices.Lobby.LobbySearchCopySearchResultByIndexOptions
		{
			LobbyIndex = 0
		};

		var copyResult = lobbySearchHandle.CopySearchResultByIndex(ref copyResultOptions, out var freshLobbyDetails);
		if (copyResult != Epic.OnlineServices.Result.Success || freshLobbyDetails == null)
		{
			GD.PrintErr($"[EOSManager:LobbySearch] Failed to copy search result: {copyResult}");
			return;
		}

		// ⚠️ NIE nadpisuj handle jeśli już działa!
		// Handle z WebSocket (member_update) ma pełne dane, a ten z search może być pusty
		if (!foundLobbyDetails.ContainsKey(currentLobbyId))
		{
			foundLobbyDetails[currentLobbyId] = freshLobbyDetails;
			GD.Print("[EOSManager:LobbySearch] LobbyDetails handle added from backend!");
		}
		else
		{
			// Sprawdź czy nowy handle ma RZECZYWISTE dane (nie tylko count)
			var testOptions = new LobbyDetailsGetMemberCountOptions();
			uint newCount = freshLobbyDetails.GetMemberCount(ref testOptions);
			uint oldCount = foundLobbyDetails[currentLobbyId].GetMemberCount(ref testOptions);

			// Testuj czy GetMemberByIndex działa na NOWYM handle
			bool newHandleValid = false;
			if (newCount > 0)
			{
				var testMemberOptions = new LobbyDetailsGetMemberByIndexOptions() { MemberIndex = 0 };
				ProductUserId testUserId = freshLobbyDetails.GetMemberByIndex(ref testMemberOptions);
				newHandleValid = testUserId != null && testUserId.IsValid();
				GD.Print($"[EOSManager:LobbySearch] hHandle validity test: UserID={(testUserId != null ? testUserId.ToString() : "NULL")} Valid={newHandleValid}");
			}

			// Tylko zamień jeśli nowy handle FAKTYCZNIE działa
			if (newHandleValid && newCount >= oldCount)
			{
				foundLobbyDetails[currentLobbyId]?.Release();
				foundLobbyDetails[currentLobbyId] = freshLobbyDetails;
				GD.Print("[EOSManager:LobbySearch] LobbyDetails handle refreshed from backend (validated)!");
			}
			else
			{
				freshLobbyDetails?.Release();
				GD.Print("[EOSManager:LobbySearch] Keeping old handle (new handle invalid or has less data)");
			}
		}

		// Teraz możemy bezpiecznie odczytać członków
		CallDeferred(nameof(RefreshCurrentLobbyInfo));
		CallDeferred(nameof(GetLobbyMembers));
	});
		};
	}

	/// <summary>
	/// Opuszcza obecne lobby, korzystając z zapisanego identyfikatora bieżącego lobby.
	/// </summary>
	/// <remarks>Wywołuje <see cref="LeaveLobby(string)"/> z bieżącym <see cref="currentLobbyId"/>.</remarks>
	/// <seealso cref="LeaveLobby(string)"/>
	/// <seealso cref="OnLeaveLobbyComplete"/>
	/// <remarks>Gdy brak aktywnego lobby do opuszczenia.</remarks>
	public void LeaveLobby()
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("[EOSManager:LobbyLeave] Cannot leave lobby: Not in any lobby!");
			return;
		}

		LeaveLobby(currentLobbyId);
	}

	/// <summary>
	/// Opuszcza wskazane lobby po jego identyfikatorze.
	/// </summary>
	/// <param name="lobbyId">ID lobby do opuszczenia.</param>
	/// <remarks>Po opuszczeniu emituje <see cref="LobbyLeft"/> oraz czyści cache.</remarks>
	/// <seealso cref="OnLeaveLobbyComplete"/>
	/// <remarks>Gdy użytkownik nie jest zalogowany.</remarks>
	public void LeaveLobby(string lobbyId)
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("[EOSManager:LobbyLeave] Cannot leave lobby: User not logged in!");
			return;
		}

		GD.Print($"[EOSManager:LobbyLeave] Leaving lobby: {lobbyId}");
		var leaveLobbyOptions = new LeaveLobbyOptions()
		{
			LobbyId = lobbyId,
			LocalUserId = localProductUserId
		};

		lobbyInterface.LeaveLobby(ref leaveLobbyOptions, null, OnLeaveLobbyComplete);
	}

	/// <summary>
	/// Callback opuszczenia lobby – czyści lokalny stan, resetuje atrybuty i emituje sygnał UI.
	/// </summary>
	/// <param name="data">Informacje zwrotne z operacji LeaveLobby.</param>
	/// <seealso cref="LeaveLobby()"/>
	/// <seealso cref="LeaveLobby(string)"/>
	/// <remarks>Gdy opuszczenie lobby zakończy się błędem.</remarks>
	private void OnLeaveLobbyComplete(ref LeaveLobbyCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print($"[EOSManager:LobbyLeave] Successfully left lobby: {data.LobbyId}");

			// Zatrzymaj timer
			if (lobbyRefreshTimer != null && lobbyRefreshTimer.TimeLeft > 0)
			{
				lobbyRefreshTimer.Stop();
				GD.Print("[EOSManager:LobbyLeave] Lobby refresh timer stopped");
			}

			// Wyczyść obecne lobby
			currentLobbyId = null;
			isLobbyOwner = false;
			isLocalPlayerInLobbyView = true; // Reset

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
			currentLobbyMembers.Clear();

			// Wyczyść ikony profilowe
			usedBlueIcons.Clear();
			usedRedIcons.Clear();

			// Wyczyść flagę tworzenia (na wszelki wypadek)
			isCreatingLobby = false;
			forcedTeamAssignments.Clear();

			// Wyślij sygnał do UI
			EmitSignal(SignalName.LobbyLeft);
		}
		else
		{
			GD.PrintErr($"[EOSManager:LobbyLeave] Failed to leave lobby: {data.ResultCode}");
		}
	}

	/// <summary>
	/// Wyrzuca gracza z lobby (tylko host może to zrobić!) >:3
	/// </summary>
	/// <param name="targetUserId">Identyfikator ProductUserId gracza do wyrzucenia.</param>
	/// <remarks>Po sukcesie wywołuje odświeżenie listy członków.</remarks>
	/// <seealso cref="OnKickMemberComplete"/>
	/// <remarks>Gdy nie ma aktywnego lobby, gracz nie jest hostem lub próbuje wyrzucić samego siebie.</remarks>
	public void KickPlayer(string targetUserId)
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("[EOSManager:LobbyKick] Cannot kick player: Not in any lobby!");
			return;
		}

		if (!isLobbyOwner)
		{
			GD.PrintErr("[EOSManager:LobbyKick] Cannot kick player: You are not the host!");
			return;
		}

		if (targetUserId == localProductUserId.ToString())
		{
			GD.PrintErr("[EOSManager:LobbyKick] Cannot kick yourself!");
			return;
		}

		GD.Print($"[EOSManager:LobbyKick] Kicking player: {targetUserId} from lobby {currentLobbyId}");
		var kickMemberOptions = new KickMemberOptions()
		{
			LobbyId = currentLobbyId,
			LocalUserId = localProductUserId,
			TargetUserId = ProductUserId.FromString(targetUserId)
		};

		lobbyInterface.KickMember(ref kickMemberOptions, null, OnKickMemberComplete);
	}

	/// <summary>
	/// Callback wyrzucenia gracza – po sukcesie odświeża cache lobby i listę członków.
	/// </summary>
	/// <param name="data">Informacje zwrotne z operacji KickMember.</param>
	/// <seealso cref="KickPlayer"/>
	/// <seealso cref="CacheCurrentLobbyDetailsHandle(string)"/>
	/// <seealso cref="GetLobbyMembers"/>
	/// <remarks>Gdy wyrzucenie gracza zwróci błąd.</remarks>
	private void OnKickMemberComplete(ref KickMemberCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print($"[EOSManager:LobbyKick] Successfully kicked player from lobby: {data.LobbyId}");

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
			GD.PrintErr($"[EOSManager:LobbyKick] Failed to kick player: {data.ResultCode}");
		}
	}

	/// <summary>
	/// Przekazuje rolę hosta innemu graczowi (tylko host może to zrobić!)
	/// </summary>
	/// <param name="targetUserId">Identyfikator ProductUserId gracza, który ma zostać hostem.</param>
	/// <remarks>Po sukcesie emituje sygnał <see cref="LobbyOwnerChanged"/>.</remarks>
	/// <seealso cref="OnPromoteMemberComplete"/>
	/// <remarks>Gdy brak lobby, gracz nie jest hostem lub wskazuje samego siebie.</remarks>
	public void TransferLobbyOwnership(string targetUserId)
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("[EOSManager:LobbyOwnership] Cannot transfer ownership: Not in any lobby!");
			return;
		}

		if (!isLobbyOwner)
		{
			GD.PrintErr("[EOSManager:LobbyOwnership] Cannot transfer ownership: You are not the host!");
			return;
		}

		if (targetUserId == localProductUserId.ToString())
		{
			GD.PrintErr("[EOSManager:LobbyOwnership] Cannot transfer ownership to yourself!");
			return;
		}

		GD.Print($"[EOSManager:LobbyOwnership] Transferring lobby ownership to: {targetUserId}");
		var promoteMemberOptions = new PromoteMemberOptions()
		{
			LobbyId = currentLobbyId,
			LocalUserId = localProductUserId,
			TargetUserId = ProductUserId.FromString(targetUserId)
		};

		lobbyInterface.PromoteMember(ref promoteMemberOptions, null, OnPromoteMemberComplete);
	}

	/// <summary>
	/// Callback przekazania hosta – aktualizuje stan własności i odświeża listę członków.
	/// </summary>
	/// <param name="data">Informacje zwrotne z operacji PromoteMember.</param>
	/// <seealso cref="TransferLobbyOwnership"/>
	/// <seealso cref="CacheCurrentLobbyDetailsHandle(string)"/>
	/// <seealso cref="GetLobbyMembers"/>
	/// <remarks>Gdy przekazanie hosta się nie powiedzie.</remarks>
	private void OnPromoteMemberComplete(ref PromoteMemberCallbackInfo data)
	{
		if (data.ResultCode == Result.Success)
		{
			GD.Print($"[EOSManager:LobbyOwnership] Successfully transferred ownership in lobby: {data.LobbyId}");
			GD.Print($"[EOSManager:LobbyOwnership] You are no longer the host!");

			// Zaktualizuj lokalny stan - już nie jesteśmy hostem
			isLobbyOwner = false;

			// Odśwież cache i listę członków po transferze
			GetTree().CreateTimer(0.3).Timeout += () =>
			{
				CacheCurrentLobbyDetailsHandle("after_promote");
				GetTree().CreateTimer(0.1).Timeout += () =>
				{
					GetLobbyMembers();
				};
			};
		}
		else
		{
			GD.PrintErr($"[EOSManager:LobbyOwnership] Failed to transfer ownership: {data.ResultCode}");
		}
	}

	// ============================================
	// NASŁUCHIWANIE NA ZMIANY W LOBBY
	// ============================================

	/// <summary>Identyfikator subskrypcji ogólnych aktualizacji lobby.</summary>
	private ulong lobbyUpdateNotificationId = 0;
	/// <summary>Identyfikator subskrypcji aktualizacji atrybutów członków lobby.</summary>
	private ulong lobbyMemberUpdateNotificationId = 0;
	/// <summary>Identyfikator subskrypcji zmian statusu członków lobby.</summary>
	private ulong lobbyMemberStatusNotificationId = 0;

	/// <summary>
	/// Rejestruje nasłuchiwanie zdarzeń lobby (zmiany atrybutów, statusu i członków).
	/// </summary>
	/// <seealso cref="OnLobbyUpdateReceived"/>
	/// <seealso cref="OnLobbyMemberUpdateReceived"/>
	/// <seealso cref="OnLobbyMemberStatusReceived"/>
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

		GD.Print("[EOSManager:LobbyNotifications] Lobby update notifications added");
	}

	/// <summary>
	/// Reaguje na ogólne aktualizacje lobby, odświeżając cache i atrybuty.
	/// </summary>
	/// <param name="data">Informacje o aktualizacji lobby.</param>
	/// <seealso cref="CacheCurrentLobbyDetailsHandle(string)"/>
	/// <seealso cref="RefreshCurrentLobbyInfo"/>
	/// <seealso cref="ApplyForcedTeamAssignments"/>
	private void OnLobbyUpdateReceived(ref LobbyUpdateReceivedCallbackInfo data)
	{
		GD.Print($"[EOSManager:LobbyUpdate] Lobby updated: {data.LobbyId}");

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

	/// <summary>
	/// Obsługuje aktualizacje atrybutów członków lobby, odświeżając listę graczy.
	/// </summary>
	/// <param name="data">Informacje o aktualizacji atrybutów członka lobby.</param>
	/// <seealso cref="CacheCurrentLobbyDetailsHandle(string)"/>
	/// <seealso cref="GetLobbyMembers"/>
	private void OnLobbyMemberUpdateReceived(ref LobbyMemberUpdateReceivedCallbackInfo data)
	{
		GD.Print($"[EOSManager:LobbyMemberUpdate] Lobby member updated in: {data.LobbyId}, User: {data.TargetUserId}");
		if (currentLobbyId != data.LobbyId.ToString()) return;

		GD.Print("[EOSManager:LobbyMemberUpdate] Member update detected - refreshing member list");

		// Odśwież LobbyDetails handle i listę członków
		CacheCurrentLobbyDetailsHandle("member_update");

		// Małe opóźnienie na synchronizację EOS
		GetTree().CreateTimer(0.2).Timeout += () =>
		{
			GetLobbyMembers();
		};
	}

	/// <summary>
	/// Obsługuje zmiany statusu członków (join/leave/kick/promote) i aktualizuje stan lokalny.
	/// </summary>
	/// <param name="data">Informacje o zmianie statusu członka lobby.</param>
	/// <seealso cref="HandleKickedFromLobby"/>
	/// <seealso cref="CacheCurrentLobbyDetailsHandle(string)"/>
	/// <seealso cref="GetLobbyMembers"/>
	private void OnLobbyMemberStatusReceived(ref LobbyMemberStatusReceivedCallbackInfo data)
	{
		GD.Print($"[EOSManager:LobbyMemberStatus] Lobby member status changed in: {data.LobbyId}, User: {data.TargetUserId}, Status: {data.CurrentStatus}");

		// NAJPIERW sprawdź czy to MY zostaliśmy wyrzuceni (zanim sprawdzimy currentLobbyId!)
		if (data.CurrentStatus == LobbyMemberStatus.Kicked &&
			data.TargetUserId.ToString() == localProductUserId.ToString())
		{
			GD.Print("[EOSManager:LobbyMemberStatus] You have been kicked from the lobby");
			CallDeferred(nameof(HandleKickedFromLobby));
			return; // Ignoruj wszystkie dalsze eventy
		}

		// Sprawdź czy ktoś został awansowany na hosta
		if (data.CurrentStatus == LobbyMemberStatus.Promoted)
		{
			string promotedUserId = data.TargetUserId.ToString();
			GD.Print($"[EOSManager:LobbyMemberStatus] Member promoted to host: {GetShortUserId(promotedUserId)}");
			EmitSignal(SignalName.LobbyOwnerChanged);

			// Jeśli to MY zostaliśmy awansowani
			if (promotedUserId == localProductUserId.ToString())
			{
				GD.Print("[EOSManager:LobbyMemberStatus] You have been promoted to lobby owner");
				isLobbyOwner = true;

				if (isLocalPlayerInLobbyView)
				{
					UnlockLobby();
					ResetGameSession();
				}
			}
			else
			{
				GD.Print($"[EOSManager:LobbyMemberStatus] {GetShortUserId(promotedUserId)} is now the lobby owner");
				isLobbyOwner = false;
			}
		}

		// Jeśli to nasze lobby (i nie zostaliśmy wyrzuceni)
		if (!string.IsNullOrEmpty(currentLobbyId) && currentLobbyId == data.LobbyId.ToString())
		{
			string userId = data.TargetUserId.ToString();

			// Obsługa KICKED - ktoś INNY został wyrzucony
			if (data.CurrentStatus == LobbyMemberStatus.Kicked)
			{
				GD.Print($"[EOSManager:LobbyMemberStatus] Member KICKED: {GetShortUserId(userId)}");
			}

			// Odśwież LobbyDetails handle (tylko jeśli nie zostaliśmy wyrzuceni)
			CacheCurrentLobbyDetailsHandle("member_status");

			// JOINED, LEFT, KICKED lub PROMOTED - odśwież całą listę członków
			if (data.CurrentStatus == LobbyMemberStatus.Joined)
			{
				GD.Print($"[EOSManager:LobbyMemberStatus] Member JOINED: {GetShortUserId(userId)}");

				// Małe opóźnienie na synchronizację EOS
				GetTree().CreateTimer(0.3).Timeout += () =>
				{
					GetLobbyMembers();
					EmitSignal(SignalName.CurrentLobbyInfoUpdated, currentLobbyId, currentLobbyMembers.Count, 10, isLobbyOwner);
				};
			}
			else if (data.CurrentStatus == LobbyMemberStatus.Left || data.CurrentStatus == LobbyMemberStatus.Kicked || data.CurrentStatus == LobbyMemberStatus.Promoted)
			{
				GD.Print($"[EOSManager:LobbyMemberStatus] Member LEFT/KICKED/PROMOTED: {GetShortUserId(userId)}");

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
	/// <seealso cref="SetMemberAttribute"/>
	/// <seealso cref="AssignToUniversalTeam"/>
	/// <remarks>Gdy brak aktywnego lobby.</remarks>
	public void AssignToNeutralTeam()
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("[EOSManager:TeamAssign] Cannot assign team: Not in any lobby!");
			return;
		}

		GD.Print("[EOSManager:TeamAssign] 🟡 Assigning new player to NeutralTeam (None)");
		SetMemberAttribute("Team", Team.None.ToString());
		SetMemberAttribute("ProfileIcon", "0"); // Brak ikony w Neutral
	}

	/// <summary>
	/// Przypisuje nowego gracza do uniwersalnej drużyny (UniversalTeam)
	/// Wywoływane przez gracza po dołączeniu do lobby jeśli tryb gry to AIvsHuman
	/// </summary>
	/// <remarks>W trybie AI vs Human wykorzystuje niebieskie ikony profilowe.</remarks>
	/// <seealso cref="SetMemberAttribute"/>
	/// <seealso cref="AssignToNeutralTeam"/>
	/// <remarks>Gdy brak aktywnego lobby lub drużyna Universal jest pełna.</remarks>
	public void AssignToUniversalTeam()
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("[EOSManager:TeamAssign] Cannot assign team: Not in any lobby!");
			return;
		}

		// Sprawdź limit graczy w trybie AI vs Human
		if (GetTeamPlayerCount(Team.Universal) >= MaxPlayersInAIvsHuman)
		{
			GD.PrintErr($"[EOSManager:TeamAssign] Cannot join Universal team: Team is full ({MaxPlayersInAIvsHuman}/{MaxPlayersInAIvsHuman})");
			return;
		}

		GD.Print("[EOSManager:TeamAssign] Assigning new player to UniversalTeam (Universal)");

		// Przypisz niebieską ikonę dla Universal team
		int newIcon = AssignProfileIcon(Team.Universal);
		SetMemberAttribute("Team", Team.Universal.ToString());
		SetMemberAttribute("ProfileIcon", newIcon.ToString());
	}

	/// <summary>
	/// Ustawia drużynę dla lokalnego gracza, respektując limity miejsc w drużynach.
	/// </summary>
	/// <param name="teamName">Docelowa drużyna (Blue, Red, None, Universal).</param>
	/// <remarks>Metoda aktualizuje ikonę profilową i emituje <see cref="CheckTeamsBalanceConditions"/>.</remarks>
	/// <seealso cref="SetMemberAttribute"/>
	/// <seealso cref="GetTeamPlayerCount"/>
	/// <remarks>Gdy docelowa drużyna jest pełna.</remarks>
	public void SetMyTeam(Team teamName)
	{

		if ((teamName == Team.Blue || teamName == Team.Red) && GetTeamPlayerCount(teamName) >= MaxPlayersPerTeam)
		{
			GD.PrintErr($"[EOSManager:Team] Cannot join team {teamName}: Team is full ({MaxPlayersPerTeam}/{MaxPlayersPerTeam})");
			return;
		}

		// Pobierz poprzednią drużynę i ikonę
		Team oldTeam = Team.None;
		int oldIcon = 0;

		foreach (var member in currentLobbyMembers)
		{
			if (member.ContainsKey("isLocalPlayer") && (bool)member["isLocalPlayer"])
			{
				if (member.ContainsKey("team") && !string.IsNullOrEmpty(member["team"].ToString()))
				{
					Enum.TryParse<Team>(member["team"].ToString(), out oldTeam);
				}
				if (member.ContainsKey("profileIcon"))
				{
					try
					{
						oldIcon = member["profileIcon"].As<int>();
					}
					catch
					{
						int.TryParse(member["profileIcon"].ToString(), out oldIcon);
					}
				}
				break;
			}
		}

		// Sprawdź czy gracz już jest w tej drużynie
		if (oldTeam == teamName && oldIcon > 0)
		{
			// Już w tej drużynie z ikoną - nie zmieniaj nic
			GD.Print($"[EOSManager:Team] Already in team {teamName} with icon {oldIcon}, skipping reassignment");
			return;
		}

		// Zwolnij starą ikonę jeśli była
		if (oldIcon > 0)
		{
			ReleaseProfileIcon(oldTeam, oldIcon);
		}

		// Przebuduj używane ikony przed przypisaniem aby uniknąć duplikatów
		RebuildUsedIcons();

		// Przypisz nową ikonę jeśli drużyna to Blue lub Red
		int newIcon = 0;
		if (teamName == Team.Blue || teamName == Team.Red)
		{
			newIcon = AssignProfileIcon(teamName);
		}

		SetMemberAttribute("Team", teamName.ToString());
		SetMemberAttribute("ProfileIcon", newIcon.ToString());
		GD.Print($"[EOSManager:Team] Set my team to: {teamName}");

		//Sprawdzenie warunków dotyczących rozpoczęcia gry
		EmitSignal(SignalName.CheckTeamsBalanceConditions);

	}

	/// <summary>
	/// Odświeża informacje o obecnym lobby i wysyła sygnał do UI
	/// </summary>
	/// <seealso cref="CacheCurrentLobbyDetailsHandle(string)"/>
	/// <seealso cref="RefreshLobbyAttributes"/>
	/// <remarks>Gdy uchwyt LobbyDetails jest niedostępny lub null.</remarks>
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

				GD.Print($"[EOSManager:LobbyInfo] Lobby info refreshed: {currentLobbyId}, Players: {memberCount}/{info.Value.MaxMembers}");

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
			GD.PrintErr($"[EOSManager:LobbyInfo] Failed to refresh lobby info - lobby details is null");
		}
	}

	/// <summary>
	/// Odświeża atrybuty lobby (CustomLobbyId, GameMode, AIType, status sesji) z uchwytu LobbyDetails.
	/// </summary>
	/// <param name="lobbyDetails">Uchwyt LobbyDetails używany do odczytu atrybutów.</param>
	/// <seealso cref="RefreshCurrentLobbyInfo"/>
	/// <seealso cref="ApplyForcedTeamAssignments"/>
	/// <remarks>Gdy kluczowe atrybuty (np. CustomLobbyId) nie są dostępne w lobby.</remarks>
	private void RefreshLobbyAttributes(LobbyDetails lobbyDetails)
	{
		if (lobbyDetails == null) return;

		// Pobierz liczbę atrybutów lobby
		var attrCountOptions = new LobbyDetailsGetAttributeCountOptions();
		uint attributeCount = lobbyDetails.GetAttributeCount(ref attrCountOptions);

		GD.Print($"[EOSManager:LobbyAttributes] Refreshing lobby attributes from {attributeCount} attributes...");

		bool customIdFound = false;
		bool gameModeFound = false;
		bool aiTypeFound = false;
		forcedTeamAssignments.Clear();

		// Reset lokalnych danych sesji przed ponownym odczytem atrybutów lobby
		CurrentGameSession.SessionId = "";
		CurrentGameSession.HostUserId = "";
		CurrentGameSession.Seed = 0;
		CurrentGameSession.State = GameSessionState.None;
		CurrentGameSession.LobbyId = currentLobbyId;

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
						GD.Print($"[EOSManager:LobbyAttributes] CustomLobbyId refreshed: {currentCustomLobbyId}");
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
						GD.Print($"[EOSManager:LobbyAttributes] GameMode refreshed: {GetEnumDescription(currentGameMode)}");
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
						GD.Print($"[EOSManager:LobbyAttributes] AIType refreshed: {GetEnumDescription(currentAIType)}");
						EmitSignal(SignalName.AITypeUpdated, GetEnumDescription(currentAIType));
					}
					aiTypeFound = true;
				}
				else if (keyStr != null && keyStr.Equals("ReadyToStart", StringComparison.OrdinalIgnoreCase))
				{
					bool isReady = valueStr == "true";
					GD.Print($"[EOSManager:LobbyAttributes] ReadyToStart status received: {isReady}");
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
							GD.Print($"[EOSManager:LobbyAttributes] Found ForceTeam request: {GetShortUserId(targetUserId)} → None");
							forcedTeamAssignments[targetUserId] = Team.None;
						}
						// Parsuj niepusty string na enum
						else if (Enum.TryParse<Team>(valueStr, out Team parsedTeam))
						{
							GD.Print($"[EOSManager:LobbyAttributes] Found ForceTeam request: {GetShortUserId(targetUserId)} → {parsedTeam}");
							forcedTeamAssignments[targetUserId] = parsedTeam;
						}
					}
				}
				else if (keyStr != null && keyStr.StartsWith(ForceIconAttributePrefix, StringComparison.OrdinalIgnoreCase))
				{
					string targetUserId = keyStr.Substring(ForceIconAttributePrefix.Length);
					if (!string.IsNullOrEmpty(targetUserId))
					{
						if (!string.IsNullOrEmpty(valueStr) && int.TryParse(valueStr, out int forcedIcon))
						{
							GD.Print($"[EOSManager:LobbyAttributes] Found ForceIcon: {GetShortUserId(targetUserId)} → {forcedIcon}");
							forcedIconAssignments[targetUserId] = forcedIcon;
						}
						else
						{
							// Pusty lub nieprawidłowy - usuń wymuszenie
							forcedIconAssignments.Remove(targetUserId);
							GD.Print($"[EOSManager:LobbyAttributes] Cleared ForceIcon for {GetShortUserId(targetUserId)}");
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
							GD.Print($"[EOSManager:LobbyAttributes] Found PreviousTeam: {GetShortUserId(targetUserId)} → {parsedTeam}");
							previousTeamAssignments[targetUserId] = parsedTeam;
						}
					}
					else if (!string.IsNullOrEmpty(targetUserId) && string.IsNullOrEmpty(valueStr))
					{
						// Pusty valueStr oznacza usunięcie poprzedniej drużyny
						previousTeamAssignments.Remove(targetUserId);
						GD.Print($"[EOSManager:LobbyAttributes] Cleared PreviousTeam for {GetShortUserId(targetUserId)}");
					}
				}

				//odczyt danych sesji gry zapisanych w atrybutach lobby
				else if (keyStr != null && keyStr.Equals(ATTR_SESSION_ID, StringComparison.OrdinalIgnoreCase))
				{
					CurrentGameSession.SessionId = valueStr;
				}
				else if (keyStr != null && keyStr.Equals(ATTR_SESSION_SEED, StringComparison.OrdinalIgnoreCase))
				{
					if (!string.IsNullOrEmpty(valueStr) && ulong.TryParse(valueStr, out var parsedSeed))
						CurrentGameSession.Seed = parsedSeed;
				}
				else if (keyStr != null && keyStr.Equals(ATTR_SESSION_HOST, StringComparison.OrdinalIgnoreCase))
				{
					CurrentGameSession.HostUserId = valueStr;
				}
				else if (keyStr != null && keyStr.Equals(ATTR_SESSION_STATE, StringComparison.OrdinalIgnoreCase))
				{
					if (!string.IsNullOrEmpty(valueStr) && Enum.TryParse<GameSessionState>(valueStr, true, out var parsedState))
						CurrentGameSession.State = parsedState;
					else
						CurrentGameSession.State = GameSessionState.None;
				}
			}
		}

		// Jeśli nie znaleziono CustomLobbyId
		if (!customIdFound && (string.IsNullOrEmpty(currentCustomLobbyId) || currentCustomLobbyId == "Unknown"))
		{
			GD.PrintErr("[EOSManager:LobbyAttributes] CustomLobbyId not found in lobby attributes");
		}

		// Jeśli nie znaleziono GameMode, ustaw domyślny
		if (!gameModeFound && currentGameMode != GameMode.AIMaster)
		{
			currentGameMode = GameMode.AIMaster;
			EmitSignal(SignalName.GameModeUpdated, GetEnumDescription(currentGameMode));
			GD.Print("[EOSManager:LobbyAttributes] GameMode not found, using default: AI Master");
		}
		// Jeśli nie znaleziono AIType, ustaw domyślny
		if (!aiTypeFound && currentAIType != AIType.API)
		{
			currentAIType = AIType.API;
			EmitSignal(SignalName.AITypeUpdated, GetEnumDescription(currentAIType));
			GD.Print("[EOSManager:LobbyAttributes] AIType not found, using default: API");
		}

		// Jeśli sesja nie jest w stanie Starting, pozwól na ponowny start w przyszłości
		if (CurrentGameSession.State != GameSessionState.Starting)
		{
			sessionStartHandled = false;
		}

		bool hasAll = !string.IsNullOrEmpty(CurrentGameSession.SessionId)
		  && !string.IsNullOrEmpty(CurrentGameSession.HostUserId)
		  && CurrentGameSession.Seed != 0
		  && !string.IsNullOrEmpty(CurrentGameSession.LobbyId);

		// Bezpieczne wykrycie startu sesji gry - wykonywane tylko raz na update lobby	
		if (!string.IsNullOrEmpty(currentLobbyId)
			&& CurrentGameSession.State == GameSessionState.Starting
			&& hasAll
			&& !sessionStartHandled)
		{
			sessionStartHandled = true;


			GD.Print($"[EOSManager:Session] Session start detected from lobby: {CurrentGameSession.SessionId}, seed={CurrentGameSession.Seed}");
			GD.Print($"[SESSION DEBUG] currentLobbyId={currentLobbyId} sessionLobbyId={CurrentGameSession.LobbyId} hostUserId={CurrentGameSession.HostUserId} localPuid={localProductUserIdString}");

			EmitSignal(SignalName.GameSessionStartRequested,
				CurrentGameSession.SessionId,
				CurrentGameSession.HostUserId,
				CurrentGameSession.Seed
			);
		}


		ApplyForcedTeamAssignments();
	}

	/// <summary>
	/// Pobiera rzeczywistą liczbę członków w lobby (użyj po dołączeniu lub przy wyszukiwaniu).
	/// </summary>
	/// <param name="lobbyId">Identyfikator lobby, dla którego liczymy członków.</param>
	/// <returns>Liczba członków lobby lub 0, gdy brak danych.</returns>
	/// <seealso cref="CacheCurrentLobbyDetailsHandle(string)"/>
	/// <seealso cref="GetLobbyMembers"/>
	/// <remarks>Gdy brak LobbyDetails dla wskazanego lobby.</remarks>
	public int GetLobbyMemberCount(string lobbyId)
	{
		if (!foundLobbyDetails.ContainsKey(lobbyId))
		{
			GD.PrintErr($"[EOSManager:LobbyMembers] Lobby details not found for ID: {lobbyId}");
			return 0;
		}

		var countOptions = new LobbyDetailsGetMemberCountOptions();
		uint memberCount = foundLobbyDetails[lobbyId].GetMemberCount(ref countOptions);

		return (int)memberCount;
	}

	/// <summary>
	/// Ustawia atrybut CustomLobbyId bieżącego lobby (tylko host).
	/// </summary>
	/// <param name="newCustomId">Nowy kod lobby widoczny w wyszukiwaniu.</param>
	/// <remarks>Zmiana jest propagowana do członków lobby przez atrybuty EOS.</remarks>
	/// <seealso cref="SetLobbyAttribute(string, string)"/>
	/// <seealso cref="CustomLobbyIdUpdatedEventHandler"/>
	public void SetCustomLobbyId(string newCustomId)
	{
		SetLobbyAttribute("CustomLobbyId", newCustomId);

		GD.Print($"[EOSManager:LobbyAttributes] Setting CustomLobbyId to: {newCustomId}");
	}

	/// <summary>
	/// Ustawia tryb gry w atrybutach lobby i dostosowuje limit graczy.
	/// </summary>
	/// <param name="gameMode">Docelowy tryb gry.</param>
	/// <remarks>Metoda aktualizuje cache i emituje <see cref="GameModeUpdated"/>.</remarks>
	/// <seealso cref="SetLobbyAttribute(string, string)"/>
	/// <seealso cref="SetMaxLobbyMembers"/>
	/// <seealso cref="GameModeUpdatedEventHandler"/>
	public void SetGameMode(GameMode gameMode)
	{
		currentGameMode = gameMode;
		string gameModeStr = GetEnumDescription(gameMode);
		SetLobbyAttribute("GameMode", gameModeStr);

		GD.Print($"[EOSManager:LobbyAttributes] Setting GameMode to: {gameModeStr}");

		// Zmień limit graczy w zależności od trybu gry
		uint maxMembers = gameMode == GameMode.AIvsHuman ? (uint)MaxPlayersInAIvsHuman : (uint)(MaxPlayersPerTeam * 2);
		SetMaxLobbyMembers(maxMembers);

		EmitSignal(SignalName.GameModeUpdated, gameModeStr);
	}

	/// <summary>
	/// Ustawia typ AI w atrybutach lobby i powiadamia UI.
	/// </summary>
	/// <param name="aiType">Wybrany typ AI.</param>
	/// <remarks>Aktualizuje cache i emituje <see cref="AITypeUpdated"/>.</remarks>
	/// <seealso cref="SetLobbyAttribute(string, string)"/>
	/// <seealso cref="AITypeUpdatedEventHandler"/>
	public void SetAIType(AIType aiType)
	{
		currentAIType = aiType;
		string aiTypeStr = GetEnumDescription(aiType);
		SetLobbyAttribute("AIType", aiTypeStr);
		GD.Print($"[EOSManager:LobbyAttributes] Setting AIType to: {aiTypeStr}");

		EmitSignal(SignalName.AITypeUpdated, aiTypeStr);
	}

	/// <summary>
	/// Zmienia maksymalną liczbę graczy w lobby.
	/// </summary>
	/// <param name="maxMembers">Docelowy limit członków lobby.</param>
	/// <remarks>Wymaga uprawnień hosta i aktywnego lobby.</remarks>
	/// <seealso cref="SetGameMode"/>
	/// <seealso cref="RefreshCurrentLobbyInfo"/>
	/// <remarks>Gdy bieżący gracz nie jest hostem, nie ma ważnego lobby lub modyfikacja się nie powiedzie.</remarks>
	public void SetMaxLobbyMembers(uint maxMembers)
	{
		if (!isLobbyOwner)
		{
			GD.PrintErr("[EOSManager:LobbyAttributes] Only lobby owner can change max members");
			return;
		}

		if (string.IsNullOrEmpty(currentLobbyId) || localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("[EOSManager:LobbyAttributes] Cannot change max members: Not in a valid lobby");
			return;
		}

		GD.Print($"[EOSManager:LobbyAttributes] Changing lobby max members to: {maxMembers}");
		var modifyOptions = new UpdateLobbyModificationOptions()
		{
			LobbyId = currentLobbyId,
			LocalUserId = localProductUserId
		};

		Result result = lobbyInterface.UpdateLobbyModification(ref modifyOptions, out LobbyModification lobbyModification);

		if (result != Result.Success || lobbyModification == null)
		{
			GD.PrintErr($"[EOSManager:LobbyAttributes] Failed to create lobby modification: {result}");
			return;
		}

		// Ustaw nową maksymalną liczbę członków
		var setMaxMembersOptions = new LobbyModificationSetMaxMembersOptions()
		{
			MaxMembers = maxMembers
		};

		result = lobbyModification.SetMaxMembers(ref setMaxMembersOptions);

		if (result != Result.Success)
		{
			GD.PrintErr($"[EOSManager:LobbyAttributes] Failed to set max members: {result}");
			lobbyModification.Release();
			return;
		}

		// Wyślij modyfikację
		var updateOptions = new UpdateLobbyOptions()
		{
			LobbyModificationHandle = lobbyModification
		};

		lobbyInterface.UpdateLobby(ref updateOptions, null, (ref UpdateLobbyCallbackInfo data) =>
		{
			if (data.ResultCode == Result.Success)
			{
				GD.Print($"[EOSManager:LobbyAttributes] Lobby max members updated to: {maxMembers}");
			}
			else
			{
				GD.PrintErr($"[EOSManager:LobbyAttributes] Failed to update max members: {data.ResultCode}");
			}

			lobbyModification.Release();
		});
	}

	/// <summary>
	/// Zamyka lobby - ustawia PermissionLevel na InviteOnly, aby nowi gracze nie mogli dołączyć
	/// Używane podczas rozpoczynania rozgrywki
	/// </summary>
	/// <remarks>Wymaga bycia hostem; po sukcesie lobby nie jest publicznie widoczne.</remarks>
	/// <seealso cref="UnlockLobby"/>
	public void LockLobby()
	{
		if (string.IsNullOrEmpty(currentLobbyId) || !isLobbyOwner)
		{
			GD.Print("[EOSManager:LobbyLock] Cannot lock lobby - not owner or no lobby");
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
			GD.PrintErr($"[EOSManager:LobbyLock] Failed to create lobby modification for locking: {result}");
			return;
		}

		// Zmień PermissionLevel na InviteOnly - zablokuj lobby
		var setPermissionOptions = new LobbyModificationSetPermissionLevelOptions()
		{
			PermissionLevel = LobbyPermissionLevel.Inviteonly
		};

		result = lobbyModification.SetPermissionLevel(ref setPermissionOptions);

		if (result != Result.Success)
		{
			GD.PrintErr($"[EOSManager:LobbyLock] Failed to set permission level: {result}");
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
				GD.Print("[EOSManager:LobbyLock] Lobby locked");
			}
			else
			{
				GD.PrintErr($"[EOSManager:LobbyLock] Failed to lock lobby: {data.ResultCode}");
			}

			lobbyModification.Release();
		});
	}

	/// <summary>
	/// Otwiera lobby - ustawia PermissionLevel na PublicAdvertised, aby nowi gracze mogli dołączyć
	/// Używane po zakończeniu rozgrywki, gdy host wraca do lobby
	/// </summary>
	/// <remarks>Wymaga bycia hostem i aktywnego lobby.</remarks>
	/// <seealso cref="LockLobby"/>
	public void UnlockLobby()
	{
		if (string.IsNullOrEmpty(currentLobbyId) || !isLobbyOwner)
		{
			GD.Print("[EOSManager:LobbyLock] Cannot unlock lobby - not owner or no lobby");
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
			GD.PrintErr($"[EOSManager:LobbyLock] Failed to create lobby modification for unlocking: {result}");
			return;
		}

		// Zmień PermissionLevel na PublicAdvertised - odblokuj lobby
		var setPermissionOptions = new LobbyModificationSetPermissionLevelOptions()
		{
			PermissionLevel = LobbyPermissionLevel.Publicadvertised
		};

		result = lobbyModification.SetPermissionLevel(ref setPermissionOptions);

		if (result != Result.Success)
		{
			GD.PrintErr($"[EOSManager:LobbyLock] Failed to set permission level: {result}");
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
				GD.Print("[EOSManager:LobbyLock] Lobby unlocked");
			}
			else
			{
				GD.PrintErr($"[EOSManager:LobbyLock] Failed to unlock lobby: {data.ResultCode}");
			}

			lobbyModification.Release();
		});
	}

	/// <summary>
	/// Ustawia atrybut ReadyToStart w lobby, informując o gotowości do startu gry.
	/// </summary>
	/// <param name="isReady">Czy lobby spełnia warunki startu.</param>
	/// <remarks>Metoda modyfikuje atrybut lobby bez walidacji roli hosta.</remarks>
	/// <seealso cref="SetLobbyAttribute(string, string)"/>
	public void SetLobbyReadyStatus(bool isReady)
	{
		SetLobbyAttribute("ReadyToStart", isReady ? "true" : "false");
		GD.Print($"[EOSManager:LobbyAttributes] Setting ReadyToStart to: {isReady}");
	}

	/// <summary>
	/// Zapisuje poprzednią drużynę gracza w atrybutach lobby (przed przeniesieniem do Universal).
	/// </summary>
	/// <param name="userId">Id gracza, którego poprzednią drużynę zapisujemy.</param>
	/// <param name="previousTeam">Drużyna, w której gracz był przed przeniesieniem.</param>
	/// <remarks>Używane podczas przejścia do trybu AI vs Human.</remarks>
	/// <seealso cref="SetLobbyAttribute(string, string)"/>
	/// <seealso cref="GetPlayerPreviousTeam"/>
	/// <seealso cref="ClearPlayerPreviousTeam"/>
	/// <remarks>Gdy przekazano pusty userId.</remarks>
	public void SavePlayerPreviousTeam(string userId, Team previousTeam)
	{
		if (string.IsNullOrEmpty(userId))
		{
			GD.PrintErr("[EOSManager:LobbyAttributes] Cannot save previous team: userId is empty");
			return;
		}

		string attributeKey = PreviousTeamAttributePrefix + userId;
		SetLobbyAttribute(attributeKey, previousTeam.ToString());

		// Cache lokalnie
		previousTeamAssignments[userId] = previousTeam;

		GD.Print($"[EOSManager:LobbyAttributes] Saved previous team for {GetShortUserId(userId)}: {previousTeam}");
	}

	/// <summary>
	/// Odczytuje poprzednią drużynę gracza z atrybutów lobby.
	/// </summary>
	/// <param name="userId">Id gracza, dla którego pobieramy poprzednią drużynę.</param>
	/// <returns>Poprzednia drużyna lub Team.None, jeśli brak danych.</returns>
	/// <remarks>Najpierw sprawdza lokalny cache, a następnie zwraca Team.None.</remarks>
	/// <seealso cref="SavePlayerPreviousTeam"/>
	/// <seealso cref="ClearPlayerPreviousTeam"/>
	/// <remarks>Gdy przekazano pusty userId.</remarks>
	public Team GetPlayerPreviousTeam(string userId)
	{
		if (string.IsNullOrEmpty(userId))
		{
			GD.PrintErr("[EOSManager:LobbyAttributes] Cannot get previous team: userId is empty");
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
	/// Czyści zapisaną poprzednią drużynę gracza.
	/// </summary>
	/// <param name="userId">Id gracza, dla którego czyścimy poprzednią drużynę.</param>
	/// <remarks>Usuwa atrybut z lobby i lokalnego cache.</remarks>
	/// <seealso cref="SavePlayerPreviousTeam"/>
	/// <seealso cref="SetLobbyAttribute(string, string)"/>
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

		GD.Print($"[EOSManager:LobbyAttributes] Cleared previous team for {GetShortUserId(userId)}");
	}

	// ============================================
	// MEMBER ATTRIBUTES
	// ============================================

	/// <summary>
	/// Ustawia atrybut lobby (np. CustomLobbyId, LobbyName)
	/// </summary>
	/// <param name="key">Klucz atrybutu</param>
	/// <param name="value">Wartość atrybutu</param>
	/// <seealso cref="ScheduleAttributeBatchUpdate"/>
	/// <remarks>Gdy brak aktywnego lobby, użytkownik nie jest zalogowany lub nie jest hostem.</remarks>
	private void SetLobbyAttribute(string key, string value)
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("[EOSManager:LobbyAttributes] Cannot set lobby attribute: Not in any lobby!");
			return;
		}

		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("[EOSManager:LobbyAttributes] Cannot set lobby attribute: User not logged in!");
			return;
		}

		if (!isLobbyOwner)
		{
			GD.PrintErr($"[EOSManager:LobbyAttributes] Cannot set lobby attribute '{key}': Not lobby owner!");
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
	/// <seealso cref="FlushPendingLobbyAttributes"/>
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
	/// <seealso cref="CacheCurrentLobbyDetailsHandle"/>
	/// <seealso cref="RefreshCurrentLobbyInfo"/>
	/// <remarks>Gdy tworzenie lub aktualizacja modyfikacji lobby nie powiedzie się albo dodanie/usunięcie atrybutu zwróci błąd.</remarks>
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
				GD.PrintErr($"[EOSManager:LobbyAttributes] Failed to add lobby attribute '{kvp.Key}': {result}");
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
				GD.PrintErr($"[EOSManager:LobbyAttributes] Failed to remove lobby attribute '{key}': {result}");
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
				GD.Print($"[EOSManager:LobbyAttributes] Lobby batch update successful ({updatedKeys.Count} updates, {removedKeys.Count} removals)");
				// Po udanym update lobby odśwież lokalny cache,
				// aby klienci zobaczyli nowe atrybuty (np. GameSessionState = strarting)
				GetTree().CreateTimer(0.1).Timeout += () =>
				{
					// 1) odśwież handle (żeby zobaczyć nowe atrybuty)
					CacheCurrentLobbyDetailsHandle("refresh_info");

					// 2) odśwież info → to wywoła RefreshLobbyAttributes(lobbyDetails)
					RefreshCurrentLobbyInfo();
				};
			}
			else
			{
				GD.PrintErr($"[EOSManager:LobbyAttributes] Failed to update lobby attributes batch: {data.ResultCode}");
			}

			lobbyModification.Release();
		});
	}

	/// <summary>
	/// Kolejkuje usunięcie atrybutu lobby (wymaga bycia hostem bieżącego lobby).
	/// </summary>
	/// <param name="key">Nazwa atrybutu do usunięcia.</param>
	/// <seealso cref="ScheduleAttributeBatchUpdate"/>
	/// <seealso cref="SetLobbyAttribute(string, string)"/>
	/// <remarks>Gdy brak aktywnego lobby, użytkownik nie jest zalogowany lub nie jest hostem.</remarks>
	private void RemoveLobbyAttribute(string key)
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("[EOSManager:LobbyAttributes] Cannot remove lobby attribute: Not in any lobby!");
			return;
		}

		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("[EOSManager:LobbyAttributes] Cannot remove lobby attribute: User not logged in!");
			return;
		}

		if (!isLobbyOwner)
		{
			GD.PrintErr($"[EOSManager:LobbyAttributes] Cannot remove lobby attribute '{key}': Not lobby owner!");
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
	/// <seealso cref="CacheCurrentLobbyDetailsHandle(string)"/>
	/// <seealso cref="GetLobbyMembers"/>
	/// <remarks>Gdy brak aktywnego lobby, użytkownik nie jest zalogowany lub modyfikacja członka kończy się błędem.</remarks>
	private void SetMemberAttribute(string key, string value)
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("[EOSManager:MemberAttributes] Cannot set member attribute: Not in any lobby!");
			return;
		}

		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("[EOSManager:MemberAttributes] Cannot set member attribute: User not logged in!");
			return;
		}

		GD.Print($"[EOSManager:MemberAttributes] Setting member attribute: {key} = '{value}'");
		var modifyOptions = new UpdateLobbyModificationOptions()
		{
			LobbyId = currentLobbyId,
			LocalUserId = localProductUserId
		};

		Result result = lobbyInterface.UpdateLobbyModification(ref modifyOptions, out LobbyModification lobbyModification);

		if (result != Result.Success || lobbyModification == null)
		{
			GD.PrintErr($"[EOSManager:MemberAttributes] Failed to create lobby modification: {result}");
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
			GD.PrintErr($"[EOSManager:MemberAttributes] Failed to add member attribute '{key}': {result}");
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
				GD.Print($"[EOSManager:MemberAttributes] Member attribute '{key}' set successfully: '{value}'");

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
				GD.PrintErr($"[EOSManager:MemberAttributes] Failed to update member attribute '{key}': {data.ResultCode}");
			}

			lobbyModification.Release();
		});
	}

	/// <summary>
	/// Wymusza zmianę drużyny wskazanego gracza (host) i zapisuje to w atrybutach lobby.
	/// </summary>
	/// <param name="targetUserId">Id ProductUserId gracza do przeniesienia.</param>
	/// <param name="teamName">Docelowa drużyna.</param>
	/// <remarks>Metoda zapisuje ForceTeam_ w atrybutach lobby i czeka na synchronizację klientów.</remarks>
	/// <seealso cref="SetMyTeam"/>
	/// <seealso cref="SetLobbyAttribute(string, string)"/>
	/// <seealso cref="ApplyForcedTeamAssignments"/>
	/// <remarks>Gdy brak lobby, bieżący gracz nie jest hostem, userId jest pusty lub docelowa drużyna jest pełna.</remarks>
	public void MovePlayerToTeam(string targetUserId, Team teamName)
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("[EOSManager:TeamManagement] Cannot move player: Not in any lobby!");
			return;
		}

		if (!isLobbyOwner)
		{
			GD.PrintErr("[EOSManager:TeamManagement] Cannot move player: Only lobby owner can change other players' teams!");
			return;
		}

		if (string.IsNullOrEmpty(targetUserId))
		{
			GD.PrintErr("[EOSManager:TeamManagement] Cannot move player: Target userId is empty!");
			return;
		}

		if ((teamName == Team.Blue || teamName == Team.Red) && GetTeamPlayerCount(teamName) >= MaxPlayersPerTeam)
		{
			GD.PrintErr($"[EOSManager:TeamManagement] Cannot move player: Team {teamName} is full ({MaxPlayersPerTeam}/{MaxPlayersPerTeam})");
			return;
		}

		if (targetUserId == localProductUserId.ToString())
		{
			GD.Print("[EOSManager:TeamManagement] Host requested to move themselves");
			SetMyTeam(teamName);
			return;
		}

		GD.Print($"[EOSManager:TeamManagement] Requesting player {GetShortUserId(targetUserId)} to join {teamName} team");
		forcedTeamAssignments[targetUserId] = teamName;
		SetLobbyAttribute($"{ForceTeamAttributePrefix}{targetUserId}", teamName.ToString());
	}

	/// <summary>
	/// Przenosi wszystkich graczy z Blue/Red do Universal i zapisuje ich poprzednie drużyny
	/// Wywoływane gdy host zmienia tryb gry na AI vs Human
	/// Host przypisuje ikony WSZYSTKIM graczom centralnie aby uniknąć duplikatów
	/// </summary>
	/// <remarks>Operacja modyfikuje atrybuty lobby ForceTeam_/ForceIcon_ i odświeża cache członków.</remarks>
	/// <seealso cref="SavePlayerPreviousTeam"/>
	/// <seealso cref="SetLobbyAttribute(string, string)"/>
	/// <seealso cref="SetMemberAttribute"/>
	/// <seealso cref="FlushPendingLobbyAttributes"/>
	/// <seealso cref="GetLobbyMembers"/>
	/// <remarks>Gdy operację wywoła użytkownik niebędący hostem.</remarks>
	public void MoveAllPlayersToUniversal()
	{
		if (!isLobbyOwner)
		{
			GD.PrintErr("[EOSManager:TeamManagement] Only host can move all players to Universal team");
			return;
		}

		GD.Print("[EOSManager:TeamManagement] Moving all players to Universal team...");

		// KROK 1: Wyczyść WSZYSTKIE używane ikony - zaczynamy od zera
		usedBlueIcons.Clear();
		usedRedIcons.Clear();
		GD.Print("[EOSManager:TeamManagement] Cleared all used icons");

		// KROK 2: Zbierz wszystkich graczy do przeniesienia
		var playersToMove = new System.Collections.Generic.List<(string userId, Team oldTeam)>();

		foreach (var member in currentLobbyMembers)
		{
			if (!member.ContainsKey("userId"))
				continue;

			string userId = member["userId"].ToString();

			// Pobierz obecny team
			Team currentTeam = Team.None;
			if (member.ContainsKey("team"))
			{
				string teamStr = member["team"].ToString();
				if (!string.IsNullOrEmpty(teamStr))
				{
					Enum.TryParse<Team>(teamStr, out currentTeam);
				}
			}

			// Przenieś tylko graczy z Blue, Red lub None (nie tych już w Universal)
			if (currentTeam == Team.Blue || currentTeam == Team.Red || currentTeam == Team.None)
			{
				playersToMove.Add((userId, currentTeam));
			}
		}

		// KROK 3: Przypisz ikony PO KOLEI każdemu graczowi (host kontroluje)
		int iconCounter = 1;
		foreach (var (userId, oldTeam) in playersToMove)
		{
			string shortUserId = userId.Length > 8 ? userId.Substring(userId.Length - 8) : userId;

			// Zapisz poprzednią drużynę
			SavePlayerPreviousTeam(userId, oldTeam);

			// Przypisz ikonę sekwencyjnie (1, 2, 3, 4, 5)
			int assignedIcon = iconCounter;
			if (iconCounter <= MaxProfileIconsPerTeam)
			{
				usedBlueIcons.Add(iconCounter);
				iconCounter++;
			}
			else
			{
				assignedIcon = 0; // Brak dostępnych ikon
				GD.PrintErr($"[EOSManager:TeamManagement] No more icons available for {shortUserId}");
			}

			// Ustaw ForceTeam i ForceIcon dla tego gracza
			forcedTeamAssignments[userId] = Team.Universal;
			forcedIconAssignments[userId] = assignedIcon;

			SetLobbyAttribute($"{ForceTeamAttributePrefix}{userId}", Team.Universal.ToString());
			SetLobbyAttribute($"{ForceIconAttributePrefix}{userId}", assignedIcon.ToString());

			GD.Print($"[EOSManager:TeamManagement] {shortUserId}: oldTeam={oldTeam} → Universal, icon={assignedIcon}");

			// Jeśli to host - ustaw od razu swoje MEMBER attributes
			bool isLocalPlayer = userId == localProductUserId.ToString();
			if (isLocalPlayer)
			{
				SetMemberAttribute("Team", Team.Universal.ToString());
				SetMemberAttribute("ProfileIcon", assignedIcon.ToString());
				GD.Print($"[EOSManager:TeamManagement] Host set own attributes: Universal, icon {assignedIcon}");
			}
		}

		GD.Print($"[EOSManager:TeamManagement] Assigned icons to {playersToMove.Count} players (icons used: {string.Join(",", usedBlueIcons)})");

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
	/// Host przypisuje ikony WSZYSTKIM graczom centralnie aby uniknąć duplikatów
	/// </summary>
	/// <remarks>Metoda czyści atrybuty ForceTeam_/ForceIcon_ i odtwarza poprzednie drużyny.</remarks>
	/// <seealso cref="GetPlayerPreviousTeam"/>
	/// <seealso cref="ClearPlayerPreviousTeam"/>
	/// <seealso cref="SetLobbyAttribute(string, string)"/>
	/// <seealso cref="SetMemberAttribute"/>
	/// <seealso cref="FlushPendingLobbyAttributes"/>
	/// <seealso cref="GetLobbyMembers"/>
	/// <remarks>Gdy operację wywoła użytkownik niebędący hostem.</remarks>
	public void RestorePlayersFromUniversal()
	{
		if (!isLobbyOwner)
		{
			GD.PrintErr("[EOSManager:TeamManagement] Only host can restore players from Universal team");
			return;
		}

		GD.Print("[EOSManager:TeamManagement] Restoring players from Universal...");

		// KROK 1: Wyczyść WSZYSTKIE używane ikony - zaczynamy od zera
		usedBlueIcons.Clear();
		usedRedIcons.Clear();
		GD.Print("[EOSManager:TeamManagement] Cleared all used icons");

		// KROK 2: Zbierz wszystkich graczy do przywrócenia i ich poprzednie drużyny
		var playersToRestore = new System.Collections.Generic.List<(string userId, Team previousTeam, bool isLocal)>();

		foreach (var member in currentLobbyMembers)
		{
			if (!member.ContainsKey("userId"))
				continue;

			string userId = member["userId"].ToString();
			bool isLocalPlayer = userId == localProductUserId.ToString();

			// Pobierz obecny team
			Team currentTeam = Team.None;
			if (member.ContainsKey("team"))
			{
				string teamStr = member["team"].ToString();
				if (!string.IsNullOrEmpty(teamStr))
				{
					Enum.TryParse<Team>(teamStr, out currentTeam);
				}
			}

			// Przywróć tylko graczy z Universal
			if (currentTeam == Team.Universal)
			{
				Team previousTeam = GetPlayerPreviousTeam(userId);
				playersToRestore.Add((userId, previousTeam, isLocalPlayer));
			}
		}

		// KROK 3: Przypisz ikony PO KOLEI każdemu graczowi według poprzedniej drużyny
		int blueIconCounter = 1;
		int redIconCounter = 1;

		foreach (var (userId, previousTeam, isLocalPlayer) in playersToRestore)
		{
			string shortUserId = userId.Length > 8 ? userId.Substring(userId.Length - 8) : userId;

			int assignedIcon = 0;
			Team targetTeam = previousTeam;

			// Jeśli nie ma zapisanej poprzedniej drużyny lub była None/Universal - ustaw None
			if (previousTeam == Team.None || previousTeam == Team.Universal)
			{
				targetTeam = Team.None;
				assignedIcon = 0;
			}
			else if (previousTeam == Team.Blue)
			{
				if (blueIconCounter <= MaxProfileIconsPerTeam)
				{
					assignedIcon = blueIconCounter;
					usedBlueIcons.Add(blueIconCounter);
					blueIconCounter++;
				}
			}
			else if (previousTeam == Team.Red)
			{
				if (redIconCounter <= MaxProfileIconsPerTeam)
				{
					assignedIcon = redIconCounter;
					usedRedIcons.Add(redIconCounter);
					redIconCounter++;
				}
			}

			// Ustaw ForceTeam i ForceIcon (lub wyczyść ForceIcon dla None)
			forcedTeamAssignments[userId] = targetTeam;
			string teamValue = (targetTeam == Team.None) ? "" : targetTeam.ToString();
			SetLobbyAttribute($"{ForceTeamAttributePrefix}{userId}", teamValue);

			if (targetTeam == Team.None)
			{
				// Wyczyść ForceIcon
				forcedIconAssignments.Remove(userId);
				SetLobbyAttribute($"{ForceIconAttributePrefix}{userId}", "");
			}
			else
			{
				forcedIconAssignments[userId] = assignedIcon;
				SetLobbyAttribute($"{ForceIconAttributePrefix}{userId}", assignedIcon.ToString());
			}

			GD.Print($"[EOSManager:TeamManagement] {shortUserId}: Universal → {targetTeam}, icon={assignedIcon}");

			// Jeśli to host - ustaw od razu swoje MEMBER attributes
			if (isLocalPlayer)
			{
				SetMemberAttribute("Team", teamValue);
				SetMemberAttribute("ProfileIcon", assignedIcon.ToString());
				GD.Print($"[EOSManager:TeamManagement] Host set own attributes: {targetTeam}, icon {assignedIcon}");
			}

			// Wyczyść zapisaną poprzednią drużynę
			ClearPlayerPreviousTeam(userId);
		}

		GD.Print($"[EOSManager:TeamManagement] Restored {playersToRestore.Count} players (Blue icons: {string.Join(",", usedBlueIcons)}, Red icons: {string.Join(",", usedRedIcons)})");


		// Wyślij wszystkie zmiany atrybutów
		FlushPendingLobbyAttributes();
		GetTree().CreateTimer(0.3).Timeout += () =>
		{
			GetLobbyMembers();
		};
	}

	/// <summary>
	/// Zwraca bieżącą drużynę gracza na podstawie cache członków lobby.
	/// </summary>
	/// <param name="userId">Id ProductUserId gracza.</param>
	/// <returns>Drużyna gracza lub Team.None, gdy brak danych.</returns>
	/// <remarks>Metoda nie odświeża danych z EOS – bazuje na lokalnym cache.</remarks>
	/// <seealso cref="GetCurrentLobbyMembers"/>
	/// <seealso cref="ApplyForcedTeamAssignments"/>
	public Team GetTeamForUser(string userId)
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
	/// Zlicza ilu graczy w cache jest przypisanych do podanej drużyny.
	/// </summary>
	/// <param name="team">Drużyna, dla której wykonujemy zliczenie.</param>
	/// <returns>Liczba graczy w drużynie.</returns>
	/// <seealso cref="AssignToNeutralTeam"/>
	/// <seealso cref="AssignToUniversalTeam"/>
	/// <seealso cref="SetMyTeam"/>
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

	/// <summary>
	/// Zastosowuje wymuszone przypisania drużyn dla lokalnego gracza i czyści spełnione żądania (gdy host).
	/// </summary>
	/// <seealso cref="SetMemberAttribute"/>
	/// <seealso cref="TryResolveForcedTeamRequests"/>
	private void ApplyForcedTeamAssignments()
	{
		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			return;
		}

		string localUserId = localProductUserId.ToString();

		if (forcedTeamAssignments.TryGetValue(localUserId, out Team forcedTeam))
		{
			// Pobierz obecny zespół i ikonę gracza z currentLobbyMembers (NIE z GetTeamForUser!)
			Team currentTeam = Team.None;
			int currentIcon = 0;

			foreach (var member in currentLobbyMembers)
			{
				if (member.ContainsKey("isLocalPlayer") && (bool)member["isLocalPlayer"])
				{
					// Pobierz team
					if (member.ContainsKey("team"))
					{
						string teamStr = member["team"].ToString();
						if (!string.IsNullOrEmpty(teamStr))
						{
							Enum.TryParse(teamStr, out currentTeam);
						}
					}

					// Pobierz icon
					if (member.ContainsKey("profileIcon"))
					{
						try
						{
							currentIcon = member["profileIcon"].As<int>();
						}
						catch
						{
							int.TryParse(member["profileIcon"].ToString(), out currentIcon);
						}
					}
					break;
				}
			}

			// Sprawdź czy host przypisał wymuszoną ikonę
			int forcedIcon = 0;
			bool hasForcedIcon = forcedIconAssignments.TryGetValue(localUserId, out forcedIcon);

			// Jeśli już jestem w tym zespole z poprawną ikoną, nie rób nic
			if (currentTeam == forcedTeam && currentIcon > 0)
			{
				// Ale sprawdź czy ikona się zgadza z wymuszoną
				if (!hasForcedIcon || currentIcon == forcedIcon)
				{
					GD.Print($"[EOSManager:TeamManagement] Already in forced team {forcedTeam} with icon {currentIcon}, skipping reassignment");
					return;
				}
			}

			// Jeśli zmienia się zespół LUB mam wymuszoną ikonę inną niż obecna
			bool iconMismatch = hasForcedIcon && forcedIcon > 0 && currentIcon != forcedIcon;
			if (currentTeam != forcedTeam || iconMismatch)
			{
				GD.Print($"[EOSManager:TeamManagement] Host forced you to switch to {forcedTeam} (currentTeam={currentTeam}, currentIcon={currentIcon}, forcedIcon={forcedIcon})");

				// Użyj wymuszonej ikony jeśli jest, w przeciwnym razie przypisz nową
				int newIcon;
				if (hasForcedIcon && forcedIcon > 0)
				{
					newIcon = forcedIcon;
					GD.Print($"[EOSManager:TeamManagement] Using forced icon from host: {newIcon}");
				}
				else if (forcedTeam == Team.Blue || forcedTeam == Team.Red || forcedTeam == Team.Universal)
				{
					// Fallback - przypisz ikonę samodzielnie (nie powinno się zdarzyć)
					RebuildUsedIcons();
					newIcon = AssignProfileIcon(forcedTeam);
					GD.Print($"[EOSManager:TeamManagement] No forced icon, assigned new: {newIcon}");
				}
				else
				{
					newIcon = 0;
				}

				// Gdy forcedTeam == None, ustaw pusty string (nie "None")
				string teamValue = (forcedTeam == Team.None) ? "" : forcedTeam.ToString();
				SetMemberAttribute("Team", teamValue);
				SetMemberAttribute("ProfileIcon", newIcon.ToString());
				GD.Print($"[EOSManager:TeamManagement] Applied forced team {forcedTeam} with icon {newIcon}");
			}
		}

		if (isLobbyOwner)
		{
			TryResolveForcedTeamRequests();
		}
	}

	/// <summary>
	/// Host weryfikuje, czy wymuszone zmiany drużyn zostały zrealizowane i usuwa zbędne atrybuty ForceTeam_.
	/// </summary>
	/// <seealso cref="ClearForcedTeamAttribute"/>
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

	/// <summary>
	/// Usuwa wymuszenie drużyny (ForceTeam_) dla wskazanego użytkownika.
	/// </summary>
	/// <param name="userId">Id ProductUserId, dla którego należy wyczyścić wymuszenie.</param>
	/// <seealso cref="RemoveLobbyAttribute"/>
	private void ClearForcedTeamAttribute(string userId)
	{
		if (string.IsNullOrEmpty(userId))
		{
			return;
		}

		forcedTeamAssignments.Remove(userId);
		string attributeKey = $"{ForceTeamAttributePrefix}{userId}";
		GD.Print($"[EOSManager:TeamManagement] Clearing forced team attribute for {GetShortUserId(userId)}");
		RemoveLobbyAttribute(attributeKey);
	}

	/// <summary>
	/// Zwraca aktualną, posortowaną listę członków lobby z lokalnego cache.
	/// </summary>
	/// <returns>Lista słowników z danymi członków aktualnego lobby.</returns>
	/// <seealso cref="GetLobbyMembers"/>
	public Godot.Collections.Array<Godot.Collections.Dictionary> GetCurrentLobbyMembers()
	{
		return currentLobbyMembers;
	}

	/// <summary>
	/// Pobiera członków bieżącego lobby z EOS, uaktualnia cache i emituje sygnały UI.
	/// </summary>
	/// <remarks>Wywołuje odświeżenie atrybutów i sortowanie wyników dla spójnego UI.</remarks>
	/// <seealso cref="CacheCurrentLobbyDetailsHandle(string)"/>
	/// <seealso cref="GetCurrentLobbyMembers"/>
	/// <remarks>Gdy brak aktywnego lobby, użytkownik nie jest zalogowany lub uchwyt LobbyDetails jest niedostępny/null.</remarks>
	public void GetLobbyMembers()
	{
		if (string.IsNullOrEmpty(currentLobbyId))
		{
			GD.PrintErr("[EOSManager:Lobby] Cannot get lobby members: Not in any lobby!");
			return;
		}

		if (localProductUserId == null || !localProductUserId.IsValid())
		{
			GD.PrintErr("[EOSManager:Lobby] Cannot get lobby members: User not logged in!");
			return;
		}

		// Sprawdź czy mamy lobby details w cache
		if (!foundLobbyDetails.ContainsKey(currentLobbyId))
		{
			GD.PrintErr($"[EOSManager:Lobby] Lobby details not found in cache for ID: {currentLobbyId}");
			return;
		}

		LobbyDetails lobbyDetails = foundLobbyDetails[currentLobbyId];

		if (lobbyDetails == null)
		{
			GD.PrintErr("[EOSManager:Lobby] Lobby details is null!");
			return;
		}

		// Pobierz liczbę członków
		var countOptions = new LobbyDetailsGetMemberCountOptions();
		uint memberCount = lobbyDetails.GetMemberCount(ref countOptions);

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
				int profileIcon = 0; // Numer ikony profilowej (0 = brak)
				string inLobbyView = "true"; // Domyślnie true dla nowych graczy
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

						// Pobierz ProfileIcon
						if (keyStr != null && keyStr.Equals("ProfileIcon", StringComparison.OrdinalIgnoreCase))
						{
							int.TryParse(valueStr, out profileIcon);
						}

						// Pobierz InLobbyView
						if (keyStr != null && keyStr.Equals("InLobbyView", StringComparison.OrdinalIgnoreCase))
						{
							inLobbyView = valueStr;
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
					{ "team", team },
					{ "profileIcon", profileIcon },
					{ "inLobbyView", inLobbyView }
				};

				membersList.Add(memberData);
				GD.Print($"[EOSManager:LobbyMembers] Added member: {displayName}, team={team}, icon={profileIcon}, inLobbyView={inLobbyView}");
			}
		}

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

		// Odbuduj listę używanych ikon na podstawie członków
		RebuildUsedIcons();

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
					GD.Print("[EOSManager:Lobby] You have been promoted to lobby owner!");
				}
				break;
			}
		}

		// Wyślij sygnał do UI
		EmitSignal(SignalName.LobbyMembersUpdated, membersList);

		// Aktualizuj licznik graczy
		EmitSignal(SignalName.CurrentLobbyInfoUpdated, currentLobbyId, membersList.Count, 10, isLobbyOwner);

		TryResolveForcedTeamRequests();
	}

	// ============================================
	// NOWE: Bezpośrednie kopiowanie LobbyDetails handle
	// ============================================

	/// <summary>
	/// Kopiuje i buforuje uchwyt LobbyDetails dla bieżącego lobby; opcjonalnie odświeża istniejący, aby mieć aktualne dane.
	/// </summary>
	/// <param name="reason">Powód odświeżenia, decyduje czy wymusić ponowne pobranie uchwytu.</param>
	/// <seealso cref="RefreshCurrentLobbyInfo"/>
	/// <seealso cref="GetLobbyMembers"/>
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
			GD.Print($"[EOSManager:LobbyInfo] Cached LobbyDetails handle for lobby {currentLobbyId} (reason={reason})");
		}
		else
		{
			GD.Print($"[EOSManager:LobbyInfo] Failed to copy LobbyDetails handle (reason={reason}): {r}");
		}
	}
}
