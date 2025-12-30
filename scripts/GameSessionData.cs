using System;

// GameSessionData.cs przechowuje lokalne dane dotyczące sesji gry tworzonej przez hosta
// Dane te są synchronizowane pomiędzy hostem i klientami poprzez atrybuty lobby (EOS)

// Stan sesji gry na poziomie logicznym
public enum GameSessionState
{
    None,       // brak aktywnej sesji
    Starting,   // sesja uruchamiana (przejście z lobby do gry)
    InGame      // gra w toku
}

// Lokalny opis sesji gry (host + klienci)
public class GameSessionData
{
    // Krótki identyfikator sesji gry (debug / logi / reconnect)
    public string SessionId { get; set; } = "";

    // Identyfikator lobby, w ramach którego uruchomiono sesję
    public string LobbyId { get; set; } = "";

    // ProductUserId hosta, który rozpoczął sesję
    public string HostUserId { get; set; } = "";

    // Seed do deterministycznej inicjalizacji rozgrywki (np. generowanie planszy)
    public ulong Seed { get; set; } = 0;

    // Aktualny stan sesji gry synchronizowany przez atrybuty lobby
    public GameSessionState State { get; set; } = GameSessionState.None;

    // Sprawdza czy sesja zawiera komplet minimalnych danych
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
