using System;

namespace AI
{
    public class NoInternetException : Exception
    {
        public NoInternetException(string message, Exception inner)
            : base(message, inner) { }
    }

    public class NoTokensException : Exception { }

    public class InvalidApiKeyException : Exception { }

    public class RateLimitException : Exception { }

    public class ApiException : Exception
    {
        public ApiException(string message) : base(message) { }
    }
}
