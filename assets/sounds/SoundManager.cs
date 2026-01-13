using Godot;

public partial class SoundManager : Node
{
	// --- ŚCIEŻKI (Zmieniono na PascalCase zgodnie ze standardem C#) ---
	private const string AudioHoverPath = "res://assets/sounds/Hover.ogg";
	private const string AudioButtonPath = "res://assets/sounds/Button.ogg";
	private const string AudioBgMusicPath = "res://assets/sounds/Background.mp3";

	// --- KOMPONENTY ---
	private AudioStreamPlayer musicPlayer;
	private AudioStreamPlayer sfxHover;
	private AudioStreamPlayer sfxClick;

	// --- ZASOBY ---
	private AudioStream hoverStream;
	private AudioStream buttonStream;
	private AudioStream bgMusicStream;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;

		GD.Print("🎵 Initializing SoundManager...");

		LoadAudioStreams();
		SetupAudioPlayers();
		PlayMusic();

		// 3. Podłączamy się do sygnału dla NOWYCH przycisków (np. ładowanie sceny)
		GetTree().NodeAdded += OnNodeAdded;

		// 4. Skanujemy przyciski JUŻ ISTNIEJĄCE w pierwszej scenie
		// Używamy CallDeferred, żeby mieć pewność, że drzewo jest gotowe (fix dla stabilności)
		CallDeferred(nameof(SafeScanTree));

		GD.Print("✅ SoundManager ready!");
	}

	private void LoadAudioStreams()
	{
		hoverStream   = GD.Load<AudioStream>(AudioHoverPath);
		buttonStream  = GD.Load<AudioStream>(AudioButtonPath);
		bgMusicStream = GD.Load<AudioStream>(AudioBgMusicPath);
	}

	private void SetupAudioPlayers()
	{
		// --- MUZYKA ---
		musicPlayer = new AudioStreamPlayer();
		musicPlayer.Stream = bgMusicStream;
		musicPlayer.VolumeDb = -15.0f;
		musicPlayer.Bus = "Music";
		AddChild(musicPlayer);

		// --- SFX KLIK ---
		sfxClick = new AudioStreamPlayer();
		sfxClick.Stream = buttonStream;
		sfxClick.VolumeDb = -5.0f;
		sfxClick.Bus = "SFX";
		AddChild(sfxClick);

		// --- SFX HOVER ---
		sfxHover = new AudioStreamPlayer();
		sfxHover.Stream = hoverStream;
		sfxHover.VolumeDb = -10.0f;
		sfxHover.Bus = "SFX";
		AddChild(sfxHover);
	}

	private void PlayMusic()
	{
		if (musicPlayer != null && musicPlayer.Stream != null && !musicPlayer.Playing)
		{
			musicPlayer.Play();
		}
	}

	// --- LOGIKA PODŁĄCZANIA ---

	private void SafeScanTree()
	{
		if (GetTree() == null || GetTree().Root == null) return;
		ScanTreeForButtons(GetTree().Root);
	}

	private void OnNodeAdded(Node node)
	{
		ConnectButtonSignals(node);
	}

	private void ScanTreeForButtons(Node node)
	{
		ConnectButtonSignals(node);

		// Rekurencja dla dzieci
		foreach (Node child in node.GetChildren())
		{
			ScanTreeForButtons(child);
		}
	}

	private void ConnectButtonSignals(Node node)
	{
		// Działamy tylko na przyciskach
		if (node is BaseButton button)
		{
			// FIX: Metoda "Na Pieczątkę"
			if (button.HasMeta("SoundConnected")) 
			{
				return; // Już podłączony, wychodzimy!
			}

			// Podłączamy (tylko raz!)
			button.MouseEntered += PlayHover;
			button.Pressed += PlayClick;

			// Przybijamy pieczątkę
			button.SetMeta("SoundConnected", true);
		}
	}

	// --- ODTWARZANIE ---

	private void PlayHover()
	{
		if (sfxHover != null)
		{
			// Lekka losowość tonacji dla lepszego efektu
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
	if (GetTree() != null)
	{
		GetTree().NodeAdded -= OnNodeAdded;
	}
	}
}
