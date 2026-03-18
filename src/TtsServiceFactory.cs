using System;

namespace UGTLive
{
    public static class TtsServiceFactory
    {
        public static ITtsService CreateService()
        {
            string currentService = ConfigManager.Instance.GetTtsService();
            return CreateService(currentService);
        }

        public static ITtsService CreateService(string serviceName)
        {
            return serviceName switch
            {
                "Google Cloud TTS" => GoogleTTSService.Instance,
                "Qwen3-TTS" => Qwen3TtsService.Instance,
                _ => ElevenLabsService.Instance
            };
        }

        public static bool IsLocalService(string serviceName)
        {
            return serviceName == "Qwen3-TTS";
        }
    }
}
