using Godot;
using System;

public partial class Settings : Control
{
	// --- UI ELEMENTS (Przypisz w Inspektorze!) ---
	[ExportGroup("Nawigacja")]
	[Export] private Button _backButton;
	[Export] private Button _saveButton;

	[ExportGroup("Audio")]
	[Export] private HSlider _masterVolumeSlider;
	[Export] private HSlider _musicVolumeSlider;
	[Export] private HSlider _sfxVolumeSlider;
	[Export] private CheckButton _mutedCheckBox;

	[ExportGroup("Wideo")]
	[Export] private OptionButton _screenModeOptionButton; // Dropdown trybu okna
	[Export] private OptionButton _resolutionOptionButton; // Dropdown rozdzielczości
	[Export] private HSlider _scaleUISlider;

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
		if (_screenModeOptionButton != null)
		{
			_screenModeOptionButton.Clear();
			// Kolejność musi zgadzać się z Enumem w SettingsManager: 0=Windowed, 1=Borderless, 2=Fullscreen
			_screenModeOptionButton.AddItem("W oknie", 0);
			_screenModeOptionButton.AddItem("Okno bez ramek", 1);
			_screenModeOptionButton.AddItem("Pełny ekran", 2);
		}

		// -- Rozdzielczości --
		if (_resolutionOptionButton != null)
		{
			_resolutionOptionButton.Clear();
			// Pobieramy listę dostępnych rozdzielczości prosto z Managera
			var resolutions = SettingsManager.Instance.AvailableResolutions;
			for (int i = 0; i < resolutions.Count; i++)
			{
				Vector2I res = resolutions[i];
				_resolutionOptionButton.AddItem($"{res.X} x {res.Y}", i);
			}
		}
	}

	private void SyncUIWithManager()
	{
		var sm = SettingsManager.Instance;
		if (sm == null) return;

		// Audio
		if (_masterVolumeSlider != null) _masterVolumeSlider.Value = sm.Sound.MasterVolume;
		if (_musicVolumeSlider != null)  _musicVolumeSlider.Value  = sm.Sound.MusicVolume;
		if (_sfxVolumeSlider != null)    _sfxVolumeSlider.Value    = sm.Sound.SfxVolume;
		if (_mutedCheckBox != null)      _mutedCheckBox.ButtonPressed = sm.Sound.Muted;

		// Wideo
		if (_screenModeOptionButton != null)
		{
			// NAPRAWA CS0266: Rzutujemy Enum na int, żeby Dropdown to zrozumiał
			_screenModeOptionButton.Selected = (int)sm.Video.DisplayMode;
		}

		if (_resolutionOptionButton != null)
		{
			// NAPRAWA CS1061: Nie bierzemy Indexu z danych, tylko pytamy Managera, który to index
			_resolutionOptionButton.Selected = sm.GetCurrentResolutionIndex();
		}

		if (_scaleUISlider != null)
		{
			_scaleUISlider.Value = sm.Video.UiScale;
		}

		CheckResolutionLock();
	}

	private void ConnectSignals()
	{
		// Nawigacja
		if (_backButton != null) _backButton.Pressed += OnBackButtonPressed;
		if (_saveButton != null) _saveButton.Pressed += OnSavePressed;

		// Audio (Natychmiastowa zmiana)
		if (_masterVolumeSlider != null) _masterVolumeSlider.ValueChanged += (v) => SettingsManager.Instance.SetMasterVolume((float)v);
		if (_musicVolumeSlider != null)  _musicVolumeSlider.ValueChanged  += (v) => SettingsManager.Instance.SetMusicVolume((float)v);
		if (_sfxVolumeSlider != null)    _sfxVolumeSlider.ValueChanged    += (v) => SettingsManager.Instance.SetSfxVolume((float)v);
		if (_mutedCheckBox != null)      _mutedCheckBox.Toggled           += (v) => SettingsManager.Instance.SetMuted(v);

		// Wideo
		if (_screenModeOptionButton != null) _screenModeOptionButton.ItemSelected += OnWindowModeSelected;
		if (_resolutionOptionButton != null) _resolutionOptionButton.ItemSelected += OnResolutionSelected;
		if (_scaleUISlider != null)          _scaleUISlider.ValueChanged          += OnUIScaleChanged;
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

	// Blokujemy zmianę rozdzielczości, jeśli jesteśmy w Fullscreen (opcjonalne, ale dobra praktyka)
	private void CheckResolutionLock()
	{
		if (_resolutionOptionButton == null) return;
		
		bool isFullscreen = SettingsManager.Instance.Video.DisplayMode == SettingsManager.WindowMode.Fullscreen;
		_resolutionOptionButton.Disabled = isFullscreen;
	}
}
