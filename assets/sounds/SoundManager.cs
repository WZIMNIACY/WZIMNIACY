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
		base._Ready();

		GD.Print("🎵 Initializing SoundManager...");

		// Załaduj streamy
		LoadAudioStreams();

		// 1. Konfiguracja odtwarzaczy przy starcie gry
		SetupAudioPlayers();

		// 2. Start muzyki
		PlayMusic();

		// 3. Podłączamy się do sygnału drzewa scen (automatyczne wykrywanie przycisków)
		GetTree().NodeAdded += OnNodeAdded;

		GD.Print("✅ SoundManager ready!");
	}

	private void LoadAudioStreams()
	{
		// Załaduj pliki audio
		hoverStream = GD.Load<AudioStream>(AUDIO_HOVER_PATH);
		buttonStream = GD.Load<AudioStream>(AUDIO_BUTTON_PATH);
		bgMusicStream = GD.Load<AudioStream>(AUDIO_BG_MUSIC_PATH);

		if (hoverStream == null)
			GD.PrintErr($"❌ Failed to load: {AUDIO_HOVER_PATH}");
		if (buttonStream == null)
			GD.PrintErr($"❌ Failed to load: {AUDIO_BUTTON_PATH}");
		if (bgMusicStream == null)
			GD.PrintErr($"❌ Failed to load: {AUDIO_BG_MUSIC_PATH}");
	}

	private void SetupAudioPlayers()
	{
		// Muzyka
		musicPlayer = new AudioStreamPlayer();
		musicPlayer.Stream = bgMusicStream;
		musicPlayer.VolumeDb = -15.0f;
		musicPlayer.ProcessMode = ProcessModeEnum.Always;
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

	// AUTOMATYCZNE WYKRYWANIE PRZYCISKÓW
	private void OnNodeAdded(Node node)
	{
		// Sprawdź czy dodany węzeł to przycisk
		if (node is BaseButton button)
		{
			// Zawsze podłączaj dźwięki (Godot ignoruje duplikaty automatycznie)
			button.MouseEntered += PlayHover;
			button.Pressed += PlayClick;
		}
	}

	// ODTWARZANIE EFEKTÓW
	private void PlayHover()
	{
		if (sfxHover != null)
		{
			// Opcjonalny randomizer, żeby nie brzmiało jak robot
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
		base._ExitTree();

		// Odłącz sygnały przy zamykaniu
		GetTree().NodeAdded -= OnNodeAdded;
	}
}
