using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
using WordTrainingAssistant.Shared;

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
            List<KeyValuePair<string, string>> words = await Core.ParseFiles(dir);

            if (words.Any() == false)
            {
                PrintDefaultMsg("The list of words is empty.");
                return;
            }
            
            PrintNumberOfWords(words);

            List<KeyValuePair<string, string>> filteredWords = Core.GetRandomSetOfWords(count > words.Count
                    ? words.Count
                    : count,
                words);
            List<KeyValuePair<string, string>> errors = CheckAnswerAndPrintResult(filteredWords);
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

                CheckAnswerAndPrintResult(errors);
            }
        }

        private static void PrintNumberOfWords(List<KeyValuePair<string, string>> words)
        {
            PrintDefaultMsg($"Number of imported words: {words.Count}");
            Console.WriteLine();
        }

        private static void PrintStatistics(List<KeyValuePair<string, string>> filteredWords, List<KeyValuePair<string, string>> errors)
        {
            PrintDefaultMsg($"Correct answers: {filteredWords.Count - errors.Count}");
            PrintDefaultMsg($"Wrong answers: {errors.Count}");
            Console.WriteLine();
        }

        private static List<KeyValuePair<string, string>> CheckAnswerAndPrintResult(List<KeyValuePair<string, string>> filteredWords)
        {
            List<KeyValuePair<string, string>> errors = new();
            foreach (KeyValuePair<string, string> pair in filteredWords)
            {
                PrintDefaultMsg(pair.Value);
                string line = Console.ReadLine();
                if (Core.CheckAnswer(line, pair.Key))
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