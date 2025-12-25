using System;

namespace UGTLive
{
    /// <summary>
    /// Static class to track translation status for UI updates.
    /// Updated by translation services, read by UI timers.
    /// </summary>
    public static class TranslationStatus
    {
        private static readonly object _lock = new object();
        
        // Current token count being received
        private static int _tokenCount = 0;
        
        // Whether the model is currently in "thinking" mode
        private static bool _isThinking = false;
        
        // Whether streaming is active
        private static bool _isStreaming = false;
        
        /// <summary>
        /// Current token count received during streaming
        /// </summary>
        public static int TokenCount
        {
            get { lock (_lock) { return _tokenCount; } }
            set { lock (_lock) { _tokenCount = value; } }
        }
        
        /// <summary>
        /// Whether the model is currently outputting thinking/reasoning content
        /// </summary>
        public static bool IsThinking
        {
            get { lock (_lock) { return _isThinking; } }
            set { lock (_lock) { _isThinking = value; } }
        }
        
        /// <summary>
        /// Whether streaming is currently active
        /// </summary>
        public static bool IsStreaming
        {
            get { lock (_lock) { return _isStreaming; } }
            set { lock (_lock) { _isStreaming = value; } }
        }
        
        /// <summary>
        /// Reset all status values (call when starting a new translation)
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _tokenCount = 0;
                _isThinking = false;
                _isStreaming = false;
            }
        }
        
        /// <summary>
        /// Start streaming mode
        /// </summary>
        public static void StartStreaming(bool isThinkingModel = false)
        {
            lock (_lock)
            {
                _tokenCount = 0;
                _isThinking = isThinkingModel;
                _isStreaming = true;
            }
        }
        
        /// <summary>
        /// Stop streaming mode
        /// </summary>
        public static void StopStreaming()
        {
            lock (_lock)
            {
                _isStreaming = false;
                _isThinking = false;
            }
        }
        
        /// <summary>
        /// Increment token count (thread-safe)
        /// </summary>
        public static void IncrementTokenCount(int count = 1)
        {
            lock (_lock)
            {
                _tokenCount += count;
            }
        }
    }
}

