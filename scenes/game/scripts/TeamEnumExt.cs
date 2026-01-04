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
}
