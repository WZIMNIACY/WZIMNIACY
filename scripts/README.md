# Scripts Structure

## Core Scripts (Global)

- **EOSManager.cs** - Główny manager Epic Online Services (autoload singleton)
- **EOStestPlatform.gd** - Testowy skrypt do wyszukiwania lobby przez EOS SDK

## UI Components (`UI/`)

- **UiManager.cs** - Manager UI dla przycisków lobby (tworzenie/dołączanie)
- **CurrentLobbyPanel.cs** - Panel wyświetlający aktualny stan lobby
- **LobbyListUI.cs** - Lista dostępnych lobby z możliwością dołączenia

## Scene-Specific Scripts

Skrypty specyficzne dla poszczególnych scen znajdują się w folderach `Scenes/`:

- `Scenes/MainMenu/MainMenu.cs` - Logika menu głównego
- `Scenes/Lobby/LobbyMenu.cs` - Logika lobby (tworzenie i zarządzanie)
- `Scenes/LobbySearch/LobbySearchMenu.cs` - Logika wyszukiwania lobby

## Project Structure

```
Scenes/
├── MainMenu/
│   ├── main.tscn          # Menu główne
│   └── MainMenu.cs        # Logika menu głównego
├── Lobby/
│   ├── Lobby.tscn         # Scena lobby
│   └── LobbyMenu.cs       # Logika lobby
└── LobbySearch/
    ├── LobbySearch.tscn   # Wyszukiwanie lobby
    └── LobbySearchMenu.cs # Logika wyszukiwania lobby

Scripts/
├── EOSManager.cs          # Manager EOS (autoload)
├── EOStestPlatform.gd     # Testy EOS
└── UI/
    ├── UiManager.cs       # Manager UI
    ├── CurrentLobbyPanel.cs
    └── LobbyListUI.cs
```

## Usage

Wszystkie ścieżki w plikach `.tscn` i `project.godot` zostały zaktualizowane do nowej struktury.
