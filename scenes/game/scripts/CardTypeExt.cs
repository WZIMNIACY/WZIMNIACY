public static class CardTypeExt
{
    public static CardManager.CardType FromGameTeam(game.Team team)
    {
        switch (team)
        {
            case game.Team.Red:
                return CardManager.CardType.Red;
            case game.Team.Blue:
                return CardManager.CardType.Blue;
            case game.Team.Assassin:
                return CardManager.CardType.Assassin;
            default:
                return CardManager.CardType.Common;
        }
    }

    public static game.Team ToGameTeam(this CardManager.CardType type)
    {
        switch (type)
        {
            case CardManager.CardType.Red:
                return game.Team.Red;
            case CardManager.CardType.Blue:
                return game.Team.Blue;
            case CardManager.CardType.Assassin:
                return game.Team.Assassin;
            default:
                return game.Team.Neutral;
        }
    }
}

