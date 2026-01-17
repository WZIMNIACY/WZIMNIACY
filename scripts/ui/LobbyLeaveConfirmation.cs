using Godot;

/// <summary>
/// Obsługuje wyświetlanie okna potwierdzenia przy opuszczaniu lobby
/// </summary>
public partial class LobbyLeaveConfirmation : Node
{
    /// <summary>
    /// Scena do której wracamy po opuszczeniu lobby
    /// </summary>
    [Export]
    public string ReturnScenePath { get; set; } = "res://scenes/menu/main.tscn";

    private EOSManager eosManager;
    private PopupSystem popupSystem;

    public override void _Ready()
    {
        base._Ready();
        eosManager = GetNode<EOSManager>("/root/EOSManager");

        // Załaduj popup system
        var popupScene = GD.Load<PackedScene>("res://scenes/popup/PopupSystem.tscn");
        if (popupScene != null)
        {
            popupSystem = popupScene.Instantiate<PopupSystem>();
            AddChild(popupSystem);
        }
    }

    /// <summary>
    /// Wyświetla dialog potwierdzenia z odpowiednim komunikatem
    /// </summary>
    public void ShowConfirmation()
    {
        if (popupSystem == null)
            return;

        bool isHost = eosManager != null && eosManager.isLobbyOwner;

        string message;
        if (isHost)
        {
            message = "Jesteś hostem lobby.\n\nOpuszczenie spowoduje przekazanie roli hosta innemu graczowi, jeśli to możliwe.\n\nCzy na pewno chcesz opuścić?";
        }
        else
        {
            message = "Czy na pewno chcesz opuścić lobby?";
        }

        popupSystem.ShowConfirmation(
            "★ OPUŚĆ LOBBY ★",
            message,
            "TAK, OPUŚĆ",
            "ANULUJ",
            OnConfirmLeave,
            OnCancelLeave
        );
    }

    /// <summary>
    /// Wywoływane gdy użytkownik potwierdził opuszczenie
    /// </summary>
    private void OnConfirmLeave()
    {
        GD.Print("🚪 User confirmed leaving lobby");

        if (eosManager != null && !string.IsNullOrEmpty(eosManager.currentLobbyId))
        {
            eosManager.LeaveLobby();
        }

        // Wróć do menu
        GetTree().ChangeSceneToFile(ReturnScenePath);
    }

    /// <summary>
    /// Wywoływane gdy użytkownik anulował opuszczenie
    /// </summary>
    private void OnCancelLeave()
    {
        GD.Print("❌ User canceled leaving lobby");
    }

    public override void _ExitTree()
    {
        base._ExitTree();

        if (popupSystem != null)
        {
            popupSystem.QueueFree();
        }
    }
}
