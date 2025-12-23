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
    private AcceptDialog confirmDialog;

    public override void _Ready()
    {
        base._Ready();
        eosManager = GetNode<EOSManager>("/root/EOSManager");
        CreateConfirmDialog();
    }

    /// <summary>
    /// Tworzy dialog potwierdzenia
    /// </summary>
    private void CreateConfirmDialog()
    {
        confirmDialog = new AcceptDialog();
        confirmDialog.Title = "Opuść Lobby";
        confirmDialog.OkButtonText = "Tak, opuść";

        confirmDialog.AddCancelButton("Anuluj");

        confirmDialog.Confirmed += OnConfirmLeave;
        confirmDialog.Canceled += OnCancelLeave;

        AddChild(confirmDialog);
    }

    /// <summary>
    /// Wyświetla dialog potwierdzenia z odpowiednim komunikatem
    /// </summary>
    public void ShowConfirmation()
    {
        if (confirmDialog == null)
            return;

        bool isHost = eosManager != null && eosManager.isLobbyOwner;

        if (isHost)
        {
            confirmDialog.DialogText = "Jesteś hostem lobby.\nOpuszczenie spowoduje zamknięcie lobby dla wszystkich graczy.\n\nCzy na pewno chcesz opuścić?";
        }
        else
        {
            confirmDialog.DialogText = "Czy na pewno chcesz opuścić lobby?";
        }

        confirmDialog.PopupCentered();
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

        if (confirmDialog != null)
        {
            confirmDialog.Confirmed -= OnConfirmLeave;
            confirmDialog.Canceled -= OnCancelLeave;
            confirmDialog.QueueFree();
        }
    }
}
