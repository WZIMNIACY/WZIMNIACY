using System.Threading.Tasks;

namespace AI
{
    public interface ILLM
    {
        Task<string> SendRequestAsync(string systemPrompt, string userPrompt, uint maxTokens = 256);
    }
}
