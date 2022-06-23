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

            RootCommand rootCommand = new("SkyEng vocabulary training application.");
            rootCommand.AddOption(dirOption);
            rootCommand.AddOption(countOption);
            rootCommand.AddOption(offlineOption);

            rootCommand.SetHandler(async (dir, count, offline) => { await Run(dir.FullName, count, offline); }, dirOption, countOption, offlineOption);

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

        private static async Task Run(string dir, int count, bool offline)
        {
            Console.Clear();
            List<KeyValuePair<string, string>> words = await Core.ParseFiles(dir);

            if (words.Any() == false)
            {
                Console.WriteLine();
                PrintDefaultMsg("The list of words is empty.");
                return;
            }
            
            Console.WriteLine();
            _window = Window.OpenBox("Vocabulary training application", 80, 6);
            _window.WriteLine($"Words for training: {count}");

            PrintNumberOfWords(words);
            
            List<KeyValuePair<string, string>> randomSetOfWords = Core.GetRandomSetOfWords(count > words.Count
                    ? words.Count
                    : count,
                words);

            List<Word> filteredWords = randomSetOfWords
                .Select(pair => new Word { Name = pair.Key, Translation = pair.Value }).ToList();

            if (offline == false)
            {
                await EnrichWithSynonyms(filteredWords);
            }

            List<Word> errors = CheckAnswerAndPrintResult(filteredWords);
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
                    GetOriginalTranscriptions(skyEngClasses, word);
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

        private static void GetOriginalTranscriptions(SkyEngClass[] skyEngClasses, Word filteredObject)
        {
            SkyEngClass skyEngClass = skyEngClasses?.FirstOrDefault(cl => cl.text.Equals(filteredObject.Name));
            Meaning meaning = skyEngClass?.meanings[0];
            if (meaning == null)
            {
                return;
            }

            string transcription = meaning.transcription;
            filteredObject.Transcription = $"[{transcription}]";
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
                    PrintSuccessMsg($"SUCCESS");
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

        private static void PrintNumberOfWords(List<KeyValuePair<string, string>> words)
        {
            _window.WriteLine(ConsoleColor.White, $"Imported words: {words.Count}");
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