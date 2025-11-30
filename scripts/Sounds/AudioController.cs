using Godot;
using System;

// Pamiętaj: Ten skrypt musi być w Autoload jako "AudioController"
public partial class AudioController : Node
{
	// --- ŚCIEŻKI ---
	// Upewnij się, że te pliki istnieją w projekcie!
	private const string PathHover = "res://Scripts/Sounds/Hover.ogg";
	private const string PathButton = "res://Scripts/Sounds/Button.ogg";
	private const string PathBgMusic = "res://Scripts/Sounds/Background.mp3";

	private AudioStreamPlayer _musicPlayer;
	private AudioStreamPlayer _sfxHover;
	private AudioStreamPlayer _sfxClick;

	public override void _Ready()
	{
		GD.Print("🎵 [AudioController] Inicjalizacja dźwięku...");

		// 1. Konfiguracja odtwarzaczy
		SetupAudioPlayers();

		// 2. Start muzyki
		PlayMusic();

		// 3. Podłączenie do sygnału dodawania węzłów (dla przyszłych scen)
		GetTree().NodeAdded += OnNodeAdded;
		
		// 4. Skanowanie obecnej sceny (dla przycisków, które już są)
		ConnectExistingNodes(GetTree().Root);
	}

	private void SetupAudioPlayers()
	{
		// Tworzymy odtwarzacz muzyki
		_musicPlayer = new AudioStreamPlayer();
		// Jeśli plik nie istnieje, gra nie wywali błędu, tylko napisze komunikat w konsoli
		if (ResourceLoader.Exists(PathBgMusic)) _musicPlayer.Stream = GD.Load<AudioStream>(PathBgMusic);
		else GD.PrintErr($"❌ Brak pliku: {PathBgMusic}");
		
		_musicPlayer.VolumeDb = -15.0f; // To jest głośność bazowa pliku (niezależna od suwaka w Settings)
		_musicPlayer.ProcessMode = ProcessModeEnum.Always; 
		_musicPlayer.Bus = "Music"; // Ważne: Musi pasować do nazwy w zakładce Audio
		AddChild(_musicPlayer);

		// Tworzymy odtwarzacz Click
		_sfxClick = new AudioStreamPlayer();
		if (ResourceLoader.Exists(PathButton)) _sfxClick.Stream = GD.Load<AudioStream>(PathButton);
		
		_sfxClick.VolumeDb = -5.0f;
		_sfxClick.Bus = "SFX";
		AddChild(_sfxClick);

		// Tworzymy odtwarzacz Hover
		_sfxHover = new AudioStreamPlayer();
		if (ResourceLoader.Exists(PathHover)) _sfxHover.Stream = GD.Load<AudioStream>(PathHover);
		
		_sfxHover.VolumeDb = -10.0f;
		_sfxHover.Bus = "SFX";
		AddChild(_sfxHover);
	}

	private void PlayMusic()
	{
		if (_musicPlayer.Stream != null && !_musicPlayer.Playing)
		{
			_musicPlayer.Play();
		}
	}

	// Funkcja rekurencyjna do znalezienia wszystkich przycisków przy starcie gry
	private void ConnectExistingNodes(Node node)
	{
		OnNodeAdded(node); // Sprawdź ten węzeł
		
		foreach (Node child in node.GetChildren())
		{
			ConnectExistingNodes(child); // Sprawdź dzieci
		}
	}

	// Wykrywanie przycisków
	private void OnNodeAdded(Node node)
	{
		if (node is Button || node is TextureButton)
		{
			BaseButton btn = (BaseButton)node;

			// Sprawdzamy, czy już nie podłączyliśmy, żeby nie dublować dźwięków
			if (!btn.IsConnected(Control.SignalName.MouseEntered, Callable.From(PlayHover)))
			{
				btn.MouseEntered += PlayHover;
				btn.Pressed += PlayClick;
			}
		}
	}

	private void PlayHover()
	{
		if (_sfxHover.Stream == null) return;
		_sfxHover.PitchScale = (float)GD.RandRange(0.95, 1.05);
		_sfxHover.Play();
	}

	private void PlayClick()
	{
		if (_sfxClick.Stream == null) return;
		_sfxClick.Play();
	}
}
