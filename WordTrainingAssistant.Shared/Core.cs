using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;

namespace WordTrainingAssistant.Shared
{
    public static class Core
    {
        
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

                var request = (HttpWebRequest)WebRequest.Create(url);
                request.KeepAlive = false;
                request.Timeout = timeoutMs;
                using var response = (HttpWebResponse)request.GetResponse();
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        public static List<KeyValuePair<string, string>> GetRandomSetOfWords(int count, 
            List<KeyValuePair<string, string>> words)
        {
            Random random = new(DateTime.Now.ToString(CultureInfo.InvariantCulture).GetHashCode());

            List<KeyValuePair<string, string>> filteredWords = new();
            for (int i = 0; i < count; i++)
            {
                string key;
                KeyValuePair<string, string> valuePair;
                do
                {
                    int index = random.Next(0, words.Count);
                    valuePair = words[index];
                    key = valuePair.Key;
                } while (filteredWords.Count(pair => pair.Key.Equals(key)) is not 0);

                filteredWords.Add(valuePair);
            }

            return filteredWords;
        }

        public static bool CheckAnswer(string line, string value)
        {
            return line == null || line.Equals(value, StringComparison.InvariantCultureIgnoreCase);
        }

        public static async Task<List<KeyValuePair<string, string>>> ParseFiles(string dir)
        {
            IConfiguration config = Configuration.Default;
            IBrowsingContext context = BrowsingContext.New(config);
            string[] files = Directory.GetFiles(dir);

            List<KeyValuePair<string, string>> words = new();
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

                        if (new[] {originalWord, translation}.All(s => String.IsNullOrWhiteSpace(s) == false))
                            words.Add(new KeyValuePair<string, string>(originalWord, translation));
                    }
                }
            }

            return words;
        }
    }
}