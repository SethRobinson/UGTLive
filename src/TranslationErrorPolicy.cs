using System;

namespace UGTLive
{
    /// <summary>
    /// Lets a translation service tell the retry loop "this error will never
    /// succeed on retry" (HTTP 400/401/403/404/etc - bad model slug, bad key,
    /// malformed request). The loop checks this and stops immediately instead of
    /// hammering the same fatal request 16 times.
    /// </summary>
    public static class TranslationErrorPolicy
    {
        private static volatile bool _abort;
        private static string _reason = "";

        public static bool AbortRetries => _abort;
        public static string Reason => _reason;

        public static void Reset()
        {
            _abort = false;
            _reason = "";
        }

        public static void SignalNonRetryable(string reason)
        {
            _abort = true;
            _reason = reason;
            Console.WriteLine($"[TranslationErrorPolicy] Non-retryable error ({reason}) - aborting further retries.");
        }

        /// <summary>
        /// True for client errors that won't change on retry. 408 (timeout) and
        /// 429 (rate limit) are intentionally treated as retryable.
        /// </summary>
        public static bool IsNonRetryableStatus(System.Net.HttpStatusCode status)
        {
            int code = (int)status;
            if (code == 408 || code == 429) return false;
            return code >= 400 && code < 500;
        }
    }
}
