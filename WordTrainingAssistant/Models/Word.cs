using System.Collections.Generic;

namespace WordTrainingAssistant.Models
{
    public class Word
    {
        public string Name { get; set; }
        public string Transcription { get; set; }
        public string Translation { get; set; }
        public List<Word> Synonyms { get; set; }
    }
}