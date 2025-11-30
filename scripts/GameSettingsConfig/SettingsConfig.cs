using Godot;
using System;
using System.Collections.Generic;

public partial class SettingsConfig : Node
{
	public static SettingsConfig Instance { get; private set; }

	private const string SAVE_PATH = "user://settings.cfg";
	private const float MIN_DB = -80.0f;

	// --- 1. ZAKTUALIZOWANA LISTA ROZDZIELCZOŚCI ---
	public readonly List<Vector2I> AvailableResolutions = new List<Vector2I>
	{
		new Vector2I(3840, 2160), 
		new Vector2I(3440, 1440),
		new Vector2I(2560, 1440), 
		new Vector2I(1920, 1200),
		new Vector2I(1920, 1080), 
		new Vector2I(1600, 900),
		new Vector2I(1366, 768),  
		new Vector2I(1280, 720)
	};

	public class SoundSettings
	{
		public float MasterVolume { get; set; } = 1.0f;
		public float MusicVolume { get; set; } = 1.0f;
		public float SFXVolume { get; set; } = 1.0f;
		public bool Muted { get; set; } = false;
	}

	public class VideoSettings
	{
		public int DisplayMode { get; set; } = 0; // 0=Windowed, 1=Fullscreen
		public int ResolutionIndex { get; set; } = 4; // Domyślnie 1920x1080 (index 4 na liście)
		public float UIScale { get; set; } = 1.0f;
		public bool VSync { get; set; } = true;
	}

	public SoundSettings Sound { get; private set; } = new SoundSettings();
	public VideoSettings Video { get; private set; } = new VideoSettings();

	private int _busMaster, _busMusic, _busSFX;

	public override void _Ready()
	{
		Instance = this;
		
		_busMaster = AudioServer.GetBusIndex("Master");
		_busMusic = AudioServer.GetBusIndex("Music");
		_busSFX = AudioServer.GetBusIndex("SFX");

		if (!LoadSettings())
		{
			SetDefaultValues();
		}
		
		CallDeferred(nameof(ApplyAllSettings));
	}

	// --- LOGIKA AUDIO ---
	public void ChangeVolume(string busName, float linearValue)
	{
		int busIdx = -1;
		switch (busName)
		{
			case "Master": Sound.MasterVolume = linearValue; busIdx = _busMaster; break;
			case "Music": Sound.MusicVolume = linearValue; busIdx = _busMusic; break;
			case "SFX": Sound.SFXVolume = linearValue; busIdx = _busSFX; break;
		}

		if (busIdx != -1)
		{
			float db = linearValue > 0.001f ? Mathf.LinearToDb(linearValue) : MIN_DB;
			AudioServer.SetBusVolumeDb(busIdx, db);
		}
	}

	public void SetMute(bool muted)
	{
		Sound.Muted = muted;
		if (_busMaster != -1) AudioServer.SetBusMute(_busMaster, muted);
	}

	// --- LOGIKA VIDEO I OKNA ---

	// Metody wywoływane przez Settings.cs
	public void SetWindowMode(int mode) 
	{ 
		Video.DisplayMode = mode; 
		ApplyWindowMode(); 
	}
	
	public void SetResolution(int idx) 
	{ 
		Video.ResolutionIndex = idx; 
		ApplyWindowMode(); 
	}
	
	public void SetUIScale(float s) 
	{ 
		Video.UIScale = s; 
		GetTree().Root.ContentScaleFactor = s; 
	}

	private void ApplyWindowMode()
	{
		int mode = Video.DisplayMode;
		
		if (mode == 0) // Windowed (W oknie)
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
			DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
			
			// Ustawienie rozmiaru
			if (Video.ResolutionIndex >= 0 && Video.ResolutionIndex < AvailableResolutions.Count)
			{
				Vector2I targetSize = AvailableResolutions[Video.ResolutionIndex];
				DisplayServer.WindowSetSize(targetSize);
				
				// WAŻNE: Centrowanie wywołujemy z opóźnieniem (CallDeferred).
				// System potrzebuje milisekundy na zmianę rozmiaru ramki, zanim obliczy pozycję.
				CallDeferred(nameof(CenterWindow));
			}
		}
		else // Fullscreen
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
		}
	}

	// Nowa metoda do centrowania okna
	private void CenterWindow()
	{
		// 1. Pobierz ekran, na którym jest gra
		int screenId = DisplayServer.WindowGetCurrentScreen();
		
		// 2. Pobierz rozmiar tego ekranu
		Vector2I screenSize = DisplayServer.ScreenGetSize(screenId);
		
		// 3. Pobierz aktualny rozmiar okna gry
		Vector2I windowSize = DisplayServer.WindowGetSize();
		
		// 4. Oblicz środek: (Ekran / 2) - (Okno / 2)
		Vector2I centerPos = (screenSize / 2) - (windowSize / 2);
		
		// 5. Ustaw pozycję, upewniając się, że nie wyjdzie poza lewą krawędź (np. na drugi monitor w złym miejscu)
		// Opcjonalnie można dodać sprawdzenie, czy pozycja nie jest ujemna, ale zazwyczaj prosta matematyka wystarcza.
		DisplayServer.WindowSetPosition(centerPos);
	}

	// --- LOGIKA ZAPISU / ODCZYTU ---

	private void SetDefaultValues()
	{
		Sound.MasterVolume = 1.0f;
		Sound.MusicVolume = 1.0f;
		Sound.SFXVolume = 1.0f;
		Sound.Muted = false;
		
		Video.DisplayMode = 0;
		Video.ResolutionIndex = 4; // 1920x1080
		Video.UIScale = 1.0f;
		Video.VSync = true;
	}

	private void ApplyAllSettings()
	{
		ChangeVolume("Master", Sound.MasterVolume);
		ChangeVolume("Music", Sound.MusicVolume);
		ChangeVolume("SFX", Sound.SFXVolume);
		SetMute(Sound.Muted);
		
		SetUIScale(Video.UIScale);
		DisplayServer.WindowSetVsyncMode(Video.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
		
		ApplyWindowMode();
	}

	public void SaveSettings()
	{
		var config = new ConfigFile();
		config.SetValue("Sound", "MasterVolume", Sound.MasterVolume);
		config.SetValue("Sound", "MusicVolume", Sound.MusicVolume);
		config.SetValue("Sound", "SFXVolume", Sound.SFXVolume);
		config.SetValue("Sound", "Muted", Sound.Muted);
		
		config.SetValue("Video", "DisplayMode", Video.DisplayMode);
		config.SetValue("Video", "ResolutionIndex", Video.ResolutionIndex);
		config.SetValue("Video", "UIScale", Video.UIScale);
		config.SetValue("Video", "VSync", Video.VSync);

		config.Save(SAVE_PATH);
		GD.Print("💾 Ustawienia zapisane.");
	}

	private bool LoadSettings()
	{
		var config = new ConfigFile();
		if (config.Load(SAVE_PATH) != Error.Ok) return false;

		Sound.MasterVolume = (float)config.GetValue("Sound", "MasterVolume", 1.0f);
		Sound.MusicVolume = (float)config.GetValue("Sound", "MusicVolume", 1.0f);
		Sound.SFXVolume = (float)config.GetValue("Sound", "SFXVolume", 1.0f);
		Sound.Muted = (bool)config.GetValue("Sound", "Muted", false);

		Video.DisplayMode = (int)config.GetValue("Video", "DisplayMode", 0);
		Video.ResolutionIndex = (int)config.GetValue("Video", "ResolutionIndex", 4);
		Video.UIScale = (float)config.GetValue("Video", "UIScale", 1.0f);
		Video.VSync = (bool)config.GetValue("Video", "VSync", true);
		
		return true;
	}
}
