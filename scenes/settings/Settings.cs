using Godot;

public partial class Settings : Control
{
	// --- UI ELEMENTS (Przypisz w Inspektorze!) ---
	[ExportGroup("Navigation")]
	[Export] private Button backButton;
	[Export] private Button saveButton;

	[ExportGroup("Audio")]
	[Export] private HSlider masterVolumeSlider;
	[Export] private HSlider musicVolumeSlider;
	[Export] private HSlider sfxVolumeSlider;
	[Export] private CheckButton mutedCheckBox;

	[ExportGroup("Video")]
	[Export] private OptionButton screenModeOptionButton; // Dropdown trybu okna
	[Export] private OptionButton resolutionOptionButton; // Dropdown rozdzielczości
	[Export] private HSlider scaleUISlider;

	public override void _Ready()
	{
		// 0. KONFIGURACJA SUWAKA SKALI (Naprawa crasha i powiększenie zakresu)
		if (scaleUISlider != null)
		{
			// Minimalna wartość 0.5 chroni przed crashem (nie można dzielić przez 0)
			scaleUISlider.MinValue = 0.5f; 
			// Maksymalna wartość 2.0 pozwala na 200% powiększenia (możesz dać więcej np. 2.5f)
			scaleUISlider.MaxValue = 2.0f; 
			// Krok suwaka - co 0.1 dla precyzji
			scaleUISlider.Step = 0.1f;
		}

		// 1. Wypełnij listy rozwijane danymi z Managera
		SetupVideoOptions();

		// 2. Zaktualizuj suwaki i przyciski, żeby pokazywały to, co jest w configu
		SyncUIWithManager();

		// 3. Podłącz zdarzenia (sygnały)
		ConnectSignals();
	}

	private void SetupVideoOptions()
	{
		// -- Tryb Okna --
		if (screenModeOptionButton != null)
		{
			screenModeOptionButton.Clear();
			// Kolejność musi zgadzać się z Enumem w SettingsManager: 0=Windowed, 1=Borderless, 2=Fullscreen
			screenModeOptionButton.AddItem("W oknie", 0);
			screenModeOptionButton.AddItem("Okno bez ramek", 1);
			screenModeOptionButton.AddItem("Pełny ekran", 2);
		}

		// -- Rozdzielczości --
		if (resolutionOptionButton != null)
		{
			resolutionOptionButton.Clear();
			// Pobieramy listę dostępnych rozdzielczości prosto z Managera
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
		// Navigation
		if (backButton != null) backButton.Pressed += OnBackButtonPressed;
		if (saveButton != null) saveButton.Pressed += OnSavePressed;

		// Audio (Natychmiastowa zmiana)
		if (masterVolumeSlider != null) masterVolumeSlider.ValueChanged += (v) => SettingsManager.Instance.SetMasterVolume((float)v);
		if (musicVolumeSlider != null)  musicVolumeSlider.ValueChanged  += (v) => SettingsManager.Instance.SetMusicVolume((float)v);
		if (sfxVolumeSlider != null)    sfxVolumeSlider.ValueChanged    += (v) => SettingsManager.Instance.SetSfxVolume((float)v);
		if (mutedCheckBox != null)      mutedCheckBox.Toggled           += (v) => SettingsManager.Instance.SetMuted(v);

		// Video
		if (screenModeOptionButton != null) screenModeOptionButton.ItemSelected += OnWindowModeSelected;
		if (resolutionOptionButton != null) resolutionOptionButton.ItemSelected += OnResolutionSelected;
		if (scaleUISlider != null)          scaleUISlider.ValueChanged          += OnUIScaleChanged;
	}

	// --- HANDLERY ZDARZEŃ ---

	private void OnWindowModeSelected(long index)
	{
		SettingsManager.Instance.SetDisplayMode((SettingsManager.WindowMode)index);
		CheckResolutionLock(); // Sprawdzamy blokadę po zmianie trybu
	}

	private void OnResolutionSelected(long index)
	{
		SettingsManager.Instance.SetResolutionByIndex((int)index);
	}

	private void OnUIScaleChanged(double value)
	{
		// Zabezpieczenie przed zerem (Mathf.Max), chociaż MinValue suwaka też to robi
		float safeValue = Mathf.Max((float)value, 0.1f);
		SettingsManager.Instance.SetUiScale(safeValue);
	}

	private void OnSavePressed()
	{
		SettingsManager.Instance.SaveConfig();
	}

	private void OnBackButtonPressed()
	{
		// Zapisujemy przy wyjściu dla pewności
		SettingsManager.Instance.SaveConfig();
		
		// Zmień ścieżkę na swoje Menu Główne!
		GetTree().ChangeSceneToFile("res://scenes/menu/main.tscn");
	}

	// Blokujemy zmianę rozdzielczości w trybie Fullscreen ORAZ Borderless
	private void CheckResolutionLock()
	{
		if (resolutionOptionButton == null) return;
		
		var mode = SettingsManager.Instance.Video.DisplayMode;

		// Blokujemy, jeśli jest Fullscreen LUB Borderless
		bool shouldLock = (mode == SettingsManager.WindowMode.Fullscreen) || 
						  (mode == SettingsManager.WindowMode.Borderless);

		resolutionOptionButton.Disabled = shouldLock;
	}
}
