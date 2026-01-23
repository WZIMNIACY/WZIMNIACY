/// <summary>
/// Extension methods and helpers for converting between CardType and Team enums.
/// </summary>
public static class CardTypeExt
{
    /// <summary>
    /// Converts a game Team enum to the corresponding CardManager.CardType.
    /// </summary>
    /// <param name="team">The team to convert.</param>
    /// <returns>The corresponding CardType (Red, Blue, Assassin, or Common).</returns>
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

    /// <summary>
    /// Converts a CardManager.CardType to the corresponding game Team enum.
    /// </summary>
    /// <param name="type">The card type to convert.</param>
    /// <returns>The corresponding Team (Red, Blue, Assassin, or Neutral).</returns>
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

