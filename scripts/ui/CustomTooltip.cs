using Godot;

public partial class CustomTooltip : PanelContainer
{
    private Label label;
    private Timer showTimer;

    private const float ShowDelay = 0.2f; // 200ms opóźnienia
    private const int MouseOffsetX = 10;
    private const int MouseOffsetY = 10;

    public override void _Ready()
    {
        base._Ready();

        // Pobierz label z dziecka
        label = GetNode<Label>("Label");

        // Stwórz timer
        showTimer = new Timer();
        showTimer.WaitTime = ShowDelay;
        showTimer.OneShot = true;
        showTimer.Timeout += OnShowTimerTimeout;
        AddChild(showTimer);

        // Początkowa konfiguracja
        Visible = false;
        ZIndex = 100;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        // Aktualizuj pozycję tooltipa gdy jest widoczny
        if (Visible)
        {
            UpdatePosition();
        }
    }

    /// <summary>
    /// Pokazuje tooltip z podanym tekstem
    /// </summary>
    public void Show(string text)
    {
        if (label != null)
        {
            label.Text = text;
            // Wymuś aktualizację rozmiaru
            CallDeferred(nameof(UpdateSize));
        }

        if (!showTimer.IsStopped())
        {
            showTimer.Stop();
        }

        showTimer.Start();
    }

    /// <summary>
    /// Ukrywa tooltip
    /// </summary>
    public new void Hide()
    {
        Visible = false;

        if (showTimer != null && !showTimer.IsStopped())
        {
            showTimer.Stop();
        }
    }

    /// <summary>
    /// Callback gdy timer się skończy
    /// </summary>
    private void OnShowTimerTimeout()
    {
        Visible = true;
        UpdatePosition();
    }

    /// <summary>
    /// Aktualizuje rozmiar tooltipa do zawartości
    /// </summary>
    private void UpdateSize()
    {
        // Resetuj rozmiar
        Size = Vector2.Zero;
        CustomMinimumSize = Vector2.Zero;

        // Wymuś przeliczenie layoutu
        if (label != null)
        {
            label.Size = Vector2.Zero;
        }

        ResetSize();
    }

    /// <summary>
    /// Aktualizuje pozycję tooltipa przy kursorze
    /// </summary>
    private void UpdatePosition()
    {
        var mousePos = GetGlobalMousePosition();
        Position = mousePos + new Vector2(MouseOffsetX, MouseOffsetY);

        // Upewnij się że tooltip nie wychodzi poza ekran
        var tooltipSize = Size;
        var screenSize = GetViewportRect().Size;

        if (Position.X + tooltipSize.X > screenSize.X)
        {
            Position = new Vector2(screenSize.X - tooltipSize.X - 10, Position.Y);
        }
        if (Position.Y + tooltipSize.Y > screenSize.Y)
        {
            Position = new Vector2(Position.X, mousePos.Y - tooltipSize.Y - 10);
        }
    }
}
