using E2ETests.Config;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;
using WebDriverManager.Helpers;

namespace E2ETests.Drivers;

public static class DriverFactory
{
    public static IWebDriver CreateChromeDriver()
    {
        // Without an explicit strategy, WebDriverManager can resolve to the
        // latest published ChromeDriver rather than one matching the locally
        // installed Chrome, which fails with "session not created" on any
        // version mismatch.
        new DriverManager().SetUpDriver(new ChromeConfig(), VersionResolveStrategy.MatchingBrowser);

        var options = new ChromeOptions();
        if (TestConfig.Headless)
        {
            options.AddArgument("--headless=new");
        }
        options.AddArgument("--window-size=1280,900");

        return new ChromeDriver(options);
    }
}
