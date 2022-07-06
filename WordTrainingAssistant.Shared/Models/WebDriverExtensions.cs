using System;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace WordTrainingAssistant.Shared.Models
{
    public static class WebDriverExtensions
    {
        public static IWebElement FindElement(this IWebDriver driver, By by, int timeoutInSeconds)
        {
            if (timeoutInSeconds <= 0)
            {
                return driver.FindElement(by);
            }

            WebDriverWait wait = new(driver, TimeSpan.FromSeconds(timeoutInSeconds));
            try
            {
                return wait.Until(webDriver => webDriver.FindElement(by));
            }
            catch
            {
                return null;
            }
        }
    }
}