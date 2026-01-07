using Godot;

public partial class Settings : Control
{
	// --- UI ELEMENTS (Przypisz w Inspektorze!) ---
	// Zmieniono nazwy: usunięto "_" i upewniono się, że zaczynają się z małej litery
	[ExportGroup("Nawigacja")]
	[Export] private Button backButton;
	[Export] private Button saveButton;

	[ExportGroup("Audio")]
	[Export] private HSlider masterVolumeSlider;
	[Export] private HSlider musicVolumeSlider;
	[Export] private HSlider sfxVolumeSlider;
	[Export] private CheckButton mutedCheckBox;

	[ExportGroup("Wideo")]
	[Export] private OptionButton screenModeOptionButton; // Dropdown trybu okna
	[Export] private OptionButton resolutionOptionButton; // Dropdown rozdzielczości
	[Export] private HSlider scaleUISlider;

	public override void _Ready()
	{
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

		// Wideo
		if (screenModeOptionButton != null)
		{
			// NAPRAWA CS0266: Rzutujemy Enum na int, żeby Dropdown to zrozumiał
			screenModeOptionButton.Selected = (int)sm.Video.DisplayMode;
		}

		if (resolutionOptionButton != null)
		{
			// NAPRAWA CS1061: Nie bierzemy Indexu z danych, tylko pytamy Managera, który to index
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
		// Nawigacja
		if (backButton != null) backButton.Pressed += OnBackButtonPressed;
		if (saveButton != null) saveButton.Pressed += OnSavePressed;

		// Audio (Natychmiastowa zmiana)
		if (masterVolumeSlider != null) masterVolumeSlider.ValueChanged += (v) => SettingsManager.Instance.SetMasterVolume((float)v);
		if (musicVolumeSlider != null)  musicVolumeSlider.ValueChanged  += (v) => SettingsManager.Instance.SetMusicVolume((float)v);
		if (sfxVolumeSlider != null)    sfxVolumeSlider.ValueChanged    += (v) => SettingsManager.Instance.SetSfxVolume((float)v);
		if (mutedCheckBox != null)      mutedCheckBox.Toggled           += (v) => SettingsManager.Instance.SetMuted(v);

		// Wideo
		if (screenModeOptionButton != null) screenModeOptionButton.ItemSelected += OnWindowModeSelected;
		if (resolutionOptionButton != null) resolutionOptionButton.ItemSelected += OnResolutionSelected;
		if (scaleUISlider != null)          scaleUISlider.ValueChanged          += OnUIScaleChanged;
	}

	// --- HANDLERY ZDARZEŃ ---

	private void OnWindowModeSelected(long index)
	{
		// NAPRAWA CS1503: Rzutujemy int (z dropdowna) na Enum (dla Managera)
		SettingsManager.Instance.SetDisplayMode((SettingsManager.WindowMode)index);
		
		CheckResolutionLock();
	}

	private void OnResolutionSelected(long index)
	{
		// NAPRAWA CS1061: Używamy nowej metody SetResolutionByIndex
		SettingsManager.Instance.SetResolutionByIndex((int)index);
	}

	private void OnUIScaleChanged(double value)
	{
		SettingsManager.Instance.SetUiScale((float)value);
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

	// Blokujemy zmianę rozdzielczości, jeśli jesteśmy w Fullscreen
	private void CheckResolutionLock()
	{
		if (resolutionOptionButton == null) return;
		
		bool isFullscreen = SettingsManager.Instance.Video.DisplayMode == SettingsManager.WindowMode.Fullscreen;
		resolutionOptionButton.Disabled = isFullscreen;
	}
}
