using Godot;

/// <summary>
/// Obsługuje cofanie się do poprzedniej sceny za pomocą klawisza ESC.
/// Nie działa gdy jakiś input jest aktywny (LineEdit, TextEdit, itp.)
/// </summary>
public partial class EscapeBackHandler : Node
{
    /// <summary>
    /// Ścieżka do sceny, do której ma wrócić po naciśnięciu ESC
    /// </summary>
    [Export]
    public string PreviousScenePath { get; set; } = "res://scenes/menu/main.tscn";

    /// <summary>
    /// Czy ma opuścić lobby przed zmianą sceny (jeśli jesteśmy w lobby)
    /// </summary>
    [Export]
    public bool LeaveLobbyBeforeExit { get; set; } = false;

    /// <summary>
    /// Czy ma pokazać dialog potwierdzenia przed opuszczeniem (dla lobby)
    /// </summary>
    [Export]
    public bool ShowConfirmDialog { get; set; } = false;

    private EOSManager eosManager;

    /// <summary>
    /// Referencja do dialogu potwierdzenia
    /// </summary>
    public LobbyLeaveConfirmation LeaveConfirmation { get; set; }

    public override void _Ready()
    {
        base._Ready();

        eosManager = GetNode<EOSManager>("/root/EOSManager");
    }

    public override void _Input(InputEvent @event)
    {
        base._Input(@event);

        var viewport = GetViewport();
        if (viewport == null)
            return;

        // Obsługa kliknięcia myszy - zabierz focus z inputów
        if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
        {
            var focusOwner = viewport.GuiGetFocusOwner();
            if (focusOwner != null && (focusOwner is LineEdit || focusOwner is TextEdit || focusOwner is CodeEdit || focusOwner is SpinBox))
            {
                // Sprawdź czy kliknięcie było poza elementem z focusem
                if (focusOwner is Control control)
                {
                    var globalMousePos = viewport.GetMousePosition();
                    var controlRect = control.GetGlobalRect();

                    if (!controlRect.HasPoint(globalMousePos))
                    {
                        control.ReleaseFocus();
                    }
                }
            }
        }

        // Sprawdź czy naciśnięto ESC
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (keyEvent.Keycode == Key.Escape)
            {
                // Jeśli trwa dołączanie do lobby, zablokuj ESC
                if (eosManager != null && eosManager.isJoiningLobby)
                {
                    GD.Print("⏳ ESC zablokowany - trwa dołączanie do lobby...");
                    viewport.SetInputAsHandled();
                    return;
                }

                if (!IsAnyInputActive())
                {
                    HandleEscapeBack();
                    viewport.SetInputAsHandled();
                }
                else
                {
                    // Jeśli input ma focus, ESC najpierw zabiera focus
                    var focusOwner = viewport.GuiGetFocusOwner();
                    if (focusOwner is Control control)
                    {
                        control.ReleaseFocus();
                        viewport.SetInputAsHandled();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Sprawdza czy jakikolwiek element input jest obecnie aktywny (ma focus)
    /// </summary>
    private bool IsAnyInputActive()
    {
        var viewport = GetViewport();
        if (viewport == null)
            return false;

        var focusOwner = viewport.GuiGetFocusOwner();


        if (focusOwner == null)
            return false;

        bool isActive = focusOwner is LineEdit ||
                        focusOwner is TextEdit ||
                        focusOwner is CodeEdit ||
                        focusOwner is SpinBox;


        return isActive;
    }

    /// <summary>
    /// Obsługuje cofnięcie się do poprzedniej sceny
    /// </summary>
    private void HandleEscapeBack()
    {
        // Pokaż dialog (dialog sam obsłuży opuszczenie lobby i zmianę sceny)
        if (ShowConfirmDialog && LeaveLobbyBeforeExit && LeaveConfirmation != null)
        {
            LeaveConfirmation.ReturnScenePath = PreviousScenePath;
            LeaveConfirmation.ShowConfirmation();
            return;
        }

        // Opuść lobby i zmień scenę bez potwierdzenia (dialog nie istnieje)
        if (ShowConfirmDialog && LeaveLobbyBeforeExit && LeaveConfirmation == null)
        {
            if (eosManager != null && !string.IsNullOrEmpty(eosManager.currentLobbyId))
            {
                eosManager.LeaveLobby();
            }
            GetTree().ChangeSceneToFile(PreviousScenePath);
            return;
        }

        // Opuść lobby i zmień scenę (bez pytania)
        if (!ShowConfirmDialog && LeaveLobbyBeforeExit)
        {
            if (eosManager != null && !string.IsNullOrEmpty(eosManager.currentLobbyId))
            {
                eosManager.LeaveLobby();
            }
            GetTree().ChangeSceneToFile(PreviousScenePath);
            return;
        }

        if (ShowConfirmDialog && !LeaveLobbyBeforeExit)
        {
            GD.PrintErr("You can not change scene without leaving lobby");
            return;
        }

        // Jeśli nie jesteśmy w lobby (lub eosManager nie istnieje)
        if (eosManager == null || string.IsNullOrEmpty(eosManager.currentLobbyId))
        {
            GetTree().ChangeSceneToFile(PreviousScenePath);
            return;
        }

    }
}
