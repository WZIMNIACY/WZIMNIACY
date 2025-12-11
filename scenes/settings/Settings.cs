using Godot;
using System;
using System.Collections.Generic;

public partial class Settings : Control
{
	// --- ELEMENTY UI (Teraz przypisywane w Inspektorze) ---
	[ExportGroup("Buttons")]
	[Export] private Button _backButton;
	[Export] private Button _saveButton;

	[ExportGroup("Audio Settings")]
	[Export] private HSlider _masterVolumeSlider;
	[Export] private HSlider _musicVolumeSlider;
	[Export] private HSlider _sfxVolumeSlider;
	[Export] private CheckButton _mutedCheckBox;

	[ExportGroup("Video Settings")]
	[Export] private OptionButton _resolutionOptionButton;
	[Export] private OptionButton _screenModeOptionButton;
	[Export] private HSlider _scaleUISlider;

	// Kopia listy rozdzielczości
	private readonly List<Vector2I> _availableResolutions = new List<Vector2I>();

	public override void _Ready()
	{
		GD.Print("⚙ Settings UI start");

		// 1. Sprawdzenie SettingsManagera
		if (SettingsManager.Instance == null)
		{
			GD.PrintErr("❌ Brak SettingsManager! Upewnij się, że dodałeś go w Globalne → Autładowanie.");
			return;
		}

		// 2. Sprawdzenie czy przypisałeś węzły w Inspektorze (Dla bezpieczeństwa)
		if (!CheckNodesAssigned())
		{
			GD.PrintErr("❌ Nie wszystkie węzły UI są przypisane w Inspektorze skryptu Settings.cs!");
			return;
		}

		// (AssignNodes zostało usunięte, bo robi to teraz Godot automatycznie)

		SetupResolutionsFromManager();
		SetupVideoOptions();
		UpdateUiFromManager();
		ConnectSignals();

		GD.Print("✅ Settings UI gotowe.");
	}

	// Metoda pomocnicza, żebyś wiedział, jeśli o czymś zapomniałeś w edytorze
	private bool CheckNodesAssigned()
	{
		bool allOk = true;
		if (_backButton == null) { GD.PrintErr("Brakuje: BackButton"); allOk = false; }
		if (_saveButton == null) { GD.PrintErr("Brakuje: SaveButton"); allOk = false; }
		if (_masterVolumeSlider == null) { GD.PrintErr("Brakuje: MasterSlider"); allOk = false; }
		if (_musicVolumeSlider == null) { GD.PrintErr("Brakuje: MusicSlider"); allOk = false; }
		if (_sfxVolumeSlider == null) { GD.PrintErr("Brakuje: SfxSlider"); allOk = false; }
		if (_mutedCheckBox == null) { GD.PrintErr("Brakuje: MutedCheckBox"); allOk = false; }
		if (_resolutionOptionButton == null) { GD.PrintErr("Brakuje: ResolutionOption"); allOk = false; }
		if (_screenModeOptionButton == null) { GD.PrintErr("Brakuje: ScreenModeOption"); allOk = false; }
		if (_scaleUISlider == null) { GD.PrintErr("Brakuje: ScaleUISlider"); allOk = false; }
		return allOk;
	}

	private void SetupResolutionsFromManager()
	{
		_availableResolutions.Clear();
		foreach (var res in SettingsManager.Instance.AvailableResolutions)
			_availableResolutions.Add(res);
	}

	private void SetupVideoOptions()
	{
		// Dzięki [Export] mamy pewność, że jeśli CheckNodesAssigned przeszło, to te elementy istnieją
		
		_screenModeOptionButton.Clear();
		_screenModeOptionButton.AddItem("W oknie", 0);
		_screenModeOptionButton.AddItem("Pełny ekran", 1);

		_resolutionOptionButton.Clear();
		
		// Pobieramy natywną rozdzielczość dla oznaczenia "(Twój ekran)"
		Vector2I nativeRes = DisplayServer.ScreenGetSize();

		for (int i = 0; i < _availableResolutions.Count; i++)
		{
			Vector2I res = _availableResolutions[i];
			string label = $"{res.X} x {res.Y}";
			
			if (res == nativeRes)
				label += " (Twój ekran)";

			_resolutionOptionButton.AddItem(label, i);
		}

		_scaleUISlider.MinValue = 0.5f;
		_scaleUISlider.MaxValue = 1.5f;
		_scaleUISlider.Step     = 0.1f;
	}

	private void UpdateUiFromManager()
	{
		var mgr = SettingsManager.Instance;

		// Audio
		_masterVolumeSlider.Value = mgr.Sound.MasterVolume;
		_musicVolumeSlider.Value  = mgr.Sound.MusicVolume;
		_sfxVolumeSlider.Value    = mgr.Sound.SfxVolume;
		_mutedCheckBox.ButtonPressed = mgr.Sound.Muted;

		// Video
		_screenModeOptionButton.Selected = mgr.Video.DisplayMode;
		_resolutionOptionButton.Selected = mgr.Video.ResolutionIndex;
		_scaleUISlider.Value             = mgr.Video.UiScale;

		UpdateResolutionLock();
	}

	private void ConnectSignals()
	{
		var mgr = SettingsManager.Instance;

		_backButton.Pressed += OnBackButtonPressed;
		_saveButton.Pressed += mgr.SaveConfig;

		_masterVolumeSlider.ValueChanged += val => mgr.SetMasterVolume((float)val);
		_musicVolumeSlider.ValueChanged  += val => mgr.SetMusicVolume((float)val);
		_sfxVolumeSlider.ValueChanged    += val => mgr.SetSfxVolume((float)val);
		_mutedCheckBox.Toggled           += pressed => mgr.SetMuted(pressed);

		_screenModeOptionButton.ItemSelected += index =>
		{
			mgr.SetDisplayMode((int)index);
			UpdateResolutionLock();
		};

		_resolutionOptionButton.ItemSelected += index =>
		{
			mgr.SetResolutionIndex((int)index);
		};

		_scaleUISlider.ValueChanged += value => mgr.SetUiScale((float)value);
	}

	private void UpdateResolutionLock()
	{
		// Blokujemy wybór rozdzielczości jeśli jest pełny ekran
		bool isWindowed = SettingsManager.Instance.Video.DisplayMode == 0;
		_resolutionOptionButton.Disabled = !isWindowed;
	}

	private void OnBackButtonPressed()
	{
		GD.Print("🔙 Powrót do menu...");
		GetTree().ChangeSceneToFile("res://Scenes/Menu/main.tscn");
	}
}
