using System;

public static class TeamEnumExt
{
    public static game.Team ToAiLibTeam(this MainGame.Team team)
    {
        return team switch
        {
            MainGame.Team.Red => game.Team.Red,
            MainGame.Team.Blue => game.Team.Blue,
            _ => throw new Exception("Team None has no conversion")
        };
    }

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
}
