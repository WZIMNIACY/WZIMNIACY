using Godot;
using System;
using System.Collections.Generic;

public partial class Settings : Control
{
	// --- PRZYCISKI ---
	private Button _backButton;
	private Button _saveButton;

	// --- AUDIO UI ---
	private HSlider _masterVolumeSlider;
	private HSlider _musicVolumeSlider;
	private HSlider _sfxVolumeSlider;
	private CheckButton _mutedCheckBox;

	// --- VIDEO UI ---
	private OptionButton _resolutionOptionButton;
	private OptionButton _screenModeOptionButton;
	private HSlider _scaleUISlider;

	// kopia listy rozdzielczości (z managera)
	private readonly List<Vector2I> _availableResolutions = new List<Vector2I>();

	public override void _Ready()
	{
		GD.Print("⚙ Settings UI start");

		if (SettingsManager.Instance == null)
		{
			GD.PrintErr("❌ Brak SettingsManager! Upewnij się, że dodałeś go w Globalne → Autładowanie.");
			return;
		}

		AssignNodes();
		SetupResolutionsFromManager();
		SetupVideoOptions();
		UpdateUiFromManager();
		ConnectSignals();

		GD.Print("✅ Settings UI gotowe.");
	}

	private void AssignNodes()
	{
		try
		{
			_backButton = GetNode<Button>("Control/BackButton");
			_saveButton = GetNode<Button>("SettingsPanel/SettingsCenter/VSettings/SaveButton");

			_masterVolumeSlider = GetNode<HSlider>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/MasterSlider");
			_musicVolumeSlider  = GetNode<HSlider>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/MusicSlider");
			_sfxVolumeSlider    = GetNode<HSlider>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/SFXSlider");
			_mutedCheckBox      = GetNode<CheckButton>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/MuteCheckBox");

			_screenModeOptionButton = GetNode<OptionButton>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Video/ScreenOption");
			_resolutionOptionButton = GetNode<OptionButton>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Video/ResolutionOption");
			_scaleUISlider          = GetNode<HSlider>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Video/SliderUI");
		}
		catch (Exception e)
		{
			GD.PrintErr("❌ Błąd AssignNodes w Settings.cs: " + e.Message);
		}
	}

	private void SetupResolutionsFromManager()
	{
		_availableResolutions.Clear();
		foreach (var res in SettingsManager.Instance.AvailableResolutions)
			_availableResolutions.Add(res);
	}

	private void SetupVideoOptions()
	{
		if (_screenModeOptionButton == null || _resolutionOptionButton == null)
			return;

		_screenModeOptionButton.Clear();
		_screenModeOptionButton.AddItem("W oknie", 0);
		_screenModeOptionButton.AddItem("Pełny ekran", 1);

		_resolutionOptionButton.Clear();
		for (int i = 0; i < _availableResolutions.Count; i++)
		{
			Vector2I res = _availableResolutions[i];
			_resolutionOptionButton.AddItem($"{res.X} x {res.Y}", i);
		}

		if (_scaleUISlider != null)
		{
			_scaleUISlider.MinValue = 0.5f;
			_scaleUISlider.MaxValue = 1.5f;
			_scaleUISlider.Step     = 0.1f;
		}
	}

	private void UpdateUiFromManager()
	{
		var mgr = SettingsManager.Instance;

		// Audio
		if (_masterVolumeSlider != null) _masterVolumeSlider.Value   = mgr.Sound.MasterVolume;
		if (_musicVolumeSlider  != null) _musicVolumeSlider.Value    = mgr.Sound.MusicVolume;
		if (_sfxVolumeSlider    != null) _sfxVolumeSlider.Value      = mgr.Sound.SfxVolume;
		if (_mutedCheckBox      != null) _mutedCheckBox.ButtonPressed = mgr.Sound.Muted;

		// Video
		if (_screenModeOptionButton != null) _screenModeOptionButton.Selected = mgr.Video.DisplayMode;
		if (_resolutionOptionButton != null) _resolutionOptionButton.Selected = mgr.Video.ResolutionIndex;
		if (_scaleUISlider          != null) _scaleUISlider.Value            = mgr.Video.UiScale;

		UpdateResolutionLock();
	}

	private void ConnectSignals()
	{
		var mgr = SettingsManager.Instance;

		if (_backButton != null)
			_backButton.Pressed += OnBackButtonPressed;

		if (_saveButton != null)
			_saveButton.Pressed += mgr.SaveConfig;

		if (_masterVolumeSlider != null)
			_masterVolumeSlider.ValueChanged += val => mgr.SetMasterVolume((float)val);

		if (_musicVolumeSlider != null)
			_musicVolumeSlider.ValueChanged += val => mgr.SetMusicVolume((float)val);

		if (_sfxVolumeSlider != null)
			_sfxVolumeSlider.ValueChanged += val => mgr.SetSfxVolume((float)val);

		if (_mutedCheckBox != null)
			_mutedCheckBox.Toggled += pressed => mgr.SetMuted(pressed);

		if (_screenModeOptionButton != null)
			_screenModeOptionButton.ItemSelected += index =>
			{
				mgr.SetDisplayMode((int)index);
				UpdateResolutionLock();
			};

		if (_resolutionOptionButton != null)
			_resolutionOptionButton.ItemSelected += index =>
			{
				mgr.SetResolutionIndex((int)index);
			};

		if (_scaleUISlider != null)
			_scaleUISlider.ValueChanged += value => mgr.SetUiScale((float)value);
	}

	private void UpdateResolutionLock()
	{
		if (_resolutionOptionButton == null)
			return;

		bool isWindowed = SettingsManager.Instance.Video.DisplayMode == 0;
		_resolutionOptionButton.Disabled = !isWindowed;
	}

	private void OnBackButtonPressed()
	{
		GD.Print("🔙 Powrót do menu...");
		GetTree().ChangeSceneToFile("res://Scenes/Menu/main.tscn");
	}
}
