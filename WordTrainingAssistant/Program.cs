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
            dirOption.AddValidator(result => { DirOptionValidator(dirOption, result); });

            Option<int> countOption = new("--count", description: "Number of words to be trained",
                getDefaultValue: () => 20);
            countOption.AddAlias("-c");

            Option<bool> offlineOption = new("--offline", description: "Offline mode", getDefaultValue: () => false);
            offlineOption.AddAlias("-o");

            Option<bool> useCacheOption = new("--useCache", description: "Use words cache", getDefaultValue: () => true);
            
            Option<FileSystemInfo> externalDictionaryOption = new("--externalDictionary", 
                description: "The path to the external dictionary file");
            externalDictionaryOption.AddAlias("-e");
            externalDictionaryOption.AddValidator(result => { FilePathOptionValidator(externalDictionaryOption, result);});

            Option<FileSystemInfo> driverOption = new("--driver", description: "Google Chrome browser driver");
            driverOption.AddAlias("-d");
            driverOption.AddValidator(result => DirOptionValidator(driverOption, result));

            Option<string> loginOption = new("--login", description: "Login for an SkyEng account");
            loginOption.AddAlias("-l");
            loginOption.AddValidator(result => StringOptionValidator(loginOption, nameof(loginOption), result));
            
            Option<string> passwordOption = new("--password", description: "Password for an SkyEng account");
            passwordOption.AddAlias("-p");
            passwordOption.AddValidator(result => StringOptionValidator(passwordOption, nameof(passwordOption), result));

            RootCommand rootCommand = new("SkyEng vocabulary training application.");
            rootCommand.AddOption(dirOption);
            rootCommand.AddOption(countOption);
            rootCommand.AddOption(offlineOption);
            rootCommand.AddOption(useCacheOption);
            rootCommand.AddOption(externalDictionaryOption);
            rootCommand.AddOption(driverOption);
            rootCommand.AddOption(loginOption);
            rootCommand.AddOption(passwordOption);

            rootCommand.SetHandler(async context =>
                {
                    FileSystemInfo dir = context.ParseResult.GetValueForOption(dirOption);
                    int count = context.ParseResult.GetValueForOption(countOption);
                    bool offline = context.ParseResult.GetValueForOption(offlineOption);
                    bool cache = context.ParseResult.GetValueForOption(useCacheOption);
                    FileSystemInfo externalDictionary = context.ParseResult.GetValueForOption(externalDictionaryOption);
                    FileSystemInfo driver = context.ParseResult.GetValueForOption(driverOption);
                    string login = context.ParseResult.GetValueForOption(loginOption);
                    string password = context.ParseResult.GetValueForOption(passwordOption);

                    await Run(dir,
                        count,
                        offline,
                        cache,
                        externalDictionary,
                        driver,
                        login,
                        password);
                });

            return await rootCommand.InvokeAsync(args);
        }

        private static void StringOptionValidator(Option<string> option, string name, OptionResult result)
        {
            string errorMessage = $"The option {name} cannot be blank.";
            string value = result.GetValueForOption(option);

            if (String.IsNullOrWhiteSpace(value))
            {
                result.ErrorMessage = errorMessage;
            }
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
        
        private static void FilePathOptionValidator(Option<FileSystemInfo> pathOption, OptionResult result)
        {
            FileSystemInfo fileSystemInfo = result.GetValueForOption(pathOption);
            const string errorMessage = "You need to specify the path to the existing file.";
            
            if (fileSystemInfo == null)
            {
                result.ErrorMessage = errorMessage;
            }
            
            if (fileSystemInfo is not FileInfo)
            {
                result.ErrorMessage = errorMessage;
            }

            if (File.Exists(fileSystemInfo!.FullName) == false)
            {
                result.ErrorMessage = errorMessage;
            }
        }

        private static async Task Run(FileSystemInfo dir, int count, bool offline, bool cache,
            FileSystemInfo externalDictionary, FileSystemInfo driver, string login, string password)
        {
            Console.Clear();
            List<Word> words;
            if (new[]{ driver?.FullName ?? "", login, password}.Any(String.IsNullOrWhiteSpace))
            {
                words = cache
                    ? await ReadCache() ?? GetWords(await Core.ParseFiles(dir, externalDictionary))
                    : GetWords(await Core.ParseFiles(dir, externalDictionary));
            }
            else
            {
                words = cache
                    ? await ReadCache() ?? GetWords(await Core.GetWordsFromSite(driver!.FullName, login, password, externalDictionary))
                    : GetWords(await Core.GetWordsFromSite(driver!.FullName, login, password, externalDictionary));
            }
            
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
            if (cache)
            {
                PrintPreviouslyRepeatedWordsCount(words);
            }

            words.Shuffle();
            List<Word> trainSet = GetTrainSet(count, words);

            if (offline == false)
            {
                await EnrichWithSynonyms(trainSet);
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

        private static async Task<List<Word>> ReadCache()
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

        private static async Task EnrichWithSynonyms(List<Word> filteredWords)
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