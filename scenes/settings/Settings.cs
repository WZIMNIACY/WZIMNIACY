using Godot;
using System;
using System.Collections.Generic;

public partial class Settings : Control
{
	// --- ELEMENTY UI ---
	private Button _backButton;
	private Button _saveButton;

	private HSlider _masterVolumeSlider;
	private HSlider _musicVolumeSlider;
	private HSlider _sfxVolumeSlider;
	private CheckButton _mutedCheckBox;

	private OptionButton _resolutionOptionButton;
	private OptionButton _screenModeOptionButton;
	private HSlider _scaleUISlider;

	// Flaga, żeby nie odpalać sygnałów podczas inicjalizacji
	private bool _isInitializing = true;

	public override void _Ready()
	{
		GD.Print("🖼️ [SettingsUI] Otwieranie menu ustawień...");
		
		// 1. Sprawdź czy Config istnieje
		if (SettingsConfig.Instance == null)
		{
			GD.PrintErr("❌ BŁĄD KRYTYCZNY: SettingsConfig nie działa! Sprawdź Autoload.");
			return;
		}

		// 2. Przypisz węzły (Jeśli tu wywali błąd, to znaczy że ścieżki są złe)
		AssignNodes();
		
		// 3. Wypełnij listy rozwijane
		SetupDropdowns();

		// 4. Ustaw suwaki tak, jak w Configu
		UpdateUIFromConfig();

		// 5. Podłącz sygnały
		ConnectSignals();
		
		_isInitializing = false;
		GD.Print("✅ [SettingsUI] Menu gotowe.");
	}

	private void AssignNodes()
	{
		try
		{
			// UWAGA: Sprawdź te ścieżki w edytorze ("Remote" tab podczas gry)
			_backButton = GetNode<Button>("Control/BackButton");
			_saveButton = GetNode<Button>("SettingsPanel/SettingsCenter/VSettings/SaveButton");

			_masterVolumeSlider = GetNode<HSlider>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/MasterSlider");
			_musicVolumeSlider = GetNode<HSlider>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/MusicSlider");
			_sfxVolumeSlider = GetNode<HSlider>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/SFXSlider");
			_mutedCheckBox = GetNode<CheckButton>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/MuteCheckBox");

			_screenModeOptionButton = GetNode<OptionButton>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Video/ScreenOption");
			_resolutionOptionButton = GetNode<OptionButton>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Video/ResolutionOption");
			_scaleUISlider = GetNode<HSlider>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Video/SliderUI");
		}
		catch (Exception e)
		{
			GD.PrintErr("❌ BŁĄD PRZYPISANIA WĘZŁÓW W SETTINGS.CS: " + e.Message);
		}
	}

	private void SetupDropdowns()
	{
		if (_screenModeOptionButton != null)
		{
			_screenModeOptionButton.Clear();
			_screenModeOptionButton.AddItem("W oknie", 0);
			_screenModeOptionButton.AddItem("Pełny ekran", 1);
		}

		if (_resolutionOptionButton != null)
		{
			_resolutionOptionButton.Clear();
			var resList = SettingsConfig.Instance.AvailableResolutions;
			for (int i = 0; i < resList.Count; i++)
			{
				_resolutionOptionButton.AddItem($"{resList[i].X} x {resList[i].Y}", i);
			}
		}
		
		if (_scaleUISlider != null)
		{
			_scaleUISlider.MinValue = 0.5;
			_scaleUISlider.MaxValue = 1.5;
			_scaleUISlider.Step = 0.1;
		}
	}

	private void UpdateUIFromConfig()
	{
		var config = SettingsConfig.Instance;
		
		// Audio
		if (_masterVolumeSlider != null) _masterVolumeSlider.Value = config.Sound.MasterVolume;
		if (_musicVolumeSlider != null) _musicVolumeSlider.Value = config.Sound.MusicVolume;
		if (_sfxVolumeSlider != null) _sfxVolumeSlider.Value = config.Sound.SFXVolume;
		if (_mutedCheckBox != null) _mutedCheckBox.ButtonPressed = config.Sound.Muted;

		// Video
		if (_screenModeOptionButton != null) _screenModeOptionButton.Selected = config.Video.DisplayMode;
		if (_resolutionOptionButton != null) _resolutionOptionButton.Selected = config.Video.ResolutionIndex;
		if (_scaleUISlider != null) _scaleUISlider.Value = config.Video.UIScale;

		CheckResolutionLock();
	}

	private void ConnectSignals()
	{
		// Slidery Audio
		if (_masterVolumeSlider != null) _masterVolumeSlider.ValueChanged += (val) => { if(!_isInitializing) SettingsConfig.Instance.ChangeVolume("Master", (float)val); };
		if (_musicVolumeSlider != null) _musicVolumeSlider.ValueChanged += (val) => { if(!_isInitializing) SettingsConfig.Instance.ChangeVolume("Music", (float)val); };
		if (_sfxVolumeSlider != null) _sfxVolumeSlider.ValueChanged += (val) => { if(!_isInitializing) SettingsConfig.Instance.ChangeVolume("SFX", (float)val); };
		if (_mutedCheckBox != null) _mutedCheckBox.Toggled += (pressed) => { if(!_isInitializing) SettingsConfig.Instance.SetMute(pressed); };

		// Video
		if (_screenModeOptionButton != null) _screenModeOptionButton.ItemSelected += (idx) => 
		{ 
			if(!_isInitializing) 
			{
				SettingsConfig.Instance.SetWindowMode((int)idx);
				CheckResolutionLock();
			}
		};
		
		if (_resolutionOptionButton != null) _resolutionOptionButton.ItemSelected += (idx) => 
		{ 
			if(!_isInitializing) SettingsConfig.Instance.SetResolution((int)idx); 
		};
		
		if (_scaleUISlider != null) _scaleUISlider.ValueChanged += (val) => 
		{ 
			if(!_isInitializing) SettingsConfig.Instance.SetUIScale((float)val); 
		};

		// Przyciski
		if (_saveButton != null) _saveButton.Pressed += () => SettingsConfig.Instance.SaveSettings();
		if (_backButton != null) _backButton.Pressed += OnBackButtonPressed;
	}

	private void CheckResolutionLock()
	{
		if (_resolutionOptionButton == null) return;
		// Blokuj zmianę rozdzielczości jeśli jest fullscreen
		bool isFullscreen = SettingsConfig.Instance.Video.DisplayMode == 1;
		_resolutionOptionButton.Disabled = isFullscreen;
	}

	private void OnBackButtonPressed()
	{
		GD.Print("🔙 Powrót do menu...");
		GetTree().ChangeSceneToFile("res://Scenes/MainMenu/main.tscn");
	}
}
