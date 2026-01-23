using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Manages application settings including audio, video, and window configuration.
/// serialized to a config file.
/// </summary>
public partial class SettingsManager : Node
{
	// Zdarzenie, na które UI nasłuchuje, aby zaktualizować suwak
	/// <summary>
	/// Event triggered when the UI scale changes.
	/// </summary>
	public event Action<float> OnUiScaleChanged;

	/// <summary>
	/// Singleton instance of the SettingsManager.
	/// </summary>
	public static SettingsManager Instance { get; private set; }

	private const string SAVE_PATH = "user://settings.cfg";
	private const float MIN_DB = -80.0f;
	
	// Wymiary bazowe
	private const float DESIGN_WIDTH = 1152.0f;
	private const float DESIGN_HEIGHT = 648.0f;
	
	// Limit skali UI
	private const float MAX_UI_SCALE = 3.0f; 

	/// <summary>
	/// Defines available window modes.
	/// </summary>
	public enum WindowMode
	{
		/// <summary>Standard windowed mode.</summary>
		Windowed = 0,
		/// <summary>Borderless windowed mode.</summary>
		Borderless = 1,
		/// <summary>Exclusive fullscreen mode.</summary>
		Fullscreen = 2
	}

	/// <summary>
	/// Container for audio settings.
	/// </summary>
	public class SoundSettings
	{
		/// <summary>Master volume level (0.0 to 1.0).</summary>
		public float MasterVolume { get; set; } = 1.0f;
		/// <summary>Music volume level (0.0 to 1.0).</summary>
		public float MusicVolume  { get; set; } = 1.0f;
		/// <summary>Sound effects volume level (0.0 to 1.0).</summary>
		public float SfxVolume    { get; set; } = 1.0f;
		/// <summary>Whether audio is globally muted.</summary>
		public bool Muted         { get; set; } = false;
	}

	/// <summary>
	/// Container for video/graphics settings.
	/// </summary>
	public class VideoSettings
	{
		/// <summary>Current window display mode.</summary>
		public WindowMode DisplayMode { get; set; } = WindowMode.Windowed;
		/// <summary>Current screen resolution.</summary>
		public Vector2I Resolution { get; set; } = new Vector2I(1920, 1080);
		/// <summary>UI scaling factor.</summary>
		public float UiScale { get; set; } = 1.0f;
		/// <summary>Whether Vertical Sync is enabled.</summary>
		public bool VSync    { get; set; } = true;
	}

	/// <summary>Gets the current sound settings.</summary>
	public SoundSettings Sound { get; private set; } = new SoundSettings();
	/// <summary>Gets the current video settings.</summary>
	public VideoSettings Video { get; private set; } = new VideoSettings();

	private int busIndexMaster;
	private int busIndexMusic;
	private int busIndexSfx;

	/// <summary>
	/// List of supported screen resolutions.
	/// </summary>
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

	/// <summary>
	/// Adds the current screen resolution to the available list if not present.
	/// </summary>
	private void AddNativeResolution()
	{
		Vector2I screenRes = DisplayServer.ScreenGetSize();
		if (!availableResolutions.Contains(screenRes))
		{
			availableResolutions.Insert(0, screenRes);
		}
	}

	// --- LOGIKA SKALOWANIA ---
	/// <summary>
	/// Calculates the optimal UI scale based on resolution.
	/// </summary>
	/// <param name="targetRes">Optional target resolution; uses screen size if null.</param>
	/// <returns>The calculated UI scale factor.</returns>
	private float GetAutoCalculatedScale(Vector2I? targetRes = null)
	{
		Vector2I size = targetRes ?? DisplayServer.ScreenGetSize();
		
		float scaleX = size.X / DESIGN_WIDTH;
		float scaleY = size.Y / DESIGN_HEIGHT;
		
		float finalScale = Mathf.Min(scaleX, scaleY);
		
		return Mathf.Clamp(finalScale, 0.5f, MAX_UI_SCALE);
	}
	
	/// <summary>
	/// Loads settings from the configuration file. Falls back to defaults if file is missing.
	/// </summary>
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

		float fallbackScale = GetAutoCalculatedScale(Video.Resolution);
		float rawScale = (float)config.GetValue("Video", "UiScale", fallbackScale);
		
		Video.UiScale = Mathf.Clamp(rawScale, 0.5f, MAX_UI_SCALE);

		Video.VSync = (bool)config.GetValue("Video", "VSync", true);

		if (!availableResolutions.Contains(Video.Resolution))
		{
			availableResolutions.Add(Video.Resolution);
		}
		
		GD.Print($"📂 Ustawienia załadowane. Skala UI: {Video.UiScale}");
	}

	/// <summary>
	/// Sets default video settings based on detected hardware capabilities.
	/// </summary>
	private void SetDefaultDefaultsBasedOnHardware()
	{
		Vector2I screenRes = DisplayServer.ScreenGetSize();
		Video.Resolution = screenRes;
		Video.DisplayMode = WindowMode.Fullscreen;
		
		Video.UiScale = GetAutoCalculatedScale(screenRes);
		
		GD.Print($"[Auto-Setup] Wykryto: {screenRes}. Ustawiono skalę UI na: {Video.UiScale}");
	}

	/// <summary>
	/// Saves the current settings to the configuration file on disk.
	/// </summary>
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

	/// <summary>Sets the master volume.</summary>
	/// <param name="linear">Volume level (0.0 to 1.0).</param>
	public void SetMasterVolume(float linear) { Sound.MasterVolume = linear; ApplyVolume(busIndexMaster, linear); }
	/// <summary>Sets the music volume.</summary>
	/// <param name="linear">Volume level (0.0 to 1.0).</param>
	public void SetMusicVolume(float linear)  { Sound.MusicVolume = linear; ApplyVolume(busIndexMusic, linear); }
	/// <summary>Sets the SFX volume.</summary>
	/// <param name="linear">Volume level (0.0 to 1.0).</param>
	public void SetSfxVolume(float linear)    { Sound.SfxVolume = linear; ApplyVolume(busIndexSfx, linear); }

	/// <summary>
	/// Mutes or unmutes the master audio bus.
	/// </summary>
	/// <param name="muted">True to mute, false to unmute.</param>
	public void SetMuted(bool muted)
	{
		Sound.Muted = muted;
		if (busIndexMaster != -1) AudioServer.SetBusMute(busIndexMaster, muted);
	}

	/// <summary>
	/// Applies volume level to a specific audio bus.
	/// </summary>
	/// <param name="busIndex">Index of the audio bus.</param>
	/// <param name="linear">Linear volume level.</param>
	private void ApplyVolume(int busIndex, float linear)
	{
		if (busIndex == -1) return;
		float db = linear > 0.001f ? Mathf.LinearToDb(linear) : MIN_DB;
		AudioServer.SetBusVolumeDb(busIndex, db);
	}

	/// <summary>
	/// Sets the window display mode (Windowed, Borderless, or Fullscreen).
	/// </summary>
	/// <param name="mode">The desired window mode.</param>
	public void SetDisplayMode(WindowMode mode)
	{
		Video.DisplayMode = mode;
		ApplyWindowMode();
	}

	/// <summary>
	/// Sets the application resolution.
	/// </summary>
	/// <param name="res">The target resolution.</param>
	public void SetResolution(Vector2I res)
	{
		Video.Resolution = res;
		ApplyWindowMode();
	}
	
	/// <summary>
	/// Sets the resolution based on index in <see cref="availableResolutions"/>.
	/// </summary>
	/// <param name="index">Index of the resolution.</param>
	public void SetResolutionByIndex(int index)
	{
		if (index >= 0 && index < availableResolutions.Count)
		{
			SetResolution(availableResolutions[index]);
		}
	}
	
	/// <summary>
	/// Gets the index of the current resolution in the available resolutions list.
	/// </summary>
	/// <returns>Index of the current resolution, or -1 if not found.</returns>
	public int GetCurrentResolutionIndex() => availableResolutions.IndexOf(Video.Resolution);

	/// <summary>
	/// Sets the UI scale factor.
	/// </summary>
	/// <param name="value">The scale value.</param>
	public void SetUiScale(float value)
	{
		float safeValue = Mathf.Clamp(value, 0.5f, MAX_UI_SCALE);
		Video.UiScale = safeValue;
		GetTree().Root.ContentScaleFactor = safeValue;
		
		// Powiadamiamy UI o zmianie skali
		OnUiScaleChanged?.Invoke(safeValue);
	}

	/// <summary>
	/// Enables or disables Vertical Sync.
	/// </summary>
	/// <param name="enabled">True to enable VSync, false to disable.</param>
	public void SetVSync(bool enabled)
	{
		Video.VSync = enabled;
		DisplayServer.WindowSetVsyncMode(enabled ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
	}

	/// <summary>
	/// Applies all current settings (Audio and Video) to the application.
	/// </summary>
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

	// --- ZARZĄDZANIE OKNEM I SKALĄ ---

	/// <summary>
	/// Applies current window mode and resolution settings.
	/// </summary>
	private void ApplyWindowMode()
	{
		Vector2I targetResForScaling = Video.Resolution;
		
		if (Video.DisplayMode == WindowMode.Borderless || Video.DisplayMode == WindowMode.Fullscreen)
		{
			targetResForScaling = DisplayServer.ScreenGetSize();
		}
		
		float targetScale = GetAutoCalculatedScale(targetResForScaling);

		switch (Video.DisplayMode)
		{
			case WindowMode.Windowed:
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
				DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
				SetWindowSizeAndCenter(targetScale); 
				break;

			case WindowMode.Borderless:
				Vector2I nativeRes = DisplayServer.ScreenGetSize();
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
				DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
				
				GetWindow().Size = nativeRes;
				GetWindow().Position = Vector2I.Zero;
				
				ApplyScaleWithDelay(targetScale);
				break;

			case WindowMode.Fullscreen:
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
				ApplyScaleWithDelay(targetScale);
				break;
		}
	}

	/// <summary>
	/// Applies UI scale after a short delay to allow window resizing to complete.
	/// </summary>
	/// <param name="scale">The scale to apply.</param>
	private async void ApplyScaleWithDelay(float scale)
	{
		await ToSignal(GetTree().CreateTimer(0.15f), SceneTreeTimer.SignalName.Timeout);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		SetUiScale(scale);
	}
	
	/// <summary>
	/// Sets window size and centers it on screen, then applies UI scale.
	/// </summary>
	/// <param name="scaleToApply">Scale to apply after positioning.</param>
	private async void SetWindowSizeAndCenter(float scaleToApply)
	{
		// Zmiana rozmiaru
		GetWindow().Size = Video.Resolution;
		
		// Czekanie na OS
		await ToSignal(GetTree().CreateTimer(0.15f), SceneTreeTimer.SignalName.Timeout);

		// Centrowanie
		Vector2I screenRes = DisplayServer.ScreenGetSize();
		Vector2I pos = (screenRes / 2) - (Video.Resolution / 2);
		
		if (pos.X < 0) pos.X = 0;
		if (pos.Y < 0) pos.Y = 0;
		
		GetWindow().Position = pos;
		
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		
		// Aplikacja skali po ustabilizowaniu okna
		SetUiScale(scaleToApply);
	}
}
