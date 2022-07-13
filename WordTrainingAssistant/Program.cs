using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Konsole;
using Newtonsoft.Json;
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
            Option<int> countOption = new("--count", description: "Number of words to be trained",
                getDefaultValue: () => 20);
            countOption.AddAlias("-c");

            Option<bool> offlineOption = new("--offline", description: "Offline mode", getDefaultValue: () => false);
            offlineOption.AddAlias("-o");

            Option<bool> useCacheOption = new("--useCache", description: "Use words cache", getDefaultValue: () => true);
            
            Option<FileSystemInfo> dictionaryOption = new("--dictionary", 
                description: "The path to the external dictionary file");
            dictionaryOption.AddAlias("-e");
            dictionaryOption.AddValidator(result => { FilePathOptionValidator(dictionaryOption, result);});

            Option<FileSystemInfo> driverOption = new("--driver", description: "Google Chrome browser driver");
            driverOption.AddAlias("-d");
            driverOption.AddValidator(result => DirOptionValidator(driverOption, result));
            driverOption.IsRequired = true;

            Option<string> loginOption = new("--login", description: "Login for an SkyEng account");
            loginOption.AddAlias("-l");
            loginOption.AddValidator(result => StringOptionValidator(loginOption, nameof(loginOption), result));
            loginOption.IsRequired = true;
            
            Option<string> passwordOption = new("--password", description: "Password for an SkyEng account");
            passwordOption.AddAlias("-p");
            passwordOption.AddValidator(result => StringOptionValidator(passwordOption, nameof(passwordOption), result));
            passwordOption.IsRequired = true;

            Option<string> studentIdOption = new("--student", description: "Student id");
            studentIdOption.AddAlias("-s");
            studentIdOption.IsRequired = true;

            Option<bool> audioOption = new("--audio", description: "Enable audio pronunciation", getDefaultValue: () => false);
            audioOption.AddAlias("-a");

            RootCommand rootCommand = new("SkyEng vocabulary training application.");
            rootCommand.AddOption(countOption);
            rootCommand.AddOption(offlineOption);
            rootCommand.AddOption(useCacheOption);
            rootCommand.AddOption(dictionaryOption);
            rootCommand.AddOption(driverOption);
            rootCommand.AddOption(loginOption);
            rootCommand.AddOption(passwordOption);
            rootCommand.AddOption(studentIdOption);
            rootCommand.AddOption(audioOption);

            rootCommand.SetHandler(async context =>
                {
                    int count = context.ParseResult.GetValueForOption(countOption);
                    bool offline = context.ParseResult.GetValueForOption(offlineOption);
                    bool cache = context.ParseResult.GetValueForOption(useCacheOption);
                    FileSystemInfo dictionary = context.ParseResult.GetValueForOption(dictionaryOption);
                    FileSystemInfo driver = context.ParseResult.GetValueForOption(driverOption);
                    string login = context.ParseResult.GetValueForOption(loginOption);
                    string password = context.ParseResult.GetValueForOption(passwordOption);
                    string id = context.ParseResult.GetValueForOption(studentIdOption);
                    bool audio = context.ParseResult.GetValueForOption(audioOption);

                    await Run(count,
                        offline,
                        cache,
                        dictionary,
                        driver,
                        login,
                        password, 
                        id, 
                        audio);
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

        private static async Task Run(int count, bool offline, bool cache,
            FileSystemInfo dictionary, FileSystemInfo driver, string login, string password, string studentId,
            bool audio)
        {
            Console.Clear();
            List<Word> words = cache
                ? await ReadCache() ?? GetWords(await Core.GetWordsFromSite(login, password, studentId, driver!.FullName, dictionary))
                : GetWords(await Core.GetWordsFromSite(login, password, studentId, driver!.FullName, dictionary));
            
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
                await EnrichWithSentencesAndSynonyms(trainSet);
            }

            List<Word> errors = await CheckAnswerAndPrintResult(trainSet, audio);
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

                await CheckAnswerAndPrintResult(errors, audio);
            }
        }

        private static async Task EnrichWithSentencesAndSynonyms(List<Word> words)
        {
            if (!Core.CheckForInternetConnection())
            {
                return;
            }

            HttpClient client = new();
            ProgressBar progressBar = new(_window, PbStyle.SingleLine, words.Count);

            foreach (Word word in words)
            {
                progressBar.Next(word.translation);
                await AddSentences(client, word);
                await AddsSynonyms(client, word);
            }
        }

        private static async Task AddsSynonyms(HttpClient client, Word word)
        {
            HttpResponseMessage response = await client
                .GetAsync($"https://sentencestack.com/q/{word.name}");
            if (response.IsSuccessStatusCode == false)
            {
                return;
            }
            string source = await response.Content.ReadAsStringAsync();
            HtmlParser parser = new();
            IHtmlDocument document = await parser.ParseDocumentAsync(source);
            IHtmlCollection<IElement> elements = document.QuerySelectorAll("div.synonym");
            List<string> synonyms = elements.Select(element => element.QuerySelector("a")?.Text()).Distinct().ToList();
            word.synonyms = synonyms;
        }

        private static async Task AddSentences(HttpClient client, Word word)
        {
            HttpResponseMessage response = await client
                .GetAsync($"https://sentencestack.com/q/{word.name}");
            if (response.IsSuccessStatusCode == false)
            {
                return;
            }
            string source = await response.Content.ReadAsStringAsync();

            HtmlParser parser = new();
            IHtmlDocument document = await parser.ParseDocumentAsync(source);
            IHtmlCollection<IElement> elements = document.QuerySelectorAll("div.sentence");
            List<string> sentences = elements.Take(5).Select(element => $"\"{element.Text().Trim()}\"").ToList();
            word.sentences = sentences;
        }

        private static void PrintPreviouslyRepeatedWordsCount(List<Word> words)
        {
            _window.WriteLine(ConsoleColor.White, $"Previously repeated words: {words.Count(word => word.isRepeatedToday)}");
        }

        private static List<Word> GetWords(List<KeyValuePair<string, string>> parseResult)
        {
            return parseResult.Select(pair => new Word
                {
                    name = pair.Key,
                    translation = pair.Value
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
            return (words ?? new List<Word>()).ToList();
        }

        private static async Task SaveWords(List<Word> words)
        {
            string serializeObject = JsonConvert.SerializeObject(words);
            await File.WriteAllTextAsync(WordsPath, serializeObject);
        }

        private static List<Word> GetTrainSet(int count, List<Word> words)
        {
            List<Word> trainSet = words.Where(word => word.isRepeatedToday == false).Take(count).ToList();
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
        
        private static async Task<List<Word>> CheckAnswerAndPrintResult(List<Word> filteredObjects, bool audio)
        {
            List<Word> errors = new();
            foreach (Word word in filteredObjects)
            {
                PrintDefaultMsg(word.translation);
                string userInput = Console.ReadLine();
                if (await Core.CheckAnswer(userInput, word, audio))
                {
                    word.dateTime = DateTime.Today;

                    PrintSuccessMsg("SUCCESS");
                    PrintSynonyms2(word);
                    PrintSentences(word);
                    Console.WriteLine("");
                }
                else
                {
                    errors.Add(word);
                    PrintErrorMsg("FAIL");
                    PrintErrorMsg($"Right answer is: {word.name}");
                    PrintSynonyms2(word);
                    PrintSentences(word);

                    Console.WriteLine("");
                }
            }

            return errors;
        }

        private static void PrintSentences(Word word)
        {
            if (word.sentences.Any() == false)
            {
                return;
            }

            PrintAdditionalInfo($"Example sentences containing {word.name.ToUpperInvariant()}:");
            foreach (string sentence in word.sentences)
            {
                PrintAdditionalInfo(sentence);
            }
        }
        
        private static void PrintSynonyms2(Word word)
        {
            if (word.synonyms.Any() == false)
            {
                return;
            }

            PrintAdditionalInfo("Synonyms:");
            StringBuilder sb = new();
            foreach (string synonym in word.synonyms)
            {
                sb.Append($"{synonym}, ");
            }
            PrintAdditionalInfo(sb.ToString().Trim().TrimEnd(','));
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