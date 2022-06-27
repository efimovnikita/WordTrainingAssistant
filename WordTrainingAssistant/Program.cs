using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Konsole;
using Newtonsoft.Json;
using WordTrainingAssistant.Models;
using WordTrainingAssistant.Shared;
using WordTrainingAssistant.Shared.Models;
using IConsole = Konsole.IConsole;

namespace WordTrainingAssistant
{
    internal static class Program
    {
        private static IConsole _window;
        private const string WordsPath = "words.txt";

        private static async Task<int> Main(string[] args)
        {
            Option<FileSystemInfo> dirOption = new("--dir", "Path to SkyEng dictionary pages folder");
            dirOption.AddAlias("-d");
            dirOption.IsRequired = true;
            dirOption.AddValidator(result => { DirOptionValidator(dirOption, result); });

            Option<int> countOption = new("--count", description: "Number of words to be trained",
                getDefaultValue: () => 20);
            countOption.AddAlias("-c");

            Option<bool> offlineOption = new("--offline", description: "Offline mode", getDefaultValue: () => false);
            offlineOption.AddAlias("-o");

            Option<bool> useCacheOption = new("--useCache", description: "Use words cache", getDefaultValue: () => false);

            Option<Direction> directionOption = new("--direction", description: "The direction of word translation",
                getDefaultValue: () => Direction.RuEn);

            RootCommand rootCommand = new("SkyEng vocabulary training application.");
            rootCommand.AddOption(dirOption);
            rootCommand.AddOption(countOption);
            rootCommand.AddOption(offlineOption);
            rootCommand.AddOption(directionOption);
            rootCommand.AddOption(useCacheOption);

            rootCommand.SetHandler(async (dir, count, offline, direction, cache) => { await Run(dir.FullName, count, offline, direction, cache); },
                dirOption, countOption, offlineOption, directionOption, useCacheOption);

            return await rootCommand.InvokeAsync(args);
        }

        private static void DirOptionValidator(Option<FileSystemInfo> dirOption, OptionResult result)
        {
            FileSystemInfo fileSystemInfo = result.GetValueForOption(dirOption);
            const string errorMessage = "You need to specify the path to the existing folder.";
            if (fileSystemInfo is { Exists: false })
            {
                result.ErrorMessage = errorMessage;
            }

            if (fileSystemInfo is FileInfo)
            {
                result.ErrorMessage = errorMessage;
            }
        }

        private static async Task Run(string dir, int count, bool offline, Direction direction, bool cache)
        {
            Console.Clear();
            List<KeyValuePair<string, string>> parseResult = await Core.ParseFiles(dir, direction);

            List<Word> words = cache == false
                ? GetWords(parseResult)
                : await DeserializeObject() ?? GetWords(parseResult);
            
            if (words == null || words.Any() == false)
            {
                Console.WriteLine();
                PrintDefaultMsg("The list of words is empty.");
                return;
            }
            
            if (count > words.Count)
            {
                count = words.Count;
            }

            Console.WriteLine();
            _window = Window.OpenBox("Vocabulary training application", 80, 7);
            _window.WriteLine($"Words for training: {count}");

            PrintNumberOfWords(words);
            PrintPreviouslyRepeatedWordsCount(words);

            words.Shuffle();
            List<Word> trainSet = GetTrainSet(count, words);

            if (offline == false)
            {
                await EnrichWithSynonyms(trainSet, direction);
            }

            List<Word> errors = CheckAnswerAndPrintResult(trainSet);
            PrintStatistics(trainSet, errors);

            await SaveWords(words);

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
            
            await SaveWords(words);
        }

        private static void PrintPreviouslyRepeatedWordsCount(List<Word> words)
        {
            _window.WriteLine(ConsoleColor.White, $"Previously repeated words: {words.Count(word => word.IsRepeatedToday)}");
        }

        private static List<Word> GetWords(List<KeyValuePair<string, string>> parseResult)
        {
            return parseResult.Distinct().Select(pair => new Word
                {
                    Name = pair.Key,
                    Translation = pair.Value
                })
                .ToList();
        }

        private static async Task<List<Word>> DeserializeObject()
        {
            if (File.Exists(WordsPath) == false)
            {
                return null;
            }
            string text = await File.ReadAllTextAsync(WordsPath);
            List<Word> words = JsonConvert.DeserializeObject<List<Word>>(text);
            return (words ?? new List<Word>()).Distinct(Word.NameTranslationComparer).ToList();
        }

        private static async Task SaveWords(List<Word> words)
        {
            string serializeObject = JsonConvert.SerializeObject(words);
            await File.WriteAllTextAsync(WordsPath, serializeObject);
        }

        private static List<Word> GetTrainSet(int count, List<Word> words)
        {
            List<Word> trainSet = words.Where(word => word.IsRepeatedToday == false).Take(count).ToList();
            if (trainSet.Count >= count)
            {
                return trainSet;
            }

            int i = count - trainSet.Count;
            trainSet.AddRange(words.Take(i));

            return trainSet;
        }

        private static void PrintNumberOfWords(List<Word> words)
        {
            _window.WriteLine(ConsoleColor.White, $"Imported words: {words.Count}");
        }

        private static async Task EnrichWithSynonyms(List<Word> filteredWords, Direction direction)
        {
            if (Core.CheckForInternetConnection())
            {
                ProgressBar progressBar = new(_window, PbStyle.SingleLine, filteredWords.Count);
                HttpClient client = new();
                foreach (Word word in filteredWords)
                {
                    progressBar.Next(word.Translation);
                    HttpResponseMessage response = await client
                        .GetAsync($"https://dictionary.skyeng.ru/api/public/v1/words/search?search={word.Translation}");

                    if (response.IsSuccessStatusCode == false)
                    {
                        continue;
                    }

                    string stringAsync = await response.Content.ReadAsStringAsync();
                    SkyEngClass[] skyEngClasses = JsonConvert.DeserializeObject<SkyEngClass[]>(stringAsync);
                    GetAnotherWords(skyEngClasses, word);
                }
            }

            Console.WriteLine();
        }

        private static void GetAnotherWords(SkyEngClass[] skyEngClasses, Word word)
        {
            if (skyEngClasses == null)
            {
                return;
            }

            List<SkyEngClass> list = skyEngClasses.Skip(1).ToList();
            List<Word> similarWords = list.Select(cl => new Word
                {
                    Name = cl.text,
                    Translation = cl.meanings[0]
                                      ?.translation?.text ??
                                  word.Translation
                })
                .ToList();
            word.Synonyms = similarWords;
        }

        private static List<Word> CheckAnswerAndPrintResult(List<Word> filteredObjects)
        {
            List<Word> errors = new();
            foreach (Word word in filteredObjects)
            {
                PrintDefaultMsg(word.Translation);
                string userInput = Console.ReadLine();
                if (Core.CheckAnswer(userInput, word))
                {
                    word.DateTime = DateTime.Today;

                    PrintSuccessMsg("SUCCESS");
                    PrintSynonyms(word);
                    Console.WriteLine("");
                }
                else
                {
                    errors.Add(word);
                    PrintErrorMsg("FAIL");
                    PrintErrorMsg($"Right answer is: {word.Name}");
                    PrintSynonyms(word);

                    Console.WriteLine("");
                }
            }

            return errors;
        }

        private static void PrintSynonyms(Word word)
        {
            if (word.Synonyms.Any() == false)
            {
                return;
            }

            StringBuilder sb = new();
            sb.Append("Synonyms of this word: ");
            foreach (Word synonym in word.Synonyms.Where(w => w.Name.Equals(word.Name) == false))
            {
                sb.Append($"[{synonym.Name} - {synonym.Translation}], ");
            }

            PrintAdditionalInfo(sb.ToString().Substring(0, sb.ToString().Length - 2));
        }

        private static void PrintAdditionalInfo(string message)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"{message}");
        }

        private static void PrintStatistics(List<Word> filteredWords, List<Word> errors)
        {
            PrintDefaultMsg($"Correct answers: {filteredWords.Count - errors.Count}");
            PrintDefaultMsg($"Wrong answers: {errors.Count}");
            Console.WriteLine();
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