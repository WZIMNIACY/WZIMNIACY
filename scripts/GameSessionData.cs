using System;
//GameSessionData.cs przechowuje dane, na temat konkretnej sesji tworzonej przez hosta. s

// Lokalny opis sesji gry (host + klienci) odczytywany z atrybutów lobby
public class GameSessionData
{
    public string SessionId { get; set; } = "";
    public string LobbyId { get; set; } = "";
    public string HostUserId { get; set; } = "";
    public ulong Seed { get; set; } = 0;
    //seed ma służyć do losowania planszy i ustawiania na podstawie seed'u u każdego takiej samej

    // “stan” sesji na poziomie lobby-atributów (minimalnie)
    public string State { get; set; } = "None"; // None / Starting / InGame (na razie użyjemy Starting)

    public bool IsValid()
    {
        return !string.IsNullOrEmpty(SessionId)
               && !string.IsNullOrEmpty(LobbyId)
               && !string.IsNullOrEmpty(HostUserId)
               && Seed != 0
               && !string.IsNullOrEmpty(State);
    }

    public override string ToString()
    {
        return $"SessionId={SessionId}, LobbyId={LobbyId}, HostUserId={HostUserId}, Seed={Seed}, State={State}";
    }
}
