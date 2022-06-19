using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WordTrainingAssistant.Models;
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
            
            List<KeyValuePair<string, string>> randomSetOfWords = Core.GetRandomSetOfWords(count > words.Count
                    ? words.Count
                    : count,
                words);

            List<Word> filteredWords = randomSetOfWords
                .Select(pair => new Word { Name = pair.Key, Translation = pair.Value }).ToList();

            await EnrichWithSynonyms(filteredWords);

            List<Word> errors = CheckAnswerAndPrintResult2(filteredWords);
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

                CheckAnswerAndPrintResult2(errors);
            }
        }

        private static async Task EnrichWithSynonyms(List<Word> filteredWords)
        {
            if (Core.CheckForInternetConnection())
            {
                HttpClient client = new();
                foreach (Word word in filteredWords)
                {
                    HttpResponseMessage response = await client
                        .GetAsync($"https://dictionary.skyeng.ru/api/public/v1/words/search?search={word.Translation}");

                    string stringAsync = await response.Content.ReadAsStringAsync();
                    SkyEngClass[] skyEngClasses = JsonConvert.DeserializeObject<SkyEngClass[]>(stringAsync);
                    GetOriginalTranscriptions(skyEngClasses, word);
                    GetAnotherWords(skyEngClasses, word);
                }
            }
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

        private static List<Word> CheckAnswerAndPrintResult2(List<Word> filteredObjects)
        {
            List<Word> errors = new();
            foreach (Word word in filteredObjects)
            {
                PrintDefaultMsg(word.Translation);
                string line = Console.ReadLine();
                List<Word> synonyms = word.Synonyms;
                if (Core.CheckAnswer(line, word.Name))
                {
                    PrintSuccessMsg($"SUCCESS - {word.Name}");
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
            PrintDefaultMsg($"Number of imported words: {words.Count}");
            Console.WriteLine();
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