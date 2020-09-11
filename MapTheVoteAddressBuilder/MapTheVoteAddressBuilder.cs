using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;

namespace MapTheVoteAddressBuilder
{
    class MapTheVoteAddressBuilder
    {
        static FirefoxDriver _driver;

        static string JSESSIONID = string.Empty;

        static void SetupDriver()
        {
            var ffOptions = new FirefoxOptions();
            ffOptions.SetPreference("geo.enabled", false);
            ffOptions.SetPreference("geo.provider.use_corelocation", false);
            ffOptions.SetPreference("geo.prompt.testing", false);
            ffOptions.SetPreference("geo.prompt.testing.allow", false);

            if (Debugger.IsAttached)
            {
                ffOptions.SetLoggingPreference(LogType.Browser, LogLevel.All);
            }

            // Set gekodriver location.
            _driver = new FirefoxDriver(Directory.GetCurrentDirectory(), ffOptions);

            Util.FixDriverCommandExecutionDelay(_driver);
        }

        static void Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("MapThe.Vote/Map Address Builder");
            Console.WriteLine("By: CJ Stankovich https://github.com/siegeJ");
            Console.ForegroundColor = ConsoleColor.White;

            ParseCommandLineArguments(args);

            SetupDriver();

            if (!string.IsNullOrEmpty(JSESSIONID))
            {
                // Can't set a cookie for a domain that we're not yet on.
                // Go to something that we know will 404 so that we can set cookies
                // before continuing execution.
                _driver.Navigate().GoToUrl(@"https://mapthe.vote/404page");

                // TODO: Attempt to get cookie from browser.
                _driver.Manage().Cookies.AddCookie(new OpenQA.Selenium.Cookie("JSESSIONID", JSESSIONID, "mapthe.vote", "/", null));
            }

            // With our JSESSION initialized, we can move onto the actual map.
            _driver.Navigate().GoToUrl(@"https://mapthe.vote/map");

            // Need to manually log in if we don't have a valid cookie.
            if (string.IsNullOrEmpty(JSESSIONID))
            {
                LoginToMapTheVote();

                var jsessionCookie = _driver.Manage().Cookies.GetCookieNamed("JSESSIONID");
                if (jsessionCookie != null)
                {
                    JSESSIONID = jsessionCookie.Value;
                }
            }

            // I hate this, but for some reason waiting for map-msg-button just doesn't work.
            Util.RandomWait(1000).Wait();

            _driver.ClickOnElement("map-msg-button", ElementSearchType.ClassName).Wait();

            var numFails = 0;
            while (numFails < 5)
            {
                var appSubmitter = new ApplicationSubmitter();

                try
                {
                    WaitForAddressSelection();

                    var taskList = new List<Task>();

                    var scraper = new AddressScraper();
                    scraper.Initialize(JSESSIONID);

                    taskList.Add(scraper.GetTargetAddresses(_driver));
                    
                    taskList.Add(appSubmitter.ProcessApplications(_driver, scraper.ParsedAddresses));

                    Task.WaitAll(taskList.ToArray());
                }
                catch (Exception e)
                {
                    Util.LogError(ErrorPhase.Misc, e.ToString());
                }

                var numAddressesSubmitted = appSubmitter.SubmittedAddresses.Count;
                Console.WriteLine($"Successfully submitted { numAddressesSubmitted } applications.");

                // We wait for 5 consecutive fails before ultimately deciding to call it quits.
                var adressesSubmitted = numAddressesSubmitted != 0;
                numFails = adressesSubmitted ? 0 : numFails + 1;

                if (adressesSubmitted)
                {
                    // Sort our addresses by Zip, City, and then address.
                    appSubmitter.SubmittedAddresses.Sort((lhs, rhs) =>
                    {
                        var compareVal = lhs.Zip5.CompareTo(rhs.Zip5);

                        if (compareVal == 0)
                        {
                            compareVal = lhs.City.CompareTo(rhs.City);

                            if (compareVal == 0)
                            {
                                compareVal = lhs.FormattedAddress.CompareTo(rhs.FormattedAddress);
                            }
                        }

                        return compareVal;
                    });

                    WriteAddressesFile($"Addresses_{DateTime.Now:yy-MM-dd_HH-mm-ss}.txt", appSubmitter.SubmittedAddresses);
                }
            }
        }

        static void ParseCommandLineArguments(string[] aArgs)
        {
            foreach (var arg in aArgs)
            {
                var argumentParsed = false;
                if (arg.Contains("JSESSIONID", StringComparison.OrdinalIgnoreCase))
                {
                    var splitArgs = arg.Split(' ');

                    if (splitArgs.Length == 2)
                    {
                        argumentParsed = true;
                        JSESSIONID = splitArgs[1];
                    }
                    else
                    {
                        if (!argumentParsed)
                        {
                            Util.LogError(ErrorPhase.ParsingArguments, $"Could not parse argument {arg}");
                        }
                    }
                }

                if (!argumentParsed)
                {
                    Util.LogError(ErrorPhase.ParsingArguments, $"Could not parse argument {arg}");
                }
            }
        }

        static void LoginToMapTheVote()
        {
            // Find the "Login with email" button.
            try
            {
                _driver.ClickOnElement("firebaseui-idp-password", ElementSearchType.ClassName).Wait();
            }
            catch (Exception e)
            {
                // if it doesn't exist yet, it's likely/possible that the user
                // has already logged in.
                if (!(e is NoSuchElementException))
                {
                    Util.LogError(ErrorPhase.MapTheVoteLogin, e.ToString());
                }

                return;
            }

            // Find the "Enter email address" field
            var nameField = _driver.FindElementByName("email");
            nameField.SendKeys("hegayiv804@wonrg.com");

            _driver.ClickOnElement("firebaseui-id-submit", ElementSearchType.ClassName).Wait();

            var passwordField = _driver.WaitForElement("password");
            passwordField.SendKeys("3*YevRM1i7L9");

            _driver.ClickOnElement("firebaseui-id-submit", ElementSearchType.ClassName).Wait();
        }

        static void WaitForAddressSelection()
        {
            var addressFound = false;
            do
            {
                try
                {
                    var registerBtn = _driver.FindElementById("map-infowindow");
                    var btnWait = new WebDriverWait(_driver, TimeSpan.FromSeconds(3));
                    var elementComplete = btnWait.Until(ExpectedConditions.ElementToBeClickable(registerBtn));

                    addressFound = elementComplete != null;

                    if (addressFound)
                    {
                        Console.WriteLine("Page load complete. Continuing execution.");
                    }
                }
                catch (Exception e)
                {
                    if (e is NoSuchElementException)
                    {
                        Console.WriteLine("Waiting for the page to load. Please select an address.");
                        Util.RandomWait(3000).Wait();
                    }
                }
            }
            while (!addressFound);
        }

        private static void WriteAddressesFile(string aFileName, IEnumerable<AddressResponse> aAddresses)
        {
            var numAddresses = aAddresses.Count();
            if (numAddresses == 0)
            {
                return;
            }

            Console.WriteLine($"Creating Addresses File @ {aFileName}");

            using var tw = new StreamWriter(aFileName);

            string pastZip = string.Empty;
            foreach (var addy in aAddresses)
            {
                // Categorize zips by address
                if (pastZip != addy.Zip5)
                {
                    tw.WriteLine($"{addy.Zip5}");
                    pastZip = addy.Zip5;
                }

                tw.WriteLine($"\t{addy.FormattedAddress}");
            }
        }
    }
}
