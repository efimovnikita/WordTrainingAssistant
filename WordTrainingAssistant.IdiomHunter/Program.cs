using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Newtonsoft.Json;

namespace WordTrainingAssistant.IdiomHunter
{
    internal static class Program
    {
        private const string BaseUrl = "https://sentencestack.com";
        private const string IdiomsUrl = "https://sentencestack.com/idioms";

        private static async Task Main(string[] args)
        {
            HttpClient client = new();
            string responseBody = await client.GetStringAsync(IdiomsUrl);
            
            HtmlParser parser = new();
            IHtmlDocument idiomsDocument = await parser.ParseDocumentAsync(responseBody);

            IElement linksContainer = idiomsDocument.QuerySelector("body > div:nth-child(3) > div > div:nth-child(5) > div:nth-child(6)");

            if (linksContainer == null)
            {
                return;
            }

            IHtmlCollection<IElement> anchors = linksContainer.QuerySelectorAll("a");

            Token token = await GetTokenFromCloud();

            foreach (IElement anchor in anchors)
            {
                string href = anchor.GetAttribute("href");
                string idiomPageSource = await client.GetStringAsync($"{BaseUrl}{href}");
                IHtmlDocument idiomDocument = await parser.ParseDocumentAsync(idiomPageSource);
                string idiomText = idiomDocument.QuerySelector(
                    "body > div.container > main > div.idioms-container > div.idiom > div.idiom-text")?.Text();
                string idiomMeaning = idiomDocument.QuerySelector(
                    "body > div.container > main > div.idioms-container > div.idiom > div.idiom-meaning")?.Text();

                idiomText = idiomText?.ToLowerInvariant();

                string translationFromCloud = await GetTranslationFromCloud(token.iamtoken, idiomMeaning);
                translationFromCloud = translationFromCloud.ToLowerInvariant();
                translationFromCloud = translationFromCloud.TrimEnd('.');
                translationFromCloud = $"{translationFromCloud} ({idiomMeaning})";
                
                string value = $"{idiomText}:{translationFromCloud}";

                Console.WriteLine(value);
            }
        }
        
        private static async Task<string> GetTranslationFromCloud(string token, string input)
        {
            try
            {
                HttpClient httpClient = new();
                httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                TranslationRequest request = new()
                    {folderId = "b1get5pjvk3cj932g5bg", texts = input, targetLanguageCode = "ru"};

                string json = JsonConvert.SerializeObject(request,
                    Formatting.None, new JsonSerializerSettings {DefaultValueHandling = DefaultValueHandling.Ignore});
                httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);

                StringContent content = new(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response =
                    await httpClient.PostAsync("https://translate.api.cloud.yandex.net/translate/v2/translate", content);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return "";
                }
            
                string responseContent = await response.Content.ReadAsStringAsync();

                TranslationResponse translationResponse = JsonConvert.DeserializeObject<TranslationResponse>(responseContent);
                if (translationResponse != null && translationResponse.translations.Length > 0)
                {
                    return translationResponse.translations[0].text;
                }

                return "";
            }
            catch
            {
                return "";
            }
        }

        private static async Task<Token> GetTokenFromCloud()
        {
            try
            {
                HttpClient httpClient = new();
                httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                string auth = await File.ReadAllTextAsync("/home/maskedball/RiderProjects/WordTrainingAssistant/WordTrainingAssistant.IdiomHunter/OauthToken.txt");
                TokenRequest request = new()
                    { yandexPassportOauthToken = auth };

                string json = JsonConvert.SerializeObject(request);

                StringContent content = new(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response =
                    await httpClient.PostAsync("https://iam.api.cloud.yandex.net/iam/v1/tokens", content);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return null;
                }

                string stringContent = await response.Content.ReadAsStringAsync();

                return JsonConvert.DeserializeObject<Token>(stringContent);
            }
            catch
            {
                return null;
            }
        }
    }
    
    public class TranslationResponse
    {
        public Translation[] translations { get; set; }
    }

    public class Translation
    {
        public string text { get; set; }
    }

    public class TranslationRequest
    {
        public string folderId { get; set; }
        public string texts { get; set; }
        public string targetLanguageCode { get; set; }
    }
    public class Token
    {
        public string iamtoken { get; set; }
    }
    
    public class TokenRequest
    {
        public string yandexPassportOauthToken { get; set; }
    }
}