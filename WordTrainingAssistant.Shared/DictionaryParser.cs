using System.Collections.Generic;
using System.Linq;
using Sprache;

namespace WordTrainingAssistant.Shared
{
    public static class DictionaryParser
    {
        private static Parser<KeyValuePair<string, string>> DictionaryRow =>
            from name in Parse.CharExcept(':').Many().Text()
            from separator in Parse.Char(':')
            from translation in Parse.AnyChar.Except(Parse.LineTerminator).Many().Text()
            select new KeyValuePair<string, string>(name, translation);

        private static Parser<KeyValuePair<string, string>[]> FullDictionary =>
            from rows in DictionaryRow.DelimitedBy(Parse.LineTerminator)
            select rows.ToArray();

        public static KeyValuePair<string, string>[] ParseDictionary(string text) => FullDictionary.Parse(text);
    }
}