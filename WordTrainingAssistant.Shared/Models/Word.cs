using System;
using System.Collections.Generic;

namespace WordTrainingAssistant.Shared.Models
{
    [Serializable]public class Word
    {
        public string name { get; set; } = "";
        public string translation { get; set; } = "";

        public List<string> sentences { get; set; } = new();

        public List<string> synonyms { get; set; } = new();

        public DateTime dateTime { get; set; }

        public bool isRepeatedToday => dateTime == DateTime.Today;

        private sealed class NameTranslationEqualityComparer : IEqualityComparer<Word>
        {
            public bool Equals(Word x, Word y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (ReferenceEquals(x, null)) return false;
                if (ReferenceEquals(y, null)) return false;
                if (x.GetType() != y.GetType()) return false;
                return x.name == y.name && x.translation == y.translation;
            }

            public int GetHashCode(Word obj)
            {
                return HashCode.Combine(obj.name, obj.translation);
            }
        }
    }
}