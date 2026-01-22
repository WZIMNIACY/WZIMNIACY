using System.Runtime.InteropServices;
using game;
using System.Reflection;
using hints;
using System.Runtime.CompilerServices;
using AI;


namespace Reaction
{

    public static class Reaction
    {

        public static async Task<string> create(ILLM llm, Hint hint, Card pickedCard, bool KapitnBomba, Team actualTour)
        {
            string reaction = string.Empty;

            string _pickedCard = pickedCard.toJson();
            string _givenHint = hint.toJson();
            bool _properGuess = actualTour == pickedCard.Team;

            var assembly = Assembly.GetExecutingAssembly();
            string systemPromptReactionRes = KapitnBomba
                ? "Reaction.ReactionPromptKapitanBomba.txt"
                : "Reaction.ReactionPrompt.txt";

            string systemPromptReaction;
            using (Stream? stream = assembly.GetManifestResourceStream(systemPromptReactionRes))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException("Couldn't find embeded resource");
                }

                using (StreamReader reader = new StreamReader(stream))
                {
                    systemPromptReaction = reader.ReadToEnd();
                }
            }

            string userPromptReaction = $"" +
                $"_pickedCard = {_pickedCard}\n" +
                $"_givenHint = {_givenHint}\n" +
                $"_properGuess = {_properGuess}";

            int try_idx = 0;
            while (try_idx < 5)
            {
                try_idx += 1;
                reaction = await llm.SendRequestAsync(systemPromptReaction, userPromptReaction);

                bool invalidReaction = string.IsNullOrWhiteSpace(reaction);

                if (invalidReaction)
                {
                    Console.WriteLine("Generated reaction is invalid, regenerating...");
                    continue;
                }
                else
                {

                    int start = reaction.IndexOf('{');
                    int end = reaction.LastIndexOf('}');

                    if (start == -1 || end == -1 || end < start)
                    {
                        Console.WriteLine("Generated reaction is invalid, regenerating...");
                        continue;
                    }

                    string trimmed = reaction.Substring(start, end - start + 1).Trim();

                    return trimmed;

                }
            }
            throw new ReactionException("Out of tries");
        }

        [System.Serializable]
        public class ReactionException : System.Exception
        {
            public ReactionException() { }
            public ReactionException(string message) : base(message) { }
            public ReactionException(string message, System.Exception inner) : base(message, inner) { }

        }

    }

}
