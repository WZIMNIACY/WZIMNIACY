// GameSessionData.cs przechowuje lokalne dane dotyczące sesji gry tworzonej przez hosta
// Dane te są synchronizowane pomiędzy hostem i klientami poprzez atrybuty lobby (EOS)

/// <summary>
/// Represents the logical state of a game session.
/// </summary>
public enum GameSessionState
{
    /// <summary>
    /// No active session.
    /// </summary>
    None,
    /// <summary>
    /// Session is starting (transition from lobby to game).
    /// </summary>
    Starting,
    /// <summary>
    /// Game is in progress.
    /// </summary>
    InGame
}

/// <summary>
/// Stores local data regarding a game session created by the host.
/// Data is synchronized between host and clients via lobby attributes (EOS).
/// </summary>
public class GameSessionData
{
    /// <summary>
    /// Short identifier for the game session (used for debug, logs, reconnect).
    /// </summary>
    public string SessionId { get; set; } = "";

    /// <summary>
    /// Identifier of the lobby where the session was started.
    /// </summary>
    public string LobbyId { get; set; } = "";

    /// <summary>
    /// ProductUserId of the host who started the session.
    /// </summary>
    public string HostUserId { get; set; } = "";

    /// <summary>
    /// Seed for deterministic gameplay initialization (e.g. board generation).
    /// </summary>
    public ulong Seed { get; set; } = 0;

    /// <summary>
    /// Current state of the game session synchronized via lobby attributes.
    /// </summary>
    public GameSessionState State { get; set; } = GameSessionState.None;

    /// <summary>
    /// Checks if the session contains the complete set of minimal data required.
    /// </summary>
    /// <returns>True if all required fields are set; otherwise, false.</returns>
    public bool IsValid()
    {
        return !string.IsNullOrEmpty(SessionId)
            && !string.IsNullOrEmpty(LobbyId)
            && !string.IsNullOrEmpty(HostUserId)
            && Seed != 0
            && State != GameSessionState.None;
    }

    // Tekstowa reprezentacja sesji (do logów i debugowania)
    public override string ToString()
    {
        return $"SessionId={SessionId}, LobbyId={LobbyId}, HostUserId={HostUserId}, Seed={Seed}, State={State}";
    }
}
