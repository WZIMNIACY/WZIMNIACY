using Godot;

public partial class Settings : Control
{
	// --- UI ELEMENTS ---
	[ExportGroup("Navigation")]
	[Export] private Button backButton;
	[Export] private Button saveButton;

	[ExportGroup("Audio")]
	[Export] private HSlider masterVolumeSlider;
	[Export] private HSlider musicVolumeSlider;
	[Export] private HSlider sfxVolumeSlider;
	[Export] private CheckButton mutedCheckBox;

	[ExportGroup("Video")]
	[Export] private OptionButton screenModeOptionButton;
	[Export] private OptionButton resolutionOptionButton;
	[Export] private HSlider scaleUISlider;

	public override void _Ready()
	{
		if (scaleUISlider != null)
		{
			scaleUISlider.MinValue = 0.5f; 
			scaleUISlider.MaxValue = 3.0f; 
			scaleUISlider.Step = 0.1f;
		}

		SetupVideoOptions();
		SyncUIWithManager();
		ConnectSignals();

		// Nasłuchiwanie na zmiany skali z Managera
		if (SettingsManager.Instance != null)
		{
			SettingsManager.Instance.OnUiScaleChanged += UpdateSliderVisuals;
		}
	}

	public override void _ExitTree()
	{
		// Sprzątanie po zdarzeniu
		if (SettingsManager.Instance != null)
		{
			SettingsManager.Instance.OnUiScaleChanged -= UpdateSliderVisuals;
		}
	}

	private void UpdateSliderVisuals(float newScale)
	{
		// SetValueNoSignal zapobiega pętli nieskończonej
		scaleUISlider?.SetValueNoSignal(newScale);
	}

	private void SetupVideoOptions()
	{
		if (screenModeOptionButton != null)
		{
			screenModeOptionButton.Clear();
			screenModeOptionButton.AddItem("W oknie", 0);
			screenModeOptionButton.AddItem("Okno bez ramek", 1);
			screenModeOptionButton.AddItem("Pełny ekran", 2);
		}

		if (resolutionOptionButton != null)
		{
			resolutionOptionButton.Clear();
			var resolutions = SettingsManager.Instance.availableResolutions;
			for (int i = 0; i < resolutions.Count; i++)
			{
				Vector2I res = resolutions[i];
				resolutionOptionButton.AddItem($"{res.X} x {res.Y}", i);
			}
		}
	}

	private void SyncUIWithManager()
	{
		var sm = SettingsManager.Instance;
		if (sm == null) return;

		// Audio
		if (masterVolumeSlider != null) masterVolumeSlider.Value = sm.Sound.MasterVolume;
		if (musicVolumeSlider != null)  musicVolumeSlider.Value  = sm.Sound.MusicVolume;
		if (sfxVolumeSlider != null)    sfxVolumeSlider.Value    = sm.Sound.SfxVolume;
		if (mutedCheckBox != null)      mutedCheckBox.ButtonPressed = sm.Sound.Muted;

		// Video
		if (screenModeOptionButton != null)
		{
			screenModeOptionButton.Selected = (int)sm.Video.DisplayMode;
		}

		if (resolutionOptionButton != null)
		{
			resolutionOptionButton.Selected = sm.GetCurrentResolutionIndex();
		}

		if (scaleUISlider != null)
		{
			scaleUISlider.Value = sm.Video.UiScale;
		}

		CheckResolutionLock();
	}

	private void ConnectSignals()
	{
		if (backButton != null) backButton.Pressed += OnBackButtonPressed;
		if (saveButton != null) saveButton.Pressed += OnSavePressed;

		if (masterVolumeSlider != null) masterVolumeSlider.ValueChanged += (v) => SettingsManager.Instance.SetMasterVolume((float)v);
		if (musicVolumeSlider != null)  musicVolumeSlider.ValueChanged  += (v) => SettingsManager.Instance.SetMusicVolume((float)v);
		if (sfxVolumeSlider != null)    sfxVolumeSlider.ValueChanged    += (v) => SettingsManager.Instance.SetSfxVolume((float)v);
		if (mutedCheckBox != null)      mutedCheckBox.Toggled           += (v) => SettingsManager.Instance.SetMuted(v);

		if (screenModeOptionButton != null) screenModeOptionButton.ItemSelected += OnWindowModeSelected;
		if (resolutionOptionButton != null) resolutionOptionButton.ItemSelected += OnResolutionSelected;
		if (scaleUISlider != null)          scaleUISlider.ValueChanged          += OnUIScaleChanged;
	}

	private void OnWindowModeSelected(long index)
	{
		SettingsManager.Instance.SetDisplayMode((SettingsManager.WindowMode)index);
		CheckResolutionLock(); 
	}

	private void OnResolutionSelected(long index)
	{
		SettingsManager.Instance.SetResolutionByIndex((int)index);
	}

	private void OnUIScaleChanged(double value)
	{
		float safeValue = Mathf.Max((float)value, 0.1f);
		SettingsManager.Instance.SetUiScale(safeValue);
	}

	private void OnSavePressed()
	{
		SettingsManager.Instance.SaveConfig();
	}

	private void OnBackButtonPressed()
	{
		SettingsManager.Instance.SaveConfig();
		string menuPath = "res://scenes/menu/main.tscn";
		if (ResourceLoader.Exists(menuPath))
		{
			GetTree().ChangeSceneToFile(menuPath);
		}
		else
		{
			GD.PrintErr($"❌ Nie znaleziono sceny menu: {menuPath}");
		}
	}

	private void CheckResolutionLock()
	{
		if (resolutionOptionButton == null) return;
		
		var mode = SettingsManager.Instance.Video.DisplayMode;

		bool shouldLock = (mode == SettingsManager.WindowMode.Fullscreen) || 
						  (mode == SettingsManager.WindowMode.Borderless);

		resolutionOptionButton.Disabled = shouldLock;
	}
}
