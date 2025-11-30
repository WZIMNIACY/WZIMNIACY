using System.Collections.Generic;
using Godot;

public partial class Settings : Control
{
    private Button backButton;
    private Button saveButton;

    private const string SAVE_PATH = "user://settings.cfg";
    private HSlider masterVolumeSlider;
    private HSlider musicVolumeSlider;
    private HSlider sfxVolumeSlider;
    private CheckButton mutedCheckBox;
    private OptionButton resolutionOptionButton;
    private OptionButton screenModeOptionButton;
    private HSlider scaleUISlider;

    private List<Vector2I> availableResolutions = new List<Vector2I>
    {
        new Vector2I(3840, 2160),
        new Vector2I(3440, 1440),
        new Vector2I(2560, 1440),
        new Vector2I(1920, 1200),
        new Vector2I(1920, 1080),
        new Vector2I(1600, 900),
        new Vector2I(1366, 768),
        new Vector2I(1280, 720),
        new Vector2I(1024, 576),
        new Vector2I(800, 600)
    };

    private class SoundSettings
    {
        public float MasterVolume { get; set; } = 1.0f;
        public float MusicVolume { get; set; } = 1.0f;
        public float SFXVolume { get; set; } = 1.0f;
        public bool Muted { get; set; } = false;
    }

    private class VideoSettings
    {
        public int DisplayMode { get; set; } = 0;
        public int ResolutionIndex { get; set; } = 0;
        public float UIScale { get; set; } = 1.0f;
        public bool VSync { get; set; } = true;
    }

    private class ConfigData
    {
        public SoundSettings Sound { get; set; } = new SoundSettings();

        public VideoSettings Video { get; set; } = new VideoSettings();
    }

    private ConfigData configData = new ConfigData();


    public override void _Ready()
    {
        base._Ready();

        GD.Print("⚙️ Settings scene initializing...");

        backButton = GetNode<Button>("Control/BackButton");
        saveButton = GetNode<Button>("SettingsPanel/SettingsCenter/VSettings/SaveButton");

        masterVolumeSlider = GetNode<HSlider>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/MasterSlider");
        musicVolumeSlider = GetNode<HSlider>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/MusicSlider");
        sfxVolumeSlider = GetNode<HSlider>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/SFXSlider");
        mutedCheckBox = GetNode<CheckButton>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Dźwięk/MuteCheckBox");

        screenModeOptionButton = GetNode<OptionButton>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Video/ScreenOption");
        resolutionOptionButton = GetNode<OptionButton>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Video/ResolutionOption");
        scaleUISlider = GetNode<HSlider>("SettingsPanel/SettingsCenter/VSettings/TabContainer/Video/SliderUI");

        VideoUIOptions();
        LoadSettings();
        UpdateUI();
        GD.Print("✅ Settings ready");

        if (backButton != null)
        {
            backButton.Pressed += OnBackButtonPressed;
        }
        if (saveButton != null)
        {
            saveButton.Pressed += SaveSettings;
        }
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.ValueChanged += OnMasterVolumeSliderChanged;
        }
        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.ValueChanged += OnMusicVolumeSliderChanged;
        }
        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.ValueChanged += OnSFXVolumeSliderChanged;
        }
        if (mutedCheckBox != null)
        {
            mutedCheckBox.Toggled += OnMutedCheckBoxToggled;
        }
        if (screenModeOptionButton != null)
        {
            screenModeOptionButton.ItemSelected += OnWindowModeSelected;
        }
        if (resolutionOptionButton != null)
        {
            resolutionOptionButton.ItemSelected += OnResolutionSelected;
        }
        if (scaleUISlider != null)
        {
            scaleUISlider.ValueChanged += OnUIScaleSliderChanged;
        }
    }

    private void OnBackButtonPressed()
    {
        GD.Print("🔙 Returning to Main Menu");
        GetTree().ChangeSceneToFile("res://scenes/menu/main.tscn");
    }

    private void VideoUIOptions()
    {
        screenModeOptionButton.Clear();
        screenModeOptionButton.AddItem("Windowed", 0);
        screenModeOptionButton.AddItem("Fullscreen", 1);

        resolutionOptionButton.Clear();
        for (int i = 0; i < availableResolutions.Count; i++)
        {
            Vector2I res = availableResolutions[i];
            resolutionOptionButton.AddItem($"{res.X} x {res.Y}", i);
        }

        scaleUISlider.MinValue = 0.5f;
        scaleUISlider.MaxValue = 1.5f;
        scaleUISlider.Step = 0.1f;
    }

    private void LoadSettings()
    {
        ConfigFile configFile = new ConfigFile();
        Error err = configFile.Load(SAVE_PATH);
        if (err != Error.Ok)
        {
            GD.Print("⚠️ No settings file, using defaults");
            ApplyAudioSettings();
            ApplyVideoSettings();
            return;
        }

        configData.Sound.MasterVolume = (float)configFile.GetValue("Sound", "MasterVolume", 1.0f);
        configData.Sound.MusicVolume = (float)configFile.GetValue("Sound", "MusicVolume", 1.0f);
        configData.Sound.SFXVolume = (float)configFile.GetValue("Sound", "SFXVolume", 1.0f);
        configData.Sound.Muted = (bool)configFile.GetValue("Sound", "Muted", false);

        configData.Video.DisplayMode = (int)configFile.GetValue("Video", "DisplayMode", 0);
        configData.Video.ResolutionIndex = (int)configFile.GetValue("Video", "ResolutionIndex", 0);
        configData.Video.UIScale = (float)configFile.GetValue("Video", "UIScale", 1.0f);
        configData.Video.VSync = (bool)configFile.GetValue("Video", "VSync", false);

        GD.Print("📂 Settings loaded");
    }

    private void SaveSettings()
    {
        ConfigFile configFile = new ConfigFile();

        configFile.SetValue("Sound", "MasterVolume", configData.Sound.MasterVolume);
        configFile.SetValue("Sound", "MusicVolume", configData.Sound.MusicVolume);
        configFile.SetValue("Sound", "SFXVolume", configData.Sound.SFXVolume);
        configFile.SetValue("Sound", "Muted", configData.Sound.Muted);

        configFile.SetValue("Video", "DisplayMode", configData.Video.DisplayMode);
        configFile.SetValue("Video", "ResolutionIndex", configData.Video.ResolutionIndex);
        configFile.SetValue("Video", "UIScale", configData.Video.UIScale);
        configFile.SetValue("Video", "VSync", configData.Video.VSync);

        configFile.Save(SAVE_PATH);
        GD.Print("💾 Settings saved");
    }

    private void UpdateUI()
    {
        masterVolumeSlider.Value = configData.Sound.MasterVolume;
        musicVolumeSlider.Value = configData.Sound.MusicVolume;
        sfxVolumeSlider.Value = configData.Sound.SFXVolume;
        mutedCheckBox.ButtonPressed = configData.Sound.Muted;

        screenModeOptionButton.Selected = configData.Video.DisplayMode;
        resolutionOptionButton.Selected = configData.Video.ResolutionIndex;
        scaleUISlider.Value = configData.Video.UIScale;

        CheckResolutionLock();
    }

    private void OnMasterVolumeSliderChanged(double value)
    {
        configData.Sound.MasterVolume = (float)value;
        SetBusVolume("Master", (float)value);
    }

    private void OnMusicVolumeSliderChanged(double value)
    {
        configData.Sound.MusicVolume = (float)value;
        SetBusVolume("Music", (float)value);
    }

    private void OnSFXVolumeSliderChanged(double value)
    {
        configData.Sound.SFXVolume = (float)value;
        SetBusVolume("SFX", (float)value);
    }

    private void OnMutedCheckBoxToggled(bool pressed)
    {
        configData.Sound.Muted = pressed;
        int masterIdx = AudioServer.GetBusIndex("Master");
        AudioServer.SetBusMute(masterIdx, pressed);
    }

    private void OnWindowModeSelected(long index)
    {
        configData.Video.DisplayMode = (int)index;

        switch (index)
        {
            case 0:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);

                int resIdx = configData.Video.ResolutionIndex;
                if (resIdx < 0 || resIdx >= availableResolutions.Count)
                    resIdx = 0;

                Vector2I size = availableResolutions[resIdx];
                DisplayServer.WindowSetSize(size);
                CenterWindow();
                resolutionOptionButton.Disabled = false;
                GD.Print($"🪟 Windowed mode: {size.X}x{size.Y}");
                break;

            case 1:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
                resolutionOptionButton.Disabled = true;
                GD.Print("🖥️ Fullscreen mode");
                break;
        }
    }

    private void OnResolutionSelected(long index)
    {
        if (index >= 0 && index < availableResolutions.Count)
        {
            configData.Video.ResolutionIndex = (int)index;
            Vector2I resolution = availableResolutions[(int)index];

            if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Windowed)
            {
                DisplayServer.WindowSetSize(resolution);
                CenterWindow();
                GD.Print($"📐 Resolution: {resolution.X}x{resolution.Y}");
            }
        }
    }

    private void OnUIScaleSliderChanged(double value)
    {
        configData.Video.UIScale = (float)value;
        GetTree().Root.ContentScaleFactor = (float)value;
    }

    private void ApplyAudioSettings()
    {
        SetBusVolume("Master", configData.Sound.MasterVolume);
        SetBusVolume("Music", configData.Sound.MusicVolume);
        SetBusVolume("SFX", configData.Sound.SFXVolume);
        int masterIdx = AudioServer.GetBusIndex("Master");
        AudioServer.SetBusMute(masterIdx, configData.Sound.Muted);
    }

    private void SetBusVolume(string busName, float linearVal)
    {
        int busIdx = AudioServer.GetBusIndex(busName);

        // Sprawdzamy czy szyna istnieje
        if (busIdx == -1)
        {
            GD.PrintErr($"Szyna audio '{busName}' nie istnieje! Utwórz ją w Audio Bus Layout.");
            return;
        }

        float dbVal = linearVal > 0 ? Mathf.LinearToDb(linearVal) : -80.0f;
        AudioServer.SetBusVolumeDb(busIdx, dbVal);
    }

    private void ApplyVideoSettings()
    {
        // VSync
        if (configData.Video.VSync)
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
        }
        else
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        }

        // Tryb okna
        switch (configData.Video.DisplayMode)
        {
            case 0:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                break;
            case 1:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
                break;
        }

        // Rozdzielczość i centrowanie (tylko dla okna)
        if (configData.Video.DisplayMode == 0)
        {
            int resIdx = configData.Video.ResolutionIndex;
            if (resIdx >= 0 && resIdx < availableResolutions.Count)
            {
                DisplayServer.WindowSetSize(availableResolutions[resIdx]);
                CenterWindow();
            }
        }

        // Skala UI
        GetTree().Root.ContentScaleFactor = (float)configData.Video.UIScale;
    }

    private void CenterWindow()
    {
        int screenId = DisplayServer.WindowGetCurrentScreen();
        Vector2I screenSize = DisplayServer.ScreenGetSize(screenId);
        Vector2I windowSize = DisplayServer.WindowGetSize();
        Vector2I origin = DisplayServer.ScreenGetPosition(screenId);
        Vector2I centerPos = origin + (screenSize / 2) - (windowSize / 2);
        DisplayServer.WindowSetPosition(centerPos);
    }

    private void CheckResolutionLock()
    {
        if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed)
        {
            resolutionOptionButton.Disabled = true;
        }
        else
        {
            resolutionOptionButton.Disabled = false;
        }
    }
}