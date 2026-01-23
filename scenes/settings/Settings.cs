using Godot;

/// <summary>
/// Manages the in-game settings UI, including audio and video options.
/// </summary>
public partial class Settings : Control
{
	// --- UI ELEMENTS ---
	[ExportGroup("Navigation")]
	/// <summary>
	/// Button to return to the previous menu.
	/// </summary>
	[Export] private Button backButton;
	/// <summary>
	/// Button to save the current configuration.
	/// </summary>
	[Export] private Button saveButton;

	[ExportGroup("Audio")]
	/// <summary>
	/// Slider for controlling the master volume.
	/// </summary>
	[Export] private HSlider masterVolumeSlider;
	/// <summary>
	/// Slider for controlling the music volume.
	/// </summary>
	[Export] private HSlider musicVolumeSlider;
	/// <summary>
	/// Slider for controlling the sound effects volume.
	/// </summary>
	[Export] private HSlider sfxVolumeSlider;
	/// <summary>
	/// CheckBox to toggle mute status.
	/// </summary>
	[Export] private CheckButton mutedCheckBox;

	[ExportGroup("Video")]
	/// <summary>
	/// OptionButton for selecting the screen mode (Windowed, Borderless, Fullscreen).
	/// </summary>
	[Export] private OptionButton screenModeOptionButton;
	/// <summary>
	/// OptionButton for selecting the screen resolution.
	/// </summary>
	[Export] private OptionButton resolutionOptionButton;
	/// <summary>
	/// Slider for modifying the UI scale.
	/// </summary>
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

	/// <summary>
	/// Updates the UI scale slider value without triggering the signal.
	/// </summary>
	/// <param name="newScale">The new UI scale value.</param>
	private void UpdateSliderVisuals(float newScale)
	{
		// SetValueNoSignal zapobiega pętli nieskończonej
		scaleUISlider?.SetValueNoSignal(newScale);
	}

	/// <summary>
	/// Populates the video option buttons with available window modes and resolutions.
	/// </summary>
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

	/// <summary>
	/// Synchronizes the UI elements with the current values from the SettingsManager.
	/// </summary>
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

	/// <summary>
	/// Connects UI element signals to their respective event handlers.
	/// </summary>
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

	/// <summary>
	/// Handles the selection of a window mode.
	/// </summary>
	/// <param name="index">The index of the selected window mode.</param>
	private void OnWindowModeSelected(long index)
	{
		SettingsManager.Instance.SetDisplayMode((SettingsManager.WindowMode)index);
		CheckResolutionLock(); 
	}

	/// <summary>
	/// Handles the selection of a screen resolution.
	/// </summary>
	/// <param name="index">The index of the selected resolution.</param>
	private void OnResolutionSelected(long index)
	{
		SettingsManager.Instance.SetResolutionByIndex((int)index);
	}

	/// <summary>
	/// Handles changes to the UI scale slider.
	/// </summary>
	/// <param name="value">The new scale value.</param>
	private void OnUIScaleChanged(double value)
	{
		float safeValue = Mathf.Max((float)value, 0.1f);
		SettingsManager.Instance.SetUiScale(safeValue);
	}

	/// <summary>
	/// Saves the current configuration via the SettingsManager.
	/// </summary>
	private void OnSavePressed()
	{
		SettingsManager.Instance.SaveConfig();
	}

	/// <summary>
	/// Saves the configuration, hides the settings menu, and unpauses the game tree.
	/// </summary>
	private void OnBackButtonPressed()
	{
		SettingsManager.Instance.SaveConfig();
		this.Visible = false;
		GetTree().Paused = false; 
		
	}

	/// <summary>
	/// Disables the resolution option button if the window mode is Fullscreen or Borderless.
	/// </summary>
	private void CheckResolutionLock()
	{
		if (resolutionOptionButton == null) return;
		
		var mode = SettingsManager.Instance.Video.DisplayMode;

		bool shouldLock = (mode == SettingsManager.WindowMode.Fullscreen) || 
						  (mode == SettingsManager.WindowMode.Borderless);

		resolutionOptionButton.Disabled = shouldLock;
	}
}
