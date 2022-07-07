using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using WordTrainingAssistant.Shared.Models;
using Cookie = OpenQA.Selenium.Cookie;

namespace WordTrainingAssistant.Shared
{
    public static class QueueExtensions
    {
        public static IEnumerable<T> DequeueChunk<T>(this Queue<T> queue, int chunkSize) 
        {
            for (int i = 0; i < chunkSize && queue.Count > 0; i++)
            {
                yield return queue.Dequeue();
            }
        }
    }
    
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

#pragma warning disable SYSLIB0014
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014
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

        public static async Task<bool> CheckAnswer(string userInput, Word word, bool audio)
        {
            bool equals = userInput == null || userInput.Equals(word.name, StringComparison.InvariantCultureIgnoreCase);

            if (equals)
            {
                if (audio) await Pronounce(word.name);
                return true;
            }

            if (word.synonyms.Count == 0)
            {
                if (audio) await Pronounce(word.name);
                return false;
            }

            Word synonym = word.synonyms
                .FirstOrDefault(synonym => synonym.name.Equals(userInput, StringComparison.InvariantCultureIgnoreCase));

            if (synonym is null)
            {
                if (audio) await Pronounce(word.name);
                return false;
            }

            if (audio) await Pronounce(synonym.name);
            return true;
        }
        
        private static async Task<KeyValuePair<string, string>[]> ParseDictionary(FileSystemInfo externalDictionary)
        {
            if (externalDictionary == null)
            {
                return Array.Empty<KeyValuePair<string,string>>();
            }

            string rawText = await File.ReadAllTextAsync(externalDictionary.FullName);
            return DictionaryParser.ParseDictionary(rawText);
        }
        
        public static async Task<List<KeyValuePair<string, string>>> GetWordsFromSite(string login,
            string password, string studentId, string path, FileSystemInfo externalDictionary)
        {
            List<KeyValuePair<string,string>> words = new();
            words.AddRange(await GetWordsFromApi(login, password, studentId, path));
            
            words.AddRange(await ParseDictionary(externalDictionary));

            return words;
        }
        
        private static async Task<List<KeyValuePair<string, string>>> GetWordsFromApi(string login, string password, 
            string studentId, string driverPath)
        {
            ChromeDriver driver = null;
            try
            {
                ChromeOptions options = new();
                options.AddArgument("--headless");
                options.AddArgument("--window-size=1920,1080");
                options.AddArgument("--log-level=3");
                
                ChromeDriverService service = ChromeDriverService.CreateDefaultService(driverPath);
                service.SuppressInitialDiagnosticInformation = true;
                service.HideCommandPromptWindow = true;
                
                driver = new ChromeDriver(service, options);

                driver.Navigate().GoToUrl("https://vimbox.skyeng.ru/words/vocabulary");
                
                IWebElement link = driver
                    .FindElement(By.CssSelector("[class='link link--primary js-phone-form-to-username-password']"),
                        5);
                if (link == null)
                {
                    return new List<KeyValuePair<string, string>>();
                }
                link.Click();
                
                IWebElement loginBox = driver
                    .FindElement(By.CssSelector("[class='input js-username-password-form-input']"), 
                        5);
                if (loginBox == null)
                {
                    return new List<KeyValuePair<string, string>>();
                }

                IWebElement passwordBox = driver.FindElement(
                    By.CssSelector("[class='input js-username-password-form-input js-username-password-form-password-input']"), 
                    5);
                if (password == null)
                {
                    return new List<KeyValuePair<string, string>>();
                }

                loginBox.SendKeys(login);
                passwordBox.SendKeys(password);

                IWebElement button = driver.FindElement(By.CssSelector("[class='button button--primary']"), 
                    5);
                if (button == null)
                {
                    return new List<KeyValuePair<string, string>>();
                }
                new Actions(driver)
                    .MoveToElement(button)
                    .Click()
                    .Pause(TimeSpan.FromMilliseconds(300))
                    .Perform();
                
                Cookie cookie = driver.Manage().Cookies.GetCookieNamed("token_global");
                
                driver.Quit();

                HttpClient httpClient = new();
                SetsRoot wordSets = await GetWordSets(cookie, studentId, httpClient);
                List<int> setsIds = wordSets.data.Select(datum => datum.id).ToList();
                
                List<int> wordIds = new();
                foreach (int setsId in setsIds.Distinct())
                {
                    SetRoot setWords = await GetWordsFromSet(studentId, setsId, cookie, httpClient);
                    List<int> meaningsIds = setWords.data.Select(datum => datum.meaningId).ToList();
                    wordIds.AddRange(meaningsIds);
                }
                
                List<MeaningRoot> meanings = new(await GetMeanings(wordIds.Distinct().ToList(), httpClient));
                
                List<KeyValuePair<string, string>> words = meanings
                    .Select(meaning => new KeyValuePair<string, string>(meaning.text, meaning.translation.text))
                    .ToList();
                return words;
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
                driver?.Quit();
                return new List<KeyValuePair<string, string>>();
            }
        }


        private static async Task<List<MeaningRoot>> GetMeanings(List<int> wordIds, HttpClient httpClient)
        {
            Queue<int> queue = new(wordIds);
            List<MeaningRoot> meanings = new ();

            do
            {
                List<int> chunk = queue.DequeueChunk(10).ToList();
                meanings.AddRange(await GetMeaningsForChunk(chunk, httpClient));
            } while (queue.Count != 0);
            
            return meanings;
        }

        private static async Task<List<MeaningRoot>> GetMeaningsForChunk(List<int> wordIds, HttpClient httpClient)
        {
            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get,
                RequestUri =
                    new Uri($"https://dictionary.skyeng.ru/api/for-services/v2/meanings?ids={String.Join(',', wordIds)}"),
            };
            using HttpResponseMessage response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            List<MeaningRoot> meanings = JsonConvert.DeserializeObject<List<MeaningRoot>>(responseBody);
            return meanings;
        }

        private static async Task<SetRoot> GetWordsFromSet(string studentId, int setsId, Cookie cookie, 
            HttpClient httpClient)
        {
            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get,
                RequestUri =
                    new Uri(
                        $"https://api-words.skyeng.ru/api/v1/wordsets/{setsId}/words.json?studentId={studentId}&wordsetId={setsId}&pageSize=500&page=1"),
                Headers =
                {
                    {
                        "authorization",
                        $"Bearer {cookie.Value}"
                    },
                },
            };
            using HttpResponseMessage response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();

            SetRoot setWords = JsonConvert.DeserializeObject<SetRoot>(responseBody);
            return setWords;
        }

        private static async Task<SetsRoot> GetWordSets(Cookie cookie, string studentId, HttpClient client)
        {
            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get,
                RequestUri =
                    new Uri(
                        $"https://api-words.skyeng.ru/api/for-vimbox/v1/wordsets.json?studentId={studentId}&pageSize=1000&page=1"),
                Headers =
                {
                    {
                        "authorization",
                        $"Bearer {cookie.Value}"
                    },
                },
            };

            using HttpResponseMessage response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();

            SetsRoot wordSets = JsonConvert.DeserializeObject<SetsRoot>(responseBody);
            return wordSets;
        }

        public static async Task Pronounce(string word)
        {
            string file = await DownloadVoice(word);
            if (String.IsNullOrEmpty(file))
            {
                return;
            }

            await Play(file);

            File.Delete(file);
        }

        private static async Task Play(string mp3)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) == false)
            {
                return;
            }

            Process pulseAudioStart = new();
            pulseAudioStart.StartInfo.RedirectStandardOutput = true;
            pulseAudioStart.StartInfo.FileName = "pactl";
            pulseAudioStart.StartInfo.Arguments = "info";
            pulseAudioStart.Start();
            await pulseAudioStart.WaitForExitAsync();
            if (pulseAudioStart.ExitCode != 0)
            {
                throw new Exception($"Failed to initialize the PulseAudio daemon: {pulseAudioStart.ExitCode}");
            }

            Process mpvStart = Process.Start("mpv", $"--no-video --gapless-audio --volume=100.0 {mp3} --no-terminal");
            if (mpvStart != null)
            {
                await mpvStart.WaitForExitAsync();
                if (mpvStart.ExitCode != 0)
                {
                    throw new Exception($"Failed to start the player: {mpvStart.ExitCode}");
                }
            }
        }

        private static async Task<string> DownloadVoice(string word)
        {
            try
            {
                HttpClientHandler clientHandler = new()
                {
                    UseCookies = false,
                };
                HttpClient client = new(clientHandler);
                HttpRequestMessage request = new()
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri($"https://vimbox-tts.skyeng.ru/api/v1/tts?text={word}&lang=en&voice=male_2"),
                };
                using HttpResponseMessage response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                Stream body = await response.Content.ReadAsStreamAsync();
            
                body.Seek(0, SeekOrigin.Begin);

                string path = Path.Combine(Path.GetTempPath(), "temp.mp3");
                await using FileStream fs = new(path, FileMode.OpenOrCreate);
                await body.CopyToAsync(fs);

                return File.Exists(path) ? path : "";
            }
            catch
            {
                return "";
            }
        }
    }
}