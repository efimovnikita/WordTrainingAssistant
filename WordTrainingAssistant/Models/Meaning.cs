namespace WordTrainingAssistant.Models
{
    public class Meaning
    {
        public int id { get; set; }
        public string partOfSpeechCode { get; set; }
        public Translation translation { get; set; }
        public string previewUrl { get; set; }
        public string imageUrl { get; set; }
        public string transcription { get; set; }
        public string soundUrl { get; set; }
    }
}