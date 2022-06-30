using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
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

        public static async Task<List<KeyValuePair<string, string>>> ParseFiles(FileSystemInfo dir,
            FileSystemInfo externalDictionary)
        {            
            List<KeyValuePair<string, string>> words = new();

            if (dir == null)
            {
                return words;
            }

            words = await ParseWebPage(dir.FullName);
            words.AddRange(await ParseDictionary(externalDictionary));

            return words;
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

        private static async Task<List<KeyValuePair<string, string>>> ParseWebPage(string dir)
        {
            List<KeyValuePair<string, string>> words = new();

            string[] files = Directory.GetFiles(dir);

            foreach (string file in files)
            {
                string source = await File.ReadAllTextAsync(file);
                words.AddRange(await GetWordsFromSource(source));
            }

            return words;
        }

        private static async Task<List<KeyValuePair<string, string>>> GetWordsFromSource(string source)
        {
            List<KeyValuePair<string, string>> words = new();
            
            IConfiguration config = Configuration.Default;
            IBrowsingContext context = BrowsingContext.New(config);

            IDocument document = await context.OpenAsync(req => req.Content(source));

            IHtmlCollection<IElement> wordSets = document.QuerySelectorAll("div.wordset");

            foreach (IElement wordSet in wordSets)
            {
                IHtmlCollection<IElement> liElements = wordSet.QuerySelectorAll("li");

                words.AddRange(liElements
                    .Select(GetWordAndTranslationFromLiElement).Where(item =>
                        !new[] {item.Key, item.Value}.Any(String.IsNullOrWhiteSpace)));
            }

            return words;
        }

        private static KeyValuePair<string, string> GetWordAndTranslationFromLiElement(IElement li)
        {
            string name =
                li.QuerySelectorAll("div.original > span.text").FirstOrDefault()?.TextContent
                    .Trim().Replace('’', '\'') ?? "";
            string translation =
                li.QuerySelectorAll("div.translation").FirstOrDefault()?.TextContent.Trim() ?? "";

            return new KeyValuePair<string, string>(name, translation);
        }

        public static async Task<List<KeyValuePair<string, string>>> GetWordsFromSite(string path, string login,
            string password, FileSystemInfo externalDictionary)
        {
            List<string> sources = GetPageSources(path, login, password);
            List<KeyValuePair<string,string>> words = new();
            foreach (string source in sources)
            {
                words.AddRange(await GetWordsFromSource(source));
            }
            
            words.AddRange(await ParseDictionary(externalDictionary));

            return words;
        }

        private static List<string> GetPageSources(string path, string login, string password)
        {
            ChromeDriver driver = null;
            try
            {
                ChromeOptions options = new();
                options.AddArgument("--headless");
                options.AddArgument("--window-size=1920,1080");
                options.AddArgument("--log-level=3");
                
                ChromeDriverService service = ChromeDriverService.CreateDefaultService(path);
                service.SuppressInitialDiagnosticInformation = true;
                service.HideCommandPromptWindow = true;
                
                driver = new ChromeDriver(service, options);

                driver.Navigate().GoToUrl("https://vimbox.skyeng.ru/words/vocabulary");
                
                IWebElement link = driver.FindElement(By.CssSelector("[class='link link--primary js-phone-form-to-username-password']"), 5);
                if (link == null)
                {
                    return new List<string>();
                }
                link.Click();
                
                IWebElement loginBox = driver.FindElement(By.CssSelector("[class='input js-username-password-form-input']"), 5);
                if (loginBox == null)
                {
                    return new List<string>();
                }

                IWebElement passwordBox = driver.FindElement(
                    By.CssSelector("[class='input js-username-password-form-input js-username-password-form-password-input']"), 5);
                if (password == null)
                {
                    return new List<string>();
                }

                loginBox.SendKeys(login);
                passwordBox.SendKeys(password);

                IWebElement button = driver.FindElement(By.CssSelector("[class='button button--primary']"), 5);
                if (button == null)
                {
                    return new List<string>();
                }
                button.Click();
                
                ReadOnlyCollection<IWebElement> pages = driver.FindElements(By.CssSelector("[class='page']"), 10);
                List<string> sources = new(){ driver.PageSource };
                if (pages == null)
                {
                    return sources;
                }
                
                for (int i = 0; i < pages.Count; i++)
                {
                    ReadOnlyCollection<IWebElement> paginationLinks = driver.FindElements(By.CssSelector("[class='page']"), 3);
                    
                    ClickWithDelay(driver, paginationLinks[i]);
                    sources.Add(driver.PageSource);
                }
            
                driver.Quit();
                return sources;
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
                driver?.Quit();
                return new List<string>();
            }
        }

        private static void ClickWithDelay(ChromeDriver driver, IWebElement paginationLink)
        {
            new Actions(driver)
                .MoveToElement(paginationLink)
                .Click()
                .Pause(TimeSpan.FromMilliseconds(500))
                .Perform();
        }

    }
}