using Godot;
using System;
using System.Collections.Generic;

public partial class Settings : Control
{
	// --- ELEMENTY UI (Exportowane do Inspektora) ---
	[ExportGroup("Buttons")]
	[Export] private Button backButton;
	[Export] private Button saveButton;

	[ExportGroup("Audio Settings")]
	[Export] private HSlider masterVolumeSlider;
	[Export] private HSlider musicVolumeSlider;
	[Export] private HSlider sfxVolumeSlider;
	[Export] private CheckButton mutedCheckBox;

	[ExportGroup("Video Settings")]
	[Export] private OptionButton resolutionOptionButton;
	[Export] private OptionButton screenModeOptionButton;
	[Export] private HSlider scaleUISlider;

	// Kopia listy rozdzielczości
	private readonly List<Vector2I> availableResolutions = new List<Vector2I>();

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
		if (backButton == null) { GD.PrintErr("Brakuje: BackButton"); allOk = false; }
		if (saveButton == null) { GD.PrintErr("Brakuje: SaveButton"); allOk = false; }
		if (masterVolumeSlider == null) { GD.PrintErr("Brakuje: MasterSlider"); allOk = false; }
		if (musicVolumeSlider == null) { GD.PrintErr("Brakuje: MusicSlider"); allOk = false; }
		if (sfxVolumeSlider == null) { GD.PrintErr("Brakuje: SfxSlider"); allOk = false; }
		if (mutedCheckBox == null) { GD.PrintErr("Brakuje: MutedCheckBox"); allOk = false; }
		if (resolutionOptionButton == null) { GD.PrintErr("Brakuje: ResolutionOption"); allOk = false; }
		if (screenModeOptionButton == null) { GD.PrintErr("Brakuje: ScreenModeOption"); allOk = false; }
		if (scaleUISlider == null) { GD.PrintErr("Brakuje: ScaleUISlider"); allOk = false; }
		return allOk;
	}

	private void SetupResolutionsFromManager()
	{
		availableResolutions.Clear();
		foreach (var res in SettingsManager.Instance.AvailableResolutions)
			availableResolutions.Add(res);
	}

	private void SetupVideoOptions()
	{
		// Dzięki [Export] mamy pewność, że jeśli CheckNodesAssigned przeszło, to te elementy istnieją
		
		screenModeOptionButton.Clear();
		screenModeOptionButton.AddItem("W oknie", 0);
		screenModeOptionButton.AddItem("Pełny ekran", 1);

		resolutionOptionButton.Clear();
		
		// Pobieramy natywną rozdzielczość dla oznaczenia "(Twój ekran)"
		Vector2I nativeRes = DisplayServer.ScreenGetSize();

		for (int i = 0; i < availableResolutions.Count; i++)
		{
			Vector2I res = availableResolutions[i];
			string label = $"{res.X} x {res.Y}";
			
			if (res == nativeRes)
				label += " (Twój ekran)";

			resolutionOptionButton.AddItem(label, i);
		}

		scaleUISlider.MinValue = 0.5f;
		scaleUISlider.MaxValue = 1.5f;
		scaleUISlider.Step     = 0.1f;
	}

	private void UpdateUiFromManager()
	{
		var mgr = SettingsManager.Instance;

		// Audio
		masterVolumeSlider.Value = mgr.Sound.MasterVolume;
		musicVolumeSlider.Value  = mgr.Sound.MusicVolume;
		sfxVolumeSlider.Value    = mgr.Sound.SfxVolume;
		mutedCheckBox.ButtonPressed = mgr.Sound.Muted;

		// Video
		screenModeOptionButton.Selected = mgr.Video.DisplayMode;
		resolutionOptionButton.Selected = mgr.Video.ResolutionIndex;
		scaleUISlider.Value             = mgr.Video.UiScale;

		UpdateResolutionLock();
	}

	private void ConnectSignals()
	{
		var mgr = SettingsManager.Instance;

		backButton.Pressed += OnBackButtonPressed;
		saveButton.Pressed += mgr.SaveConfig;

		masterVolumeSlider.ValueChanged += val => mgr.SetMasterVolume((float)val);
		musicVolumeSlider.ValueChanged  += val => mgr.SetMusicVolume((float)val);
		sfxVolumeSlider.ValueChanged    += val => mgr.SetSfxVolume((float)val);
		mutedCheckBox.Toggled           += pressed => mgr.SetMuted(pressed);

		screenModeOptionButton.ItemSelected += index =>
		{
			mgr.SetDisplayMode((int)index);
			UpdateResolutionLock();
		};

		resolutionOptionButton.ItemSelected += index =>
		{
			mgr.SetResolutionIndex((int)index);
		};

		scaleUISlider.ValueChanged += value => mgr.SetUiScale((float)value);
	}

	private void UpdateResolutionLock()
	{
		// Blokujemy wybór rozdzielczości jeśli jest pełny ekran
		bool isWindowed = SettingsManager.Instance.Video.DisplayMode == 0;
		resolutionOptionButton.Disabled = !isWindowed;
	}

	private void OnBackButtonPressed()
	{
		GD.Print("🔙 Powrót do menu...");
		GetTree().ChangeSceneToFile("res://Scenes/Menu/main.tscn");
	}
}
