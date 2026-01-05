using Godot;
using System;

public partial class SoundManager : Node
{
	// Ścieżki do plików audio
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
		// 1. Dźwięki działają w pauzie
		ProcessMode = ProcessModeEnum.Always;

		GD.Print("🎵 Initializing SoundManager...");

		LoadAudioStreams();
		SetupAudioPlayers();
		PlayMusic();

		// 2. Podłączamy się do sygnału dla PRZYSZŁYCH przycisków
		GetTree().NodeAdded += OnNodeAdded;

		// 3. Skanujemy przyciski już istniejące
		ScanTreeForButtons(GetTree().Root);

		GD.Print("✅ SoundManager ready!");
	}

	private void LoadAudioStreams()
	{
		hoverStream   = GD.Load<AudioStream>(AUDIO_HOVER_PATH);
		buttonStream  = GD.Load<AudioStream>(AUDIO_BUTTON_PATH);
		bgMusicStream = GD.Load<AudioStream>(AUDIO_BG_MUSIC_PATH);

		if (hoverStream == null)   GD.PrintErr($"❌ Failed to load: {AUDIO_HOVER_PATH}");
		if (buttonStream == null)  GD.PrintErr($"❌ Failed to load: {AUDIO_BUTTON_PATH}");
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

	private void OnNodeAdded(Node node)
	{
		ConnectButtonSignals(node);
	}

	private void ScanTreeForButtons(Node node)
	{
		// Sprawdź obecny węzeł
		ConnectButtonSignals(node);

		// Sprawdź dzieci (rekurencja)
		foreach (Node child in node.GetChildren())
		{
			ScanTreeForButtons(child);
		}
	}

	private void ConnectButtonSignals(Node node)
	{
		if (node is BaseButton button)
		{
			// FIX: Zamiast IsConnected (które jest zawodne przy C# events), 
			// używamy bezpiecznego wzorca: najpierw odejmij (-=), potem dodaj (+=).
			// To gwarantuje, że funkcja nie podłączy się dwa razy.
			
			button.MouseEntered -= PlayHover; // Usuń jeśli już jest (bezpieczne, nawet jak nie ma)
			button.MouseEntered += PlayHover; // Dodaj
			
			button.Pressed -= PlayClick;
			button.Pressed += PlayClick;
		}
	}

	// --- ODTWARZANIE ---

	private void PlayHover()
	{
		if (sfxHover != null)
		{
			// FIX: Używamy nameof(), żeby nie zależeć od generowania kodu podczas błędu kompilacji
			// GD.RandRange zwraca double, rzutujemy na float - to jest OK.
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
		// FIX: Sprawdź czy Tree istnieje (przy zamykaniu gry może być null)
		if (GetTree() != null)
		{
			GetTree().NodeAdded -= OnNodeAdded;
		}
	}
}
