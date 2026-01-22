using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using game;
using AI;
using System.Security;
using System.Text;

namespace hints
{
    [Serializable]
    public sealed class Hint
    {
        [JsonPropertyName("Word")]
        public string Word { get; set; }
        [JsonPropertyName("NOSW")]
        public int NoumberOfSimilarWords { get; set; }

        [JsonPropertyName("Cards")]
        public List<Card>? Cards { get; set; }

        [JsonConstructor]
        public Hint(string word, List<Card>? cards, int noumberOfSimilarWords)
        {
            this.Word = word;
            this.NoumberOfSimilarWords = noumberOfSimilarWords;
            this.Cards = cards;
        }

        public async static Task<Hint> Create(Deck deck, ILLM llm, Team nowTour, List<string> previousHints, uint maxTokens = 256, bool testMode = false)
        {

            //

            //Prompt set up
            string _nowTour = nowTour.ToString();
            string _actualDeck = deck.ToJson();
            string? lastWrongHint = null;

            var assembly = Assembly.GetExecutingAssembly();
            string hintPromptRes = "Hint_Generating.hintPrompt.txt";
            string systemPromptHint;
            using (Stream? stream = assembly.GetManifestResourceStream(hintPromptRes))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException("Couldn't find embeded resource hintPrompt.txt");
                }

                using (StreamReader reader = new StreamReader(stream))
                {
                    systemPromptHint = reader.ReadToEnd();
                }
            }

            string userPromptHint = $"" +
            $"_nowTour = {_nowTour}\n" +
            $"_actualDeck = {_actualDeck}" +
            $"_previousHints = {previousHints}" +
            $"_lastWrongHint = {((lastWrongHint == null)? "" : lastWrongHint)}\n";

            //Genereting hint
            while (true)
            {
                try
                {
                    Console.WriteLine("Generating hint...");
                    string response = await llm.SendRequestAsync(systemPromptHint, userPromptHint, maxTokens);
                    Hint hint = Hint.FromJson(response);

                    bool hintInDeck = deck.Cards.Any(card => card.Word.Equals(hint.Word, StringComparison.OrdinalIgnoreCase));
                    bool cardsFromHintInDeck = hint.Cards != null && hint.Cards.Any(card => deck.Cards.Any(deckCard => deckCard.Word.Equals(card.Word, StringComparison.OrdinalIgnoreCase)));
                    bool hintIsEmpty = string.IsNullOrWhiteSpace(hint.Word);
                    bool teamOK = hint.Cards != null && hint.Cards.All(card => card.Team == nowTour);
                    bool hitDoesNotExistInPreviousHints = !previousHints.Any(previousHint => previousHint.Equals(hint.Word, StringComparison.OrdinalIgnoreCase));

                    if (hintInDeck || hintIsEmpty || !teamOK || !cardsFromHintInDeck || !hitDoesNotExistInPreviousHints)
                    {

                        if (hintInDeck)
                            Console.WriteLine("Generated hint is already in deck, regenerating...");
                        else if (hintIsEmpty)
                            Console.WriteLine("Generated hint is empty, regenerating...");
                        else if (!teamOK)
                            Console.WriteLine("Generated hint has cards that do not belong to the current team, regenerating...");
                        else if (!cardsFromHintInDeck)
                            Console.WriteLine("Generated hint contains cards that are not in the deck, regenerating...");
                        else if (!hitDoesNotExistInPreviousHints)
                            Console.WriteLine("Generated hint already exists in previous hints, regenerating...");

                        if (testMode)
                        {

                            if(lastWrongHint != null)
                            {
                                Console.WriteLine("\n\n=== Last Wrong Hint Details ===");
                                Console.WriteLine(lastWrongHint);
                                Console.WriteLine("=== End of Last Wrong Hint Details ===\n\n");
                            }
                            
                            
                            Console.WriteLine("=== Generated Hint Details ===");
                            Console.WriteLine(response);
                            Console.WriteLine("=== End of Generated Hint Details ===\n\n");
                        }

                        lastWrongHint = response;

                        continue;
                    }
                    else
                    {
                        return hint;
                    }
                }
                catch (HintException ex)
                {
                    Console.WriteLine($"Failed to parse hint response: {ex.Message}, regenerating...");

                    continue;
                }
            }
        }

        public string toJson()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.WriteIndented = true;
            options.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

            string json = JsonSerializer.Serialize(this, options);

            if (json == "" || json == " " || json == null)
                throw new HintException("HITN Json serialization ERROR!");
            else
                return json;
        }

        public static Hint FromJson(string jsonFormat)
        {
            if (string.IsNullOrWhiteSpace(jsonFormat))
                throw new HintException("Input jsonFormat is null or whitespace.");


            int start = jsonFormat.IndexOf('{');
            int end = jsonFormat.LastIndexOf('}');
            if (start == -1 || end == -1 || end < start)
                throw new HintException("Input does not contain a valid JSON object.");

            string trimmed = jsonFormat.Substring(start, end - start + 1).Trim();

            Hint? hint;

            try
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

                hint = JsonSerializer.Deserialize<Hint>(trimmed, options);
            }
            catch (JsonException ex)
            {
                throw new HintException("Failed to deserialize Hint from JSON.", ex);
            }

            if (hint == null)
                throw new HintException("Hint did not deserialized properly!");
            else
                return hint;
        }

        public override string ToString()
        {
            return $"{Word} | {NoumberOfSimilarWords}";
        }

        public string listToString(List<string> list)
        {
            StringBuilder sb = new();


            foreach (string item in list)
            {
                sb.AppendLine(item);
            }

            return sb.ToString();
        }

        [Serializable]
        public class HintException : Exception
        {
            public HintException() { }
            public HintException(string message) : base(message) { }
            public HintException(string message, Exception inner) : base(message, inner) { }
        }
    }
}
