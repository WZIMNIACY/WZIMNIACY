using Godot;

/// <summary>
/// Obsługuje wyświetlanie okna potwierdzenia przy opuszczaniu lobby i wywołuje <see cref="EOSManager.LeaveLobby"/> przed zmianą sceny.
/// </summary>
/// <remarks>
/// Zakłada obecność autoloadu <see cref="EOSManager"/>. Dialog tworzony dynamicznie w wątku głównym; klasa nie jest thread-safe.
/// </remarks>
public partial class LobbyLeaveConfirmation : Node
{
    /// <summary>
    /// Scena do której wracamy po opuszczeniu lobby
    /// </summary>
    /// <value>Ścieżka pliku sceny głównego menu (domyślnie main.tscn).</value>
    [Export]
    public string ReturnScenePath { get; set; } = "res://scenes/menu/main.tscn";

    /// <summary>Autoload EOS do zarządzania lobby.</summary>
    private EOSManager eosManager;
    private PopupSystem popupSystem;

    /// <summary>
    /// Inicjalizuje referencje i tworzy dialog potwierdzenia po załadowaniu węzła.
    /// </summary>
    /// <seealso cref="CreateConfirmDialog"/>
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
    /// <seealso cref="OnConfirmLeave"/>
    /// <seealso cref="OnCancelLeave"/>
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
    /// Obsługuje potwierdzenie opuszczenia lobby: wylogowuje z lobby i przełącza scenę na ekran główny.
    /// </summary>
    /// <seealso cref="EOSManager.LeaveLobby"/>
    /// <seealso cref="ShowConfirmation"/>
    private void OnConfirmLeave()
    {
        GD.Print("[LobbyLeaveConfirmation] User confirmed leaving lobby");

        if (eosManager != null && !string.IsNullOrEmpty(eosManager.currentLobbyId))
        {
            eosManager.LeaveLobby();
        }

        // Wróć do menu
        GetTree().ChangeSceneToFile(ReturnScenePath);
    }

    /// <summary>
    /// Reaguje na anulowanie opuszczenia lobby i pozostawia użytkownika w bieżącej scenie.
    /// </summary>
    /// <seealso cref="ShowConfirmation"/>
    private void OnCancelLeave()
    {
        GD.Print("[LobbyLeaveConfirmation] User canceled leaving lobby");
    }

    /// <summary>
    /// Czyści subskrypcje i zwalnia dialog przy usuwaniu węzła z drzewa.
    /// </summary>
    public override void _ExitTree()
    {
        base._ExitTree();

        if (popupSystem != null)
        {
            popupSystem.QueueFree();
        }
    }
}
