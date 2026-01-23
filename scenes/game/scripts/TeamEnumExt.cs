using System;

/// <summary>
/// Extension methods and helpers for converting between different Team enum types used in the game (MainGame, AiLib, EOSManager).
/// </summary>
public static class TeamEnumExt
{
    /// <summary>
    /// Converts a MainGame.Team value to its corresponding game.Team (AiLib) representation.
    /// </summary>
    /// <param name="team">The MainGame.Team value to convert.</param>
    /// <returns>The corresponding game.Team value.</returns>
    /// <exception cref="Exception">Thrown if the team is None, as it has no valid conversion to game.Team.</exception>
    public static game.Team ToAiLibTeam(this MainGame.Team team)
    {
        return team switch
        {
            MainGame.Team.Red => game.Team.Red,
            MainGame.Team.Blue => game.Team.Blue,
            _ => throw new Exception("Team None has no conversion")
        };
    }

    /// <summary>
    /// Converts a MainGame.Team value to its corresponding EOSManager.Team representation.
    /// </summary>
    /// <param name="team">The MainGame.Team value to convert.</param>
    /// <returns>The corresponding EOSManager.Team value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the provided team value is not a valid MainGame.Team.</exception>
    public static EOSManager.Team ToEOSManagerTeam(this MainGame.Team team)
    {
        return team switch
        {
            MainGame.Team.Red => EOSManager.Team.Red,
            MainGame.Team.Blue => EOSManager.Team.Blue,
            MainGame.Team.None => EOSManager.Team.None,
             _ => throw new ArgumentOutOfRangeException(nameof(team), team, null) // zeby kompilator nie dawal ostrzezenia
        };
    }

    /// <summary>
    /// Converts an EOSManager.Team value to its corresponding MainGame.Team representation.
    /// </summary>
    /// <param name="team">The EOSManager.Team value to convert.</param>
    /// <returns>The corresponding MainGame.Team value. Note that Universal is treated as Blue.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the provided team value is not a valid EOSManager.Team.</exception>
    public static MainGame.Team FromEOSManagerTeam(EOSManager.Team team)
    {
        return team switch
        {
            EOSManager.Team.Red => MainGame.Team.Red,
            EOSManager.Team.Blue or EOSManager.Team.Universal => MainGame.Team.Blue,
            EOSManager.Team.None => MainGame.Team.None,
            _ => throw new ArgumentOutOfRangeException(nameof(team), team, null)
        };
    }
}
