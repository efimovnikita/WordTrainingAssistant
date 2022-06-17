using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;

namespace WordTrainingAssistant
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            Option<string> dirOption = new("--dir", "Path to SkyEng dictionary pages folder");
            dirOption.AddAlias("-d");
            dirOption.IsRequired = true;

            Option<int> countOption = new("--count", description: "Number of words to be trained",
                getDefaultValue: () => 19);
            countOption.AddAlias("-c");

            RootCommand rootCommand = new("SkyEng vocabulary training application.");
            rootCommand.AddOption(dirOption);
            rootCommand.AddOption(countOption);

            rootCommand.SetHandler(async (dir, count) => { await Run(dir, count); }, dirOption, countOption);

            return await rootCommand.InvokeAsync(args);
        }

        private static async Task Run(string dir, int count)
        {
            List<KeyValuePair<string, string>> words = await ParseFiles(dir);

            if (words.Any() == false)
            {
                PrintDefaultMsg("The list of words is empty.");
                return;
            }
            
            List<KeyValuePair<string, string>> filteredWords = GetRandomSetOfWords(count, words);
            List<KeyValuePair<string, string>> errors = CheckAnswer(filteredWords);
            PrintStatistics(filteredWords, errors);

            if (errors.Any())
            {
                PrintDefaultMsg("Repeat the words in which mistakes were made? (y/n)");
                string answer = Console.ReadLine();
                Console.WriteLine();
                if (answer != "y")
                {
                    return;
                }

                CheckAnswer(errors);
            }
        }

        private static void PrintStatistics(List<KeyValuePair<string, string>> filteredWords, List<KeyValuePair<string, string>> errors)
        {
            PrintDefaultMsg($"Correct answers: {filteredWords.Count - errors.Count}");
            PrintDefaultMsg($"Wrong answers: {errors.Count}");
            Console.WriteLine();
        }

        private static List<KeyValuePair<string, string>> GetRandomSetOfWords(int count, 
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

        private static List<KeyValuePair<string, string>> CheckAnswer(List<KeyValuePair<string, string>> filteredWords)
        {
            List<KeyValuePair<string, string>> errors = new();
            foreach (KeyValuePair<string, string> pair in filteredWords)
            {
                PrintDefaultMsg(pair.Value);
                string line = Console.ReadLine();
                if (line == null || line.Equals(pair.Key, StringComparison.InvariantCultureIgnoreCase))
                {
                    PrintSuccessMsg("SUCCESS");
                    Console.WriteLine("");
                }
                else
                {
                    errors.Add(pair);
                    PrintErrorMsg("FAIL");
                    PrintErrorMsg($"Right answer is: {pair.Key}");
                    Console.WriteLine("");
                }
            }

            return errors;
        }

        private static async Task<List<KeyValuePair<string, string>>> ParseFiles(string dir)
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

        private static void PrintDefaultMsg(string msg)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(msg);
        }

        private static void PrintErrorMsg(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(msg);
        }

        private static void PrintSuccessMsg(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(msg);
        }
    }
}