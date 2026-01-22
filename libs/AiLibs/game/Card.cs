using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace game
{
    public class Card
    {
        public string Word { get; init; }
        public List<double>? Vector { get; init; }
        public Team Team { get; init; }

        public Card(string word, List<double>? vector, Team team)
        {
            Word = word ?? throw new ArgumentNullException(nameof(word));
            Vector = vector;
            Team = team;
        }

        public string fullWordInfo()
        {
            StringBuilder sb = new StringBuilder();

            if (Vector != null)
            {
                foreach (double item in Vector)
                {
                    sb.AppendLine(item.ToString());
                }
            }

            return $"Word: {Word}\nVector: {sb.ToString()}\nTeam: {Team}";
        }
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            if (Vector != null)
            {
                foreach (double item in Vector)
                {
                    sb.AppendLine(item.ToString());
                }

            }

            return $"Word: {Word}\nTeam: {Team.ToString()}";
        }

        public string toJson()
        {
            return System.Text.Json.JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)

            });
        }

        public static Card fromJson(string json)
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                    WriteIndented = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString,
                    Converters = { new JsonStringEnumConverter() }
            };

            return System.Text.Json.JsonSerializer.Deserialize<Card>(json, options)!;
        }

        public override bool Equals(object? obj)
        {
            Card? card = obj as Card;
            if (card == null)
                return false;

            return this.GetHashCode() == card.GetHashCode();

            
        }

        public override int GetHashCode()
        {
            return Word.GetHashCode() + Team.GetHashCode();
        }

        
    }
}
