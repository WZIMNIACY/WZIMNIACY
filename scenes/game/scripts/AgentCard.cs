using Godot;

public partial class AgentCard : PanelContainer
{
	[Export] public Control highlightBorder;
	[Export] public Control contentContainer;
	[Export] public Color darkColor = new Color(0.7f, 0.7f, 0.7f);
	[Export] private CardManager cardManager;
	[Export] private Label textLabel;
	[Export] private TextureRect cardImage;

	[Export] private Button selectButton;

	[Signal] public delegate void CardConfirmedEventHandler(AgentCard card);

    /// Mainly for AI lib
    public game.Card cardInfo;

	private Vector2 hoverScale = new Vector2(1.05f, 1.05f);
	private float duration = 0.1f;

	private Vector2 normalScale = Vector2.One;
	private Tween tween;

	private CardManager.CardType type;
	public CardManager.CardType Type
	{
		get { return type; }
	}
	private bool selected = false;

	public override void _Ready()
	{
		base._Ready();
		CallDeferred(nameof(SetPivotCenter));

		MouseEntered += OnHoverEnter;
		MouseExited += OnHoverExit;

		Resized += SetPivotCenter;

		cardManager.CardManagerReady += SetCard;
		AddToGroup("cards");
		MouseFilter = MouseFilterEnum.Pass;
		SetProcessInput(true);
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

	private void SetCard()
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
		if(type == CardManager.CardType.Blue)
		{
			cardImage.Modulate = new Color("4597ffff");
		}
		else if(type == CardManager.CardType.Red)
		{
			cardImage.Modulate = new Color("ff627bff");
		}
		else if(type == CardManager.CardType.Assassin)
		{
			cardImage.Modulate = new Color("767676aa");
		}
		else
		{
			cardImage.Modulate = new Color("ffffbd");
		}
	}

	public override void _GuiInput(InputEvent @event)
	{
		base._GuiInput(@event);
		if (@event is InputEventMouseButton mouseEvent &&
			mouseEvent.Pressed &&
			mouseEvent.ButtonIndex == MouseButton.Left)
		{
			ToggleSelected();
		}
	}

	public void Unselect()
	{
		selected = false;
		selectButton.Visible = false;
	}

	public void ToggleSelected()
	{
		selected = !selected;
		selectButton.Visible = selected;
	}

	public void OnSelectButtonPressed()
	{
		EmitSignal(SignalName.CardConfirmed, this);
	}
}
