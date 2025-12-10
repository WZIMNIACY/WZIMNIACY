using Godot;
using System;
using System.Collections.Generic;

public partial class SettingsManager : Node
{
	public static SettingsManager Instance { get; private set; }

	private const string SAVE_PATH = "user://settings.cfg";
	private const float MIN_DB = -80.0f;

	public class SoundSettings
	{
		public float MasterVolume { get; set; } = 1.0f;
		public float MusicVolume  { get; set; } = 1.0f;
		public float SfxVolume    { get; set; } = 1.0f;
		public bool Muted         { get; set; } = false;
	}

	public class VideoSettings
	{
		// 0 = okno, 1 = fullscreen
		public int   DisplayMode     { get; set; } = 0;
		public int   ResolutionIndex { get; set; } = 4; // np. 1920x1080
		public float UiScale         { get; set; } = 1.0f;
		public bool  VSync           { get; set; } = true;
	}

	public SoundSettings Sound { get; private set; } = new SoundSettings();
	public VideoSettings Video { get; private set; } = new VideoSettings();

	// Indeksy busów audio
	private int _busIndexMaster;
	private int _busIndexMusic;
	private int _busIndexSfx;

	// Lista dostępnych rozdzielczości
	// UWAGA: Teraz będziemy do niej dodawać dynamicznie, więc readonly dotyczy tylko referencji
	public readonly List<Vector2I> AvailableResolutions = new List<Vector2I>
	{
		new Vector2I(3840, 2160), new Vector2I(3440, 1440),
		new Vector2I(2560, 1440), new Vector2I(1920, 1200),
		new Vector2I(1920, 1080), new Vector2I(1600, 900),
		new Vector2I(1366, 768),  new Vector2I(1280, 720)
	};

	public override void _Ready()
	{
		// Singleton
		if (Instance != null && Instance != this)
		{
			QueueFree();
			return;
		}

		Instance = this;
		
		// WAŻNE: Manager musi działać zawsze, nawet gdy gra jest zapauzowana (Menu Pauzy)
		ProcessMode = ProcessModeEnum.Always;

		_busIndexMaster = AudioServer.GetBusIndex("Master");
		_busIndexMusic  = AudioServer.GetBusIndex("Music");
		_busIndexSfx    = AudioServer.GetBusIndex("SFX");

		// 1. Najpierw wykrywamy rozdzielczość monitora
		AddNativeResolution();

		// 2. Ładujemy config
		LoadConfig();
		
		// 3. Sprawdzamy czy załadowany indeks ma sens (bo lista mogła się zmienić)
		ValidateResolutionIndex();

		// 4. Aplikujemy wszystko
		ApplyAllSettings();

		GD.Print("✅ SettingsManager gotowy – config załadowany i zastosowany.");
	}


private void AddNativeResolution()
{
	// 1. Pobierz rozmiar ekranu gracza
	Vector2I screenRes = DisplayServer.ScreenGetSize();

	// 2. Jeśli nie ma jej na liście -> dodaj
	if (!AvailableResolutions.Contains(screenRes))
	{
		AvailableResolutions.Add(screenRes);
		GD.Print($"🖥️ Dodano natywną rozdzielczość gracza: {screenRes}");
	}

	// 3. SORTOWANIE
	// Sortujemy malejąco (Największa -> Najmniejsza), żeby pasowało do Twojej listy.
	AvailableResolutions.Sort((a, b) =>
	{
		// Najpierw porównaj szerokość (X)
		// Używamy b.CompareTo(a), żeby sortować MALEJĄCO
		int result = b.X.CompareTo(a.X);

		// Jeśli szerokości są takie same (np. 1920x1080 i 1920x1200),
		// to porównaj wysokość (Y)
		if (result == 0)
		{
			return b.Y.CompareTo(a.Y);
		}

		return result;
	});
}

	private void ValidateResolutionIndex()
	{
		// Zabezpieczenie: jeśli zapisany indeks jest większy niż długość listy
		// (np. config miał index 10, a teraz mamy 8 opcji), resetujemy do bezpiecznej wartości.
		if (Video.ResolutionIndex < 0 || Video.ResolutionIndex >= AvailableResolutions.Count)
		{
			GD.Print("⚠ Wykryto nieprawidłowy indeks rozdzielczości. Resetowanie do domyślnego.");
			// Próbujemy znaleźć 1920x1080 jako bezpieczny start, lub bierzemy pierwszy z brzegu
			int defaultIndex = AvailableResolutions.IndexOf(new Vector2I(1920, 1080));
			if (defaultIndex == -1) defaultIndex = 0; // Jeśli nie ma FHD, weź największą
			
			Video.ResolutionIndex = defaultIndex;
		}
	}

	public void LoadConfig()
	{
		var config = new ConfigFile();
		var err = config.Load(SAVE_PATH);

		if (err != Error.Ok)
		{
			GD.Print("⚠ Brak pliku ustawień, używam domyślnych.");
			return;
		}

		// Sound
		Sound.MasterVolume = (float)config.GetValue("Sound", "MasterVolume", 1.0f);
		Sound.MusicVolume  = (float)config.GetValue("Sound", "MusicVolume",  1.0f);
		Sound.SfxVolume    = (float)config.GetValue("Sound", "SfxVolume",    1.0f);
		Sound.Muted        = (bool) config.GetValue("Sound", "Muted",        false);

		// Video
		Video.DisplayMode     = (int)   config.GetValue("Video", "DisplayMode",     0);
		Video.ResolutionIndex = (int)   config.GetValue("Video", "ResolutionIndex", 4);
		Video.UiScale         = (float) config.GetValue("Video", "UiScale",         1.0f);
		Video.VSync           = (bool)  config.GetValue("Video", "VSync",           true);

		GD.Print("📂 Ustawienia załadowane z pliku.");
	}

	public void SaveConfig()
	{
		var config = new ConfigFile();

		// Sound
		config.SetValue("Sound", "MasterVolume", Sound.MasterVolume);
		config.SetValue("Sound", "MusicVolume",  Sound.MusicVolume);
		config.SetValue("Sound", "SfxVolume",    Sound.SfxVolume);
		config.SetValue("Sound", "Muted",        Sound.Muted);

		// Video
		config.SetValue("Video", "DisplayMode",     Video.DisplayMode);
		config.SetValue("Video", "ResolutionIndex", Video.ResolutionIndex);
		config.SetValue("Video", "UiScale",         Video.UiScale);
		config.SetValue("Video", "VSync",           Video.VSync);

		config.Save(SAVE_PATH);
		GD.Print("💾 Ustawienia zapisane na dysku (SettingsManager).");
	}


	public void SetMasterVolume(float linear)
	{
		Sound.MasterVolume = linear;
		ApplyVolume(_busIndexMaster, linear);
	}

	public void SetMusicVolume(float linear)
	{
		Sound.MusicVolume = linear;
		ApplyVolume(_busIndexMusic, linear);
	}

	public void SetSfxVolume(float linear)
	{
		Sound.SfxVolume = linear;
		ApplyVolume(_busIndexSfx, linear);
	}

	public void SetMuted(bool muted)
	{
		Sound.Muted = muted;
		if (_busIndexMaster != -1)
			AudioServer.SetBusMute(_busIndexMaster, muted);
	}

	private void ApplyVolume(int busIndex, float linear)
	{
		if (busIndex == -1)
			return;

		// konwersja 0–1 -> dB, z podłogą na -80 dB
		float db = linear > 0.001f ? Mathf.LinearToDb(linear) : MIN_DB;
		AudioServer.SetBusVolumeDb(busIndex, db);
	}

	public void SetDisplayMode(int mode)
	{
		Video.DisplayMode = mode;
		ApplyWindowMode();
	}

	public void SetResolutionIndex(int index)
	{
		// Zabezpieczenie przed wyjściem poza zakres przy klikaniu
		if (index >= 0 && index < AvailableResolutions.Count)
		{
			Video.ResolutionIndex = index;
			ApplyWindowMode();
		}
	}

	public void SetUiScale(float scale)
	{
		Video.UiScale = scale;
		GetTree().Root.ContentScaleFactor = scale;
	}

	public void SetVSync(bool enabled)
	{
		Video.VSync = enabled;
		DisplayServer.WindowSetVsyncMode(
			enabled ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled
		);
	}


	public void ApplyAllSettings()
	{
		// Audio
		SetMasterVolume(Sound.MasterVolume);
		SetMusicVolume(Sound.MusicVolume);
		SetSfxVolume(Sound.SfxVolume);
		SetMuted(Sound.Muted);

		// Video
		SetUiScale(Video.UiScale);
		SetVSync(Video.VSync);
		ApplyWindowMode();
	}

	private void ApplyWindowMode()
	{
		if (Video.DisplayMode == 0) // okno
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
			DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);

			if (Video.ResolutionIndex >= 0 && Video.ResolutionIndex < AvailableResolutions.Count)
			{
				Vector2I size = AvailableResolutions[Video.ResolutionIndex];
				DisplayServer.WindowSetSize(size);
				CenterWindow();
			}
		}
		else // fullscreen
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
		}
	}

	private void CenterWindow()
	{
		int screenId    = DisplayServer.WindowGetCurrentScreen();
		Vector2I screen = DisplayServer.ScreenGetSize(screenId);
		Vector2I win    = DisplayServer.WindowGetSize();
		Vector2I pos    = (screen / 2) - (win / 2);
		DisplayServer.WindowSetPosition(pos);
	}
}
