using System.Reflection;
using AI;
using game;
using hints;

namespace AIPlayer
{
    public class LLMPlayer
    {
        private readonly ILLM _llm;

        public LLMPlayer(ILLM llm)
        {
            _llm = llm;
        }

        //AI player get Deck and hint, returns card which is his pick
        public async Task<Card> PickCardFromDeck(Deck deck, Hint hint, bool tests = false)
        {
            var assembly = Assembly.GetExecutingAssembly();
            string hintPromptRes = "LLMPlayer.LLMPlayerPrompt.txt";
            string systemPromptHint;
            using (Stream? stream = assembly.GetManifestResourceStream(hintPromptRes))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException("Couldn't find embeded resource LLM.LLMPlayerPrompt.txt");
                }

                using (StreamReader reader = new StreamReader(stream))
                {
                    systemPromptHint = reader.ReadToEnd();
                }
            }

            string userPromptHint = $"_deck = {deck.ToJson()}\n_hint = {hint}\n";
            while (true)
            {
                try
                {
                    Console.WriteLine("\nLLMPlayer response generating...\n");
                    string response = await _llm.SendRequestAsync(systemPromptHint, userPromptHint, 256);
                    if (tests)
                    {
                        Console.WriteLine(response);
                    }
                    Card? chosenCard = Card.fromJson(response);

                    if (chosenCard != null)
                    {
                        bool cardInDeck = deck.Cards.Any(item => chosenCard.Word == item.Word);
                        if (cardInDeck)
                        {
                            return chosenCard;
                        }
                        else
                        {
                            Console.WriteLine("Chosen card is not in deck, regenerating...");
                            continue;
                        }
                    }
                    else
                    {
                        Console.WriteLine("Failed to parse chosen card from response, regenerating...");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to parse hint response: {ex.Message}, regenerating...");
                    continue;
                }
            }
        }
    }
}