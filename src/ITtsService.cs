using System.Threading.Tasks;

namespace UGTLive
{
    public interface ITtsService
    {
        Task<bool> SpeakText(string text);
        Task<string?> GenerateAudioFileAsync(string text, string voiceId);
    }
}
