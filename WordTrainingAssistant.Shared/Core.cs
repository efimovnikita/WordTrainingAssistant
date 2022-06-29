using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using WordTrainingAssistant.Shared.Models;

namespace WordTrainingAssistant.Shared
{
    public static class Core
    {
        public static void Shuffle<T>(this IList<T> list)
        {
            Random rng = new();
            int n = list.Count;  
            while (n > 1) {  
                n--;  
                int k = rng.Next(n + 1);  
                (list[k], list[n]) = (list[n], list[k]);
            }  
        }
        public static bool CheckForInternetConnection(int timeoutMs = 10000, string url = null)
        {
            try
            {
                url ??= CultureInfo.InstalledUICulture switch
                {
                    { Name: var n } when n.StartsWith("fa") => // Iran
                        "http://www.aparat.com",
                    { Name: var n } when n.StartsWith("zh") => // China
                        "http://www.baidu.com",
                    _ =>
                        "http://www.gstatic.com/generate_204",
                };

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.KeepAlive = false;
                request.Timeout = timeoutMs;
                using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool CheckAnswer(string userInput, Word word)
        {
            bool equals = userInput == null || userInput.Equals(word.Name, StringComparison.InvariantCultureIgnoreCase);

            if (equals)
            {
                return true;
            }

            if (word.Synonyms.Count == 0)
            {
                return false;
            }

            return word.Synonyms.Where(synonym => synonym.Name.Equals(userInput, StringComparison.InvariantCultureIgnoreCase))
                .ToList().Any();
        }

        public static async Task<List<KeyValuePair<string, string>>> ParseFiles(string dir, Direction direction,
            FileSystemInfo externalDictionary)
        {
            List<KeyValuePair<string, string>> words = await ParseWebPage(dir, direction);
            words.AddRange(await ParseDictionary(externalDictionary, direction));

            return words;
        }

        private static async Task<KeyValuePair<string, string>[]> ParseDictionary(FileSystemInfo externalDictionary,
            Direction direction)
        {
            if (externalDictionary == null)
            {
                return Array.Empty<KeyValuePair<string,string>>();
            }

            string rawText = await File.ReadAllTextAsync(externalDictionary.FullName);
            return DictionaryParser.ParseDictionary(rawText, direction: direction);
        }

        private static async Task<List<KeyValuePair<string, string>>> ParseWebPage(string dir, Direction direction)
        {
            List<KeyValuePair<string, string>> words = new();

            IConfiguration config = Configuration.Default;
            IBrowsingContext context = BrowsingContext.New(config);
            string[] files = Directory.GetFiles(dir);

            foreach (string file in files)
            {
                string source = await File.ReadAllTextAsync(file);
                IDocument document = await context.OpenAsync(req => req.Content(source));

                IHtmlCollection<IElement> wordSets = document.QuerySelectorAll("div.wordset");

                foreach (IElement wordSet in wordSets)
                {
                    IHtmlCollection<IElement> liElements = wordSet.QuerySelectorAll("li");

                    foreach (IElement liElement in liElements)
                    {
                        string originalWord =
                            liElement.QuerySelectorAll("div.original > span.text").FirstOrDefault()?.TextContent
                                .Trim().Replace('’', '\'') ?? "";
                        string translation =
                            liElement.QuerySelectorAll("div.translation").FirstOrDefault()?.TextContent.Trim() ??
                            "";

                        if (new[] { originalWord, translation }.All(s => String.IsNullOrWhiteSpace(s) == false))
                        {
                            if (direction is Direction.RuEn)
                            {
                                words.Add(new KeyValuePair<string, string>(originalWord, translation));
                                continue;
                            }

                            words.Add(new KeyValuePair<string, string>(translation, originalWord));
                        }
                    }
                }
            }

            return words;
        }
    }
}