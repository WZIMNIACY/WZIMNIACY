using Godot;

public partial class SoundManager : Node
{
	//Ścieżki do plików audio
	private const string AUDIO_HOVER_PATH = "res://assets/sounds/Hover.ogg";
	private const string AUDIO_BUTTON_PATH = "res://assets/sounds/Button.ogg";
	private const string AUDIO_BG_MUSIC_PATH = "res://assets/sounds/Background.mp3";

	// Odtwarzacze audio
	private AudioStreamPlayer musicPlayer;
	private AudioStreamPlayer sfxHover;
	private AudioStreamPlayer sfxClick;

	// Preloadowane streamy
	private AudioStream hoverStream;
	private AudioStream buttonStream;
	private AudioStream bgMusicStream;

	public override void _Ready()
	{
		// 1. WAŻNE: Ustawiamy tryb Always, żeby dźwięki działały też w PAUZIE
		ProcessMode = ProcessModeEnum.Always;

		GD.Print("🎵 Initializing SoundManager...");

		LoadAudioStreams();
		SetupAudioPlayers();
		PlayMusic();

		// 2. Podłączamy się do sygnału dla PRZYSZŁYCH przycisków
		GetTree().NodeAdded += OnNodeAdded;

		// 3. NOWOŚĆ: Ręcznie skanujemy przyciski, które JUŻ ISTNIEJĄ w scenie startowej
		ScanTreeForButtons(GetTree().Root);

		GD.Print("✅ SoundManager ready!");
	}

	private void LoadAudioStreams()
	{
		hoverStream = GD.Load<AudioStream>(AUDIO_HOVER_PATH);
		buttonStream = GD.Load<AudioStream>(AUDIO_BUTTON_PATH);
		bgMusicStream = GD.Load<AudioStream>(AUDIO_BG_MUSIC_PATH);

		if (hoverStream == null) GD.PrintErr($"❌ Failed to load: {AUDIO_HOVER_PATH}");
		if (buttonStream == null) GD.PrintErr($"❌ Failed to load: {AUDIO_BUTTON_PATH}");
		if (bgMusicStream == null) GD.PrintErr($"❌ Failed to load: {AUDIO_BG_MUSIC_PATH}");
	}

	private void SetupAudioPlayers()
	{
		// Muzyka
		musicPlayer = new AudioStreamPlayer();
		musicPlayer.Stream = bgMusicStream;
		musicPlayer.VolumeDb = -15.0f;
		musicPlayer.Bus = "Music";
		AddChild(musicPlayer);

		// Dźwięk kliknięcia
		sfxClick = new AudioStreamPlayer();
		sfxClick.Stream = buttonStream;
		sfxClick.VolumeDb = -5.0f;
		sfxClick.Bus = "SFX";
		AddChild(sfxClick);

		// Dźwięk najechania
		sfxHover = new AudioStreamPlayer();
		sfxHover.Stream = hoverStream;
		sfxHover.VolumeDb = -10.0f;
		sfxHover.Bus = "SFX";
		AddChild(sfxHover);
	}

	private void PlayMusic()
	{
		if (musicPlayer != null && !musicPlayer.Playing)
		{
			musicPlayer.Play();
			GD.Print("🎵 Background music started");
		}
	}

	// --- LOGIKA PODŁĄCZANIA ---

	// Metoda dla nowych węzłów (działa automatycznie)
	private void OnNodeAdded(Node node)
	{
		ConnectButtonSignals(node);
	}

	// NOWA METODA: Rekurencyjne przeszukiwanie istniejącego drzewa
	private void ScanTreeForButtons(Node node)
	{
		// Sprawdź obecny węzeł
		ConnectButtonSignals(node);

		// Sprawdź dzieci węzła (idź głębiej)
		foreach (Node child in node.GetChildren())
		{
			ScanTreeForButtons(child);
		}
	}

	// Wspólna funkcja podłączająca (żeby nie pisać tego samego kodu 2 razy)
	private void ConnectButtonSignals(Node node)
	{
		if (node is BaseButton button)
		{
			// Sprawdzamy czy już jest podłączony, żeby uniknąć błędów
			if (!button.IsConnected("mouse_entered", new Callable(this, MethodName.PlayHover)))
			{
				button.MouseEntered += PlayHover;
			}
			
			if (!button.IsConnected("pressed", new Callable(this, MethodName.PlayClick)))
			{
				button.Pressed += PlayClick;
			}
		}
	}

	// --- ODTWARZANIE ---

	private void PlayHover()
	{
		if (sfxHover != null)
		{
			sfxHover.PitchScale = (float)GD.RandRange(0.95, 1.05);
			sfxHover.Play();
		}
	}

	private void PlayClick()
	{
		if (sfxClick != null)
		{
			sfxClick.Play();
		}
	}

	public override void _ExitTree()
	{
		GetTree().NodeAdded -= OnNodeAdded;
	}
}
