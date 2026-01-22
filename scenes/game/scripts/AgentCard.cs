using Godot;
using System.Collections.Generic;

public partial class AgentCard : PanelContainer
{
    [Export] private MainGame mainGame;
    [Export] public Control highlightBorder;
	[Export] public Control contentContainer;
	[Export] public Color darkColor = new Color(0.7f, 0.7f, 0.7f);
	[Export] private CardManager cardManager;
	[Export] private Label textLabel;
	[Export] private TextureRect cardImage;
    [Export] private Label debugSelectionsDisplay;
    [Export] private HBoxContainer iconsContainer;
    [Export] private Button confirmButton;
    [Export] private NodePath frontSideRectPath;
    [Export] private NodePath revealedBackgroundRectPath;
    [Export] private NodePath revealedFaceRectPath;
    private TextureRect frontSideRect;
    private TextureRect revealedBackgroundRect;
    private TextureRect revealedFaceRect;

    [ExportGroup("Card Textures")]
    [Export] private Texture2D[] blueCardTextures;
    [Export] private Texture2D[] redCardTextures;
    [Export] private Texture2D[] neutralCardTextures;
    [Export] private Texture2D assassinCardTexture;

	[Signal] public delegate void CardSelectedEventHandler(AgentCard card);
    [Signal] public delegate void CardConfirmedEventHandler(AgentCard card);

    /// Mainly for AI lib
    public game.Card cardInfo;

	private Vector2 hoverScale = new Vector2(1.05f, 1.05f);
	private float duration = 0.1f;

	private Vector2 normalScale = Vector2.One;
	private Tween tween;

    private byte? id;
    public byte? Id
    {
        get { return id; }
    }
    private CardManager.CardType type;
	public CardManager.CardType Type
	{
		get { return type; }
	}
    private List<int> selectedBy; // list of indexes of players who selected this card
    public int SelectionsCount
    {
        get { return selectedBy.Count; }
    }

    private int teamIndex = 0;

    public override void _Ready()
	{
		base._Ready();
        frontSideRect = GetNode<TextureRect>(frontSideRectPath);
        revealedBackgroundRect = GetNode<TextureRect>(revealedBackgroundRectPath);
        revealedFaceRect = GetNode<TextureRect>(revealedFaceRectPath);

		CallDeferred(nameof(SetPivotCenter));

		MouseEntered += OnHoverEnter;
		MouseExited += OnHoverExit;

		Resized += SetPivotCenter;

		AddToGroup("cards");
		MouseFilter = MouseFilterEnum.Pass;
		SetProcessInput(true);

        selectedBy = new List<int>();

        iconsContainer.MouseFilter = MouseFilterEnum.Ignore;
    }

    public void SetId(byte newId)
    {
        if (id == null)
            id = newId;
    }

    private void SetPivotCenter()
	{
		PivotOffset = Size / 2;
	}

	private void OnHoverEnter()
	{
		ZIndex = 1;
		Animate(true);
    }

	private void OnHoverExit()
	{
		ZIndex = 0;
		Animate(false);
    }

	private void Animate(bool isHovering)
	{
		if (tween != null && tween.IsValid()) tween.Kill();

		tween = CreateTween();
		tween.SetParallel(true);
		tween.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);

		if (isHovering)
		{
			tween.TweenProperty(this, "scale", hoverScale, duration);

			if (contentContainer != null)
				tween.TweenProperty(contentContainer, "modulate", darkColor, duration);
		}
		else
		{
			tween.TweenProperty(this, "scale", normalScale, duration);

			if (contentContainer != null)
				tween.TweenProperty(contentContainer, "modulate", Colors.White, duration);
		}
	}

    public void SetTeamIndex(int index)
    {
        teamIndex = index;
    }

	public void SetCard()
	{
        cardInfo = cardManager.TakeCard();
		SetCardName(cardInfo.Word);
		type = CardTypeExt.FromGameTeam(cardInfo.Team);
	}

	private void SetCardName(string name)
	{
		textLabel.Text = name;
	}

	public void SetColor()
	{
		frontSideRect.Visible = false;
        revealedBackgroundRect.Visible = true;

        if (revealedFaceRect != null)
        {
            revealedFaceRect.Visible = true;
            revealedFaceRect.Modulate = Colors.White;

            switch (type)
            {
                case CardManager.CardType.Blue:
                    SetTextureFromArray(blueCardTextures);
                    break;
                case CardManager.CardType.Red:
                    SetTextureFromArray(redCardTextures);
                    break;
                case CardManager.CardType.Assassin:
                    if (assassinCardTexture != null) revealedFaceRect.Texture = assassinCardTexture;
                    break;
                case CardManager.CardType.Common:
                default:
                    SetTextureFromArray(neutralCardTextures);
                    break;
            }
        }
	}

    private void SetTextureFromArray(Texture2D[] textures)
    {
        if (textures == null || textures.Length == 0) return;

        int finalIndex = teamIndex % textures.Length;

        if (textures[finalIndex] != null)
        {
            revealedFaceRect.Texture = textures[finalIndex];
        }
    }

	public override void _GuiInput(InputEvent @event)
	{
		base._GuiInput(@event);
		if (@event is InputEventMouseButton mouseEvent &&
			mouseEvent.Pressed &&
			mouseEvent.ButtonIndex == MouseButton.Left)
		{
            //ToggleSelected();
		    EmitSignal(SignalName.CardSelected, this);
        }
    }

    public void ClearSelections()
    {
        //GD.Print($"[MainGame][Card] Clearing selections of card={id}");
        selectedBy.Clear();
        UpdateSelectionDisplay();
    }

    public void SetSelections(ushort selections) // n-th bit represents whether selected by player of n-th index
    {
        //GD.Print($"[MainGame][Card] Setting selections of card={id} by selections_ushort={Convert.ToString(selections, 2)}");

        if (GetSelectionsAsUshort() == selections)
            return;

        selectedBy.Clear();
        for (int i = 0; i < 10; i++)
        {
            if ((selections & (1 << i)) != 0)
            {
                selectedBy.Add(i);
            }
        }

        int localPlayerIndex = mainGame.GetLocalPlayerIndex();
        if (selectedBy.Contains(localPlayerIndex))
        {
            selectedBy.Remove(localPlayerIndex);
            selectedBy.Insert(0, localPlayerIndex);
        }

        UpdateSelectionDisplay();
    }

    public ushort GetSelectionsAsUshort()
    {
        ushort selections = 0b0;
        foreach (int index in selectedBy)
        {
            selections |= (ushort)(1 << index);
        }
        return selections;
    }

    public void AddSelection(int playerIndex)
    {
        GD.Print($"[MainGame][Card] Adding a selection to card={id} by player={playerIndex}");
        if (!selectedBy.Contains(playerIndex))
        {
            if (mainGame.GetLocalPlayerIndex() == playerIndex)
                selectedBy.Insert(0, playerIndex);
            else
                selectedBy.Add(playerIndex);
            UpdateSelectionDisplay();
        }
    }

    public void RemoveSelection(int playerIndex)
    {
        GD.Print($"[MainGame][Card] Removing a selection from card={id} by player={playerIndex}");
        if (selectedBy.Contains(playerIndex))
        {
            selectedBy.Remove(playerIndex);
            UpdateSelectionDisplay();
        }
    }

    public void UpdateSelectionDisplay()
    {
        //string indexes = string.Join(", ", selectedBy);
        //debugSelectionsDisplay.Text = indexes;

        int localPLayerIndex = mainGame.GetLocalPlayerIndex();

        foreach (Node child in iconsContainer.GetChildren())
            child.QueueFree();

        foreach (int playerIndex in selectedBy)
        {
            string iconPath = mainGame.PlayersByIndex[playerIndex].profileIconPath;

            Texture2D texture = GD.Load<Texture2D>(iconPath);

            string playerName = mainGame.PlayersByIndex[playerIndex].name;

            var icon = new TextureRect
            {
                Texture = texture,
                CustomMinimumSize = new Vector2(16, 16),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                TooltipText = playerName,
                MouseFilter = MouseFilterEnum.Pass
            };

            if (mainGame.GetLocalPlayerIndex() == playerIndex)
            {
                SetupLocalPlayerIcon(icon);
            }

            iconsContainer.AddChild(icon);
        }

        confirmButton.Visible = selectedBy.Contains(mainGame.GetLocalPlayerIndex());
    }

    private void SetupLocalPlayerIcon(TextureRect icon)
    {
        icon.TooltipText = "Zatwierd\u017A kart\u0119";

        //icon.MouseEntered += () =>
        //{
        //    icon.Modulate = new Color(0.8f, 0.8f, 0.8f, 1f);
        //};

        //icon.MouseExited += () =>
        //{
        //    icon.Modulate = new Color(1f, 1f, 1f, 1f);
        //};

        //icon.GuiInput += (InputEvent e) =>
        //{
        //    if (e is InputEventMouseButton mb &&
        //        mb.Pressed &&
        //        mb.ButtonIndex == MouseButton.Left)
        //    {
        //        OnConfirmButtonPressed();
        //    }
        //};
    }

    public bool IsSelectedBy(int playerIndex)
    {
        return selectedBy.Contains(playerIndex);
    }

    public void OnConfirmButtonPressed()
	{
		EmitSignal(SignalName.CardConfirmed, this);
	}
}
