using Godot;

/// <summary>
/// Obsługuje cofanie się do poprzedniej sceny za pomocą klawisza ESC.
/// Nie działa gdy jakiś input jest aktywny (LineEdit, TextEdit, itp.).
/// Może korzystać z <see cref="LobbyLeaveConfirmation"/> oraz opuszczać lobby przez <see cref="EOSManager.LeaveLobby"/>.
/// </summary>
/// <remarks>
/// Wymaga autoloadu <see cref="EOSManager"/> i (opcjonalnie) instancji <see cref="LobbyLeaveConfirmation"/> w scenie; logika powinna działać w wątku głównym Godota i nie jest thread-safe.
/// </remarks>
public partial class EscapeBackHandler : Node
{
    /// <summary>
    /// Ścieżka do sceny, do której ma wrócić po naciśnięciu ESC
    /// </summary>
    /// <value>Pełna ścieżka pliku sceny docelowej.</value>
    [Export]
    public string PreviousScenePath { get; set; } = "res://scenes/menu/main.tscn";

    /// <summary>
    /// Czy ma opuścić lobby przed zmianą sceny (jeśli jesteśmy w lobby)
    /// </summary>
    /// <value>Gdy <c>true</c>, przed zmianą sceny wywoła opuszczenie lobby.</value>
    [Export]
    public bool LeaveLobbyBeforeExit { get; set; } = false;

    /// <summary>
    /// Czy ma pokazać dialog potwierdzenia przed opuszczeniem (dla lobby)
    /// </summary>
    /// <value><c>true</c> włącza dialog potwierdzenia opuszczenia.</value>
    [Export]
    public bool ShowConfirmDialog { get; set; } = false;

    /// <summary>Autoload EOS używany do obsługi opuszczania lobby.</summary>
    private EOSManager eosManager;

    /// <summary>
    /// Referencja do dialogu potwierdzenia opuszczenia lobby, wykorzystywana przy obsłudze ESC.
    /// </summary>
    /// <value>Instancja okna potwierdzenia; może być <c>null</c>.</value>
    public LobbyLeaveConfirmation LeaveConfirmation { get; set; }

    /// <summary>
    /// Inicjalizuje referencje i pobiera autoload <see cref="EOSManager"/>.
    /// </summary>
    public override void _Ready()
    {
        base._Ready();

        eosManager = GetNode<EOSManager>("/root/EOSManager");
    }

    /// <summary>
    /// Nasłuchuje klawisza ESC i klikania myszy, by obsłużyć cofnięcie lub zwolnić fokus z pól wejściowych; integruje się z <see cref="EOSManager"/> aby blokować ESC podczas dołączania do lobby.
    /// </summary>
    /// <param name="@event">Zdarzenie wejściowe przekazane przez Godot.</param>
    /// <seealso cref="HandleEscapeBack"/>
    /// <seealso cref="IsAnyInputActive"/>
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
    }

    /// <summary>
    /// Obsługuje nieprzechwycone zdarzenia wejściowe (ESC) i wywołuje logikę powrotu, jeśli żaden input nie ma fokusu.
    /// </summary>
    /// <param name="@event">Zdarzenie wejściowe przekazane przez Godot.</param>
    public override void _UnhandledInput(InputEvent @event)
    {
        base._UnhandledInput(@event);

        var viewport = GetViewport();
        if (viewport == null)
            return;

        // Sprawdź czy naciśnięto ESC
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (keyEvent.Keycode == Key.Escape)
            {
                // Jeśli trwa dołączanie do lobby, zablokuj ESC
                if (eosManager != null && eosManager.isJoiningLobby)
                {
                    GD.Print("[EscapeBackHandler:Input] ESC zablokowany - trwa dołączanie do lobby...");
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
    /// Sprawdza czy jakikolwiek element input jest obecnie aktywny (ma focus).
    /// </summary>
    /// <returns>True, jeśli focus posiada LineEdit/TextEdit/CodeEdit/SpinBox.</returns>
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
    /// Obsługuje cofnięcie się do poprzedniej sceny; opcjonalnie wywołuje <see cref="LobbyLeaveConfirmation"/> lub <see cref="EOSManager.LeaveLobby"/> przed zmianą sceny.
    /// <exception>Metoda loguje błąd, gdy wymagane warunki opuszczenia lobby nie są spełnione.</exception>
    /// </summary>
    /// <seealso cref="LobbyLeaveConfirmation.ShowConfirmation"/>
    /// <seealso cref="EOSManager.LeaveLobby"/>
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
            GD.PrintErr("[EscapeBackHandler] Cannot change scene without leaving lobby");
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
