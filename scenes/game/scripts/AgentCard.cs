using Godot;
using System.Collections.Generic;

public partial class AgentCard : PanelContainer
{
    /// <summary>
    /// Reference to the main game controller.
    /// </summary>
    [Export] private MainGame mainGame;

    /// <summary>
    /// Control used to display the highlight border around the card.
    /// </summary>
    [Export] public Control highlightBorder;

    /// <summary>
    /// Container holding the content of the card.
    /// </summary>
	[Export] public Control contentContainer;

    /// <summary>
    /// Color applied to the card when hovering or interacting.
    /// </summary>
	[Export] public Color darkColor = new Color(0.7f, 0.7f, 0.7f);

    /// <summary>
    /// Manager responsible for card logic.
    /// </summary>
	[Export] private CardManager cardManager;

    /// <summary>
    /// Label displaying the card's word.
    /// </summary>
	[Export] private Label textLabel;

    /// <summary>
    /// Texture rect displaying the card image.
    /// </summary>
	[Export] private TextureRect cardImage;

    /// <summary>
    /// Label for debugging selection display.
    /// </summary>
    [Export] private Label debugSelectionsDisplay;

    /// <summary>
    /// Container for player selection icons.
    /// </summary>
    [Export] private HBoxContainer iconsContainer;

    /// <summary>
    /// Button to confirm card selection.
    /// </summary>
    [Export] private Button confirmButton;

    /// <summary>
    /// Path to the front side texture node.
    /// </summary>
    [Export] private NodePath frontSideRectPath;

    /// <summary>
    /// Path to the background texture node when revealed.
    /// </summary>
    [Export] private NodePath revealedBackgroundRectPath;

    /// <summary>
    /// Path to the face texture node when revealed.
    /// </summary>
    [Export] private NodePath revealedFaceRectPath;

    private TextureRect frontSideRect;
    private TextureRect revealedBackgroundRect;
    private TextureRect revealedFaceRect;

    [ExportGroup("Card Textures")]
    /// <summary>
    /// Array of textures for blue team cards.
    /// </summary>
    [Export] private Texture2D[] blueCardTextures;

    /// <summary>
    /// Array of textures for red team cards.
    /// </summary>
    [Export] private Texture2D[] redCardTextures;

    /// <summary>
    /// Array of textures for neutral/civilian cards.
    /// </summary>
    [Export] private Texture2D[] neutralCardTextures;

    /// <summary>
    /// Texture for the assassin card.
    /// </summary>
    [Export] private Texture2D assassinCardTexture;

	/// <summary>
    /// Signal emitted when the card is clicked/selected.
    /// </summary>
    /// <param name="card">The card instance that was selected.</param>
    [Signal] public delegate void CardSelectedEventHandler(AgentCard card);

    /// <summary>
    /// Signal emitted when the card selection is confirmed.
    /// </summary>
    /// <param name="card">The card instance that was confirmed.</param>
    [Signal] public delegate void CardConfirmedEventHandler(AgentCard card);

    /// <summary>
    /// The underlying card data structure.
    /// </summary>
    /// Mainly for AI lib
    public game.Card cardInfo;

	private Vector2 hoverScale = new Vector2(1.05f, 1.05f);
	private float duration = 0.1f;

	private Vector2 normalScale = Vector2.One;
	private Tween tween;

    private byte? id;
    
    /// <summary>
    /// Gets the unique ID of the card.
    /// </summary>
    public byte? Id
    {
        get { return id; }
    }
    private CardManager.CardType type;

    /// <summary>
    /// Gets the type of the card (e.g., Blue, Red, Assassin, Neutral).
    /// </summary>
	public CardManager.CardType Type
	{
		get { return type; }
	}
    private List<int> selectedBy; // list of indexes of players who selected this card
    
    /// <summary>
    /// Gets the number of players who have selected this card.
    /// </summary>
    public int SelectionsCount
    {
        get { return selectedBy.Count; }
    }

    private int teamIndex = 0;

    public override void _Ready()
	{
		base._Ready();
        frontSideRect = GetNode<TextureRect>("%AgentCardU");
        revealedBackgroundRect = GetNode<TextureRect>("%RevealedBackgroundU");
        revealedFaceRect = GetNode<TextureRect>("%RevealedFaceU");

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

    /// <summary>
    /// Sets the ID of the card. Can only be set once if it is currently null.
    /// </summary>
    /// <param name="newId">The new ID to assign.</param>
    public void SetId(byte newId)
    {
        if (id == null)
            id = newId;
    }

    /// <summary>
    /// Adjusts the pivot offset to the center of the control.
    /// </summary>
    private void SetPivotCenter()
	{
		PivotOffset = Size / 2;
	}

	/// <summary>
    /// Handles the MouseEntered event to trigger hover animation.
    /// </summary>
    private void OnHoverEnter()
	{
		ZIndex = 1;
		Animate(true);
    }

    /// <summary>
    /// Handles the MouseExited event to trigger un-hover animation.
    /// </summary>
	private void OnHoverExit()
	{
		ZIndex = 0;
		Animate(false);
    }

    /// <summary>
    /// Animates the card's scale and modulation based on hover state.
    /// </summary>
    /// <param name="isHovering">True if the mouse is hovering over the card, false otherwise.</param>
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

    /// <summary>
    /// Sets the team index for the card, mainly used for determining the texture.
    /// </summary>
    /// <param name="index">The team index.</param>
    public void SetTeamIndex(int index)
    {
        teamIndex = index;
    }

    /// <summary>
    /// Initializes the card with data from the CardManager.
    /// </summary>
	public void SetCard()
	{
        cardInfo = cardManager.TakeCard();
		SetCardName(cardInfo.Word);
		type = CardTypeExt.FromGameTeam(cardInfo.Team);
	}

    /// <summary>
    /// Sets the text label on the card.
    /// </summary>
    /// <param name="name">The word to display on the card.</param>
	private void SetCardName(string name)
	{
		textLabel.Text = name;
	}

    /// <summary>
    /// Reveals the card's color and texture based on its type.
    /// </summary>
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

    /// <summary>
    /// Sets the card's revealed face texture from an array, based on team index.
    /// </summary>
    /// <param name="textures">Array of textures to choose from.</param>
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

    /// <summary>
    /// Clears all player selections for this card.
    /// </summary>
    public void ClearSelections()
    {
        //GD.Print($"[MainGame][Card] Clearing selections of card={id}");
        selectedBy.Clear();
        UpdateSelectionDisplay();
    }

    /// <summary>
    /// Sets the selections based on a ushort bitmask.
    /// </summary>
    /// <param name="selections">A bitmask where the n-th bit represents whether the card is selected by the player at the n-th index.</param>
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

    /// <summary>
    /// Gets the current selections as a ushort bitmask.
    /// </summary>
    /// <returns>A bitmask representing the current selections.</returns>
    public ushort GetSelectionsAsUshort()
    {
        ushort selections = 0b0;
        foreach (int index in selectedBy)
        {
            selections |= (ushort)(1 << index);
        }
        return selections;
    }

    /// <summary>
    /// Adds a player's selection to this card.
    /// </summary>
    /// <param name="playerIndex">The index of the player selecting the card.</param>
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

    /// <summary>
    /// Removes a player's selection from this card.
    /// </summary>
    /// <param name="playerIndex">The index of the player unselecting the card.</param>
    public void RemoveSelection(int playerIndex)
    {
        GD.Print($"[MainGame][Card] Removing a selection from card={id} by player={playerIndex}");
        if (selectedBy.Contains(playerIndex))
        {
            selectedBy.Remove(playerIndex);
            UpdateSelectionDisplay();
        }
    }

    /// <summary>
    /// Updates the visual display of player selections (icons).
    /// </summary>
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

    /// <summary>
    /// Configures the local player's selection icon (tooltip, events).
    /// </summary>
    /// <param name="icon">The icon texture rect.</param>
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

    /// <summary>
    /// Checks if the card is selected by a specific player.
    /// </summary>
    /// <param name="playerIndex">The player's index.</param>
    /// <returns>True if the player has selected this card, false otherwise.</returns>
    public bool IsSelectedBy(int playerIndex)
    {
        return selectedBy.Contains(playerIndex);
    }

    /// <summary>
    /// Handles the confirm button press event.
    /// </summary>
    public void OnConfirmButtonPressed()
	{
		EmitSignal(SignalName.CardConfirmed, this);
	}
}
