using Godot;
using System.Collections.Generic;

public partial class SettingsManager : Node
{
	public static SettingsManager Instance { get; private set; }

	private const string SAVE_PATH = "user://settings.cfg";
	private const float MIN_DB = -80.0f;
	
	// Stała szerokość, pod którą projektowałeś UI (Full HD)
	private const float DESIGN_WIDTH = 1920.0f; 
	// Maksymalna skala UI jaką dopuszczamy
	private const float MAX_UI_SCALE = 3.0f; 

	public enum WindowMode
	{
		Windowed = 0,
		Borderless = 1,
		Fullscreen = 2
	}

	public class SoundSettings
	{
		public float MasterVolume { get; set; } = 1.0f;
		public float MusicVolume  { get; set; } = 1.0f;
		public float SfxVolume    { get; set; } = 1.0f;
		public bool Muted         { get; set; } = false;
	}

	public class VideoSettings
	{
		public WindowMode DisplayMode { get; set; } = WindowMode.Windowed;
		public Vector2I Resolution { get; set; } = new Vector2I(1920, 1080);
		public float UiScale { get; set; } = 1.0f;
		public bool VSync    { get; set; } = true;
	}

	public SoundSettings Sound { get; private set; } = new SoundSettings();
	public VideoSettings Video { get; private set; } = new VideoSettings();

	private int busIndexMaster;
	private int busIndexMusic;
	private int busIndexSfx;

	public readonly List<Vector2I> availableResolutions = new List<Vector2I>
	{
		new Vector2I(3840, 2160), new Vector2I(3440, 1440),
		new Vector2I(2560, 1440), new Vector2I(1920, 1200),
		new Vector2I(1920, 1080), new Vector2I(1600, 900),
		new Vector2I(1366, 768),  new Vector2I(1280, 720)
	};

	public override void _Ready()
	{
		if (Instance != null && Instance != this)
		{
			QueueFree();
			return;
		}

		Instance = this;
		ProcessMode = ProcessModeEnum.Always;

		busIndexMaster = AudioServer.GetBusIndex("Master");
		busIndexMusic  = AudioServer.GetBusIndex("Music");
		busIndexSfx    = AudioServer.GetBusIndex("SFX");

		AddNativeResolution();
		LoadConfig();
		ApplyAllSettings();

		GD.Print("✅ SettingsManager gotowy i wczytany.");
	}

	private void AddNativeResolution()
	{
		Vector2I screenRes = DisplayServer.ScreenGetSize();
		if (!availableResolutions.Contains(screenRes))
		{
			availableResolutions.Insert(0, screenRes);
		}
	}

	// --- NOWA METODA POMOCNICZA ---
	// Oblicza idealną skalę na podstawie aktualnego monitora.
	private float GetAutoCalculatedScale()
	{
		Vector2I screenRes = DisplayServer.ScreenGetSize();
		float autoScale = (float)screenRes.X / DESIGN_WIDTH;
		// Tu też używamy MAX_UI_SCALE (3.0f)
		return Mathf.Clamp(autoScale, 0.5f, MAX_UI_SCALE);
	}
	
	public void LoadConfig()
	{
		var config = new ConfigFile();
		var err = config.Load(SAVE_PATH);

		if (err != Error.Ok)
		{
			GD.Print("⚠ Brak pliku ustawień. Ustawiam wartości domyślne.");
			SetDefaultDefaultsBasedOnHardware();
			return;
		}

		// Sound
		Sound.MasterVolume = (float)config.GetValue("Sound", "MasterVolume", 1.0f);
		Sound.MusicVolume  = (float)config.GetValue("Sound", "MusicVolume",  1.0f);
		Sound.SfxVolume    = (float)config.GetValue("Sound", "SfxVolume",    1.0f);
		Sound.Muted        = (bool) config.GetValue("Sound", "Muted",        false);

		// Video
		int modeInt = (int)config.GetValue("Video", "DisplayMode", (int)WindowMode.Windowed);
		Video.DisplayMode = (WindowMode)modeInt;

		int resX = (int)config.GetValue("Video", "ResolutionWidth",  1920);
		int resY = (int)config.GetValue("Video", "ResolutionHeight", 1080);
		Video.Resolution = new Vector2I(resX, resY);

		// --- POPRAWKA ---
		// Jeśli w pliku brakuje klucza "UiScale", używamy wyliczonej wartości, a nie 1.0f.
		float fallbackScale = GetAutoCalculatedScale();
		float rawScale = (float)config.GetValue("Video", "UiScale", fallbackScale);
		
		// WAŻNE: Tutaj był stary Clamp 2.0f, który psuł wczytywanie większych skal!
		Video.UiScale = Mathf.Clamp(rawScale, 0.5f, MAX_UI_SCALE);

		Video.VSync = (bool)config.GetValue("Video", "VSync", true);

		if (!availableResolutions.Contains(Video.Resolution))
		{
			availableResolutions.Add(Video.Resolution);
		}
		
		GD.Print($"📂 Ustawienia załadowane. Skala UI: {Video.UiScale}");
	}

	private void SetDefaultDefaultsBasedOnHardware()
	{
		Vector2I screenRes = DisplayServer.ScreenGetSize();
		Video.Resolution = screenRes;
		Video.DisplayMode = WindowMode.Fullscreen;
		
		// Używamy wspólnej metody
		Video.UiScale = GetAutoCalculatedScale();
		
		GD.Print($"[Auto-Setup] Wykryto: {screenRes}. Ustawiono skalę UI na: {Video.UiScale}");
	}

	public void SaveConfig()
	{
		var config = new ConfigFile();

		config.SetValue("Sound", "MasterVolume", Sound.MasterVolume);
		config.SetValue("Sound", "MusicVolume",  Sound.MusicVolume);
		config.SetValue("Sound", "SfxVolume",    Sound.SfxVolume);
		config.SetValue("Sound", "Muted",        Sound.Muted);

		config.SetValue("Video", "DisplayMode",      (int)Video.DisplayMode);
		config.SetValue("Video", "ResolutionWidth",  Video.Resolution.X);
		config.SetValue("Video", "ResolutionHeight", Video.Resolution.Y);
		config.SetValue("Video", "UiScale",          Video.UiScale);
		config.SetValue("Video", "VSync",            Video.VSync);

		config.Save(SAVE_PATH);
		GD.Print("💾 Ustawienia zapisane na dysku.");
	}

	// --- SETTERY ---

	public void SetMasterVolume(float linear) { Sound.MasterVolume = linear; ApplyVolume(busIndexMaster, linear); }
	public void SetMusicVolume(float linear)  { Sound.MusicVolume = linear; ApplyVolume(busIndexMusic, linear); }
	public void SetSfxVolume(float linear)    { Sound.SfxVolume = linear; ApplyVolume(busIndexSfx, linear); }

	public void SetMuted(bool muted)
	{
		Sound.Muted = muted;
		if (busIndexMaster != -1) AudioServer.SetBusMute(busIndexMaster, muted);
	}

	private void ApplyVolume(int busIndex, float linear)
	{
		if (busIndex == -1) return;
		float db = linear > 0.001f ? Mathf.LinearToDb(linear) : MIN_DB;
		AudioServer.SetBusVolumeDb(busIndex, db);
	}

	public void SetDisplayMode(WindowMode mode)
	{
		Video.DisplayMode = mode;
		ApplyWindowMode();
	}

	public void SetResolution(Vector2I res)
	{
		Video.Resolution = res;
		ApplyWindowMode();
	}
	
	public void SetResolutionByIndex(int index)
	{
		if (index >= 0 && index < availableResolutions.Count)
		{
			SetResolution(availableResolutions[index]);
		}
	}
	
	public int GetCurrentResolutionIndex() => availableResolutions.IndexOf(Video.Resolution);

	public void SetUiScale(float value)
	{
		float safeValue = Mathf.Clamp(value, 0.5f, MAX_UI_SCALE);
		Video.UiScale = safeValue;
		GetTree().Root.ContentScaleFactor = safeValue;
	}

	public void SetVSync(bool enabled)
	{
		Video.VSync = enabled;
		DisplayServer.WindowSetVsyncMode(enabled ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
	}

	public void ApplyAllSettings()
	{
		SetMasterVolume(Sound.MasterVolume);
		SetMusicVolume(Sound.MusicVolume);
		SetSfxVolume(Sound.SfxVolume);
		SetMuted(Sound.Muted);
		SetUiScale(Video.UiScale);
		SetVSync(Video.VSync);
		ApplyWindowMode();
	}

	private void ApplyWindowMode()
	{
		switch (Video.DisplayMode)
		{
			case WindowMode.Windowed:
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
				DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
				SetWindowSizeAndCenter(); 
				break;

			case WindowMode.Borderless:
				Vector2I nativeRes = DisplayServer.ScreenGetSize();
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
				DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
				DisplayServer.WindowSetSize(nativeRes);
				DisplayServer.WindowSetPosition(Vector2I.Zero);
				break;

			case WindowMode.Fullscreen:
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
				break;
		}
	}
	
	private async void SetWindowSizeAndCenter()
	{
		DisplayServer.WindowSetSize(Video.Resolution);
		await ToSignal(GetTree().CreateTimer(0.2f), SceneTreeTimer.SignalName.Timeout);

		Vector2I screenRes = DisplayServer.ScreenGetSize();
		Vector2I pos = (screenRes / 2) - (Video.Resolution / 2);
		
		if (pos.X < 0) pos.X = 0;
		if (pos.Y < 0) pos.Y = 0;
		
		DisplayServer.WindowSetPosition(pos);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}
