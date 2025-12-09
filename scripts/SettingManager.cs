using Godot;
using System;
using System.Collections.Generic;

public partial class SettingsManager : Node
{
	public static SettingsManager Instance { get; private set; }

	private const string SAVE_PATH = "user://settings.cfg";
	private const float MIN_DB = -80.0f;

	// =============================
	// KLASY DANYCH
	// =============================

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

		_busIndexMaster = AudioServer.GetBusIndex("Master");
		_busIndexMusic  = AudioServer.GetBusIndex("Music");
		_busIndexSfx    = AudioServer.GetBusIndex("SFX");

		LoadConfig();
		ApplyAllSettings();

		GD.Print("✅ SettingsManager gotowy – config załadowany i zastosowany.");
	}

	// =============================
	// ŁADOWANIE / ZAPIS
	// =============================

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

	// =============================
	// AUDIO – SETTERY + ZASTOSOWANIE
	// =============================

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

	// =============================
	// VIDEO – SETTERY + ZASTOSOWANIE
	// =============================

	public void SetDisplayMode(int mode)
	{
		Video.DisplayMode = mode;
		ApplyWindowMode();
	}

	public void SetResolutionIndex(int index)
	{
		Video.ResolutionIndex = index;
		ApplyWindowMode();
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

	// =============================
	// ZASTOSOWANIE CAŁOŚCI
	// =============================

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
