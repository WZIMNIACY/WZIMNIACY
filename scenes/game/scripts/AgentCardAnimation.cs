using Godot;

public partial class AgentCardAnimation : PanelContainer
{
    [Export] public Control highlightBorder;
    [Export] public Control contentContainer;
    [Export] public Color darkColor = new Color(0.7f, 0.7f, 0.7f);
    [Export] private CardMenager cardMenager;
    [Export] private Label textLabel;
    [Export] private TextureRect cardImage;

    private Vector2 hoverScale = new Vector2(1.05f, 1.05f); 
    private float duration = 0.1f; 

    private Vector2 normalScale = Vector2.One;
    private Tween tween;

    private string type;
    private bool isChecked = false;

    public override void _Ready()
    {
        base._Ready();
        CallDeferred(nameof(SetPivotCenter));

        MouseEntered += OnHoverEnter;
        MouseExited += OnHoverExit;
    
        Resized += SetPivotCenter;

        //GD.Print(cardMenager.GetCardName());
        SetCardName(cardMenager.GetCardName());
        type = cardMenager.GetCardType();
        SetColor();
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

    private void SetCardName(string name){
        textLabel.Text = name;
    }

    private void SetColor(){
        if(type == "blue"){
			cardImage.Modulate = new Color("4597ffff");
		}
		else if(type == "red"){
			cardImage.Modulate = new Color("ff627bff");
		}
		else if(type == "assassin"){
			cardImage.Modulate = new Color("767676aa");
		}
    }
}