using System;
using System.Collections.Generic;

namespace WordTrainingAssistant.Shared.Models
{
    [Serializable]public class Word
    {
        public string Name { get; set; } = "";
        public string Transcription { get; set; } = "";
        public string Translation { get; set; } = "";
        public List<Word> Synonyms { get; set; } = new();

        public DateTime DateTime { get; set; }

        public bool IsRepeatedToday => this.DateTime == DateTime.Today;

        private sealed class NameTranslationEqualityComparer : IEqualityComparer<Word>
        {
            public bool Equals(Word x, Word y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;
                return x.Name == y.Name && x.Translation == y.Translation;
            }

            public int GetHashCode(Word obj)
            {
                return HashCode.Combine(obj.Name, obj.Translation);
            }
        }

        public static IEqualityComparer<Word> NameTranslationComparer { get; } = new NameTranslationEqualityComparer();
    }
}