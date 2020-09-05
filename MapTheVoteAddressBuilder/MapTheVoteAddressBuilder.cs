using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;

namespace MapTheVoteAddressBuilder
{
    class MapTheVoteAddressBuilder
    {
        static FirefoxDriver _driver;

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

            var tr = new TargetRequest()
            {
                //To get JESSIONID, log into mapthe.vote/map
                //Go to cookies in developer menu, get value of JSESSIONID
                JSESSIONID = string.Empty,

                //To Get Coordinates. Go to dev console and get values from "viewBounds" object.

                N = "32.960363",
                S = "32.954188",
                E = "-96.66536",
                W = "-96.673546"
            };

            if (string.IsNullOrWhiteSpace(tr.JSESSIONID))
            {
                Console.WriteLine("You need to put in your JSESSIONID and coordinates noob.");
                Console.ReadLine();
                System.Environment.FailFast("");
            }

            SetupDriver();

            // Can't set a cookie for a domain that we're not yet on.
            // Go to something that we know will 404 so that we can set cookies
            // before continuing execution.
            _driver.Navigate().GoToUrl(@"https://mapthe.vote/404page");

            // TODO: Attempt to get cookie from browser.
            _driver.Manage().Cookies.AddCookie(new OpenQA.Selenium.Cookie("JSESSIONID", tr.JSESSIONID, "mapthe.vote", "/", null));

            // With our JSESSION initialized, we can move onto the actual map.
            _driver.Navigate().GoToUrl(@"https://mapthe.vote/map");

            // TODO: Detect if JSESSION was valid. If not, we'll need to log in.
            //if (jsessionInvalid) { LoginToMapTheVote(); }

            var enterMapBtn = _driver.FindElementByClassName("map-msg-button");
            enterMapBtn.Click();

            var taskList = new List<Task>();

            var scraper = new AddressScraper();
            scraper.Initialize(tr.JSESSIONID);

            taskList.Add(scraper.GetTargetAddresses());

            var appSubmitter = new ApplicationSubmitter();
            taskList.Add(appSubmitter.ProcessApplications(_driver, scraper.ParsedAddresses));

            Task.WaitAll(taskList.ToArray());

            Console.WriteLine($"Successfully submitted { appSubmitter.SubmittedAddresses.Count } applications.");

            var addressLines = new List<string>(appSubmitter.SubmittedAddresses.Count);
            foreach (var processedAddress in appSubmitter.SubmittedAddresses)
            {
                addressLines.Add($"{processedAddress.Zip5} {processedAddress.Addr}");
            }

            WriteFile($"Addresses_{DateTime.Now:yy-MM-dd_HH-mm-ss}.txt", addressLines);
        }

        static void LoginToMapTheVote()
        {
            // Find the "Login with email" button.
            var loginEmailBtn = _driver.WaitForElement("firebaseui-idp-password");
            if (loginEmailBtn == null)
            {
                return;
            }
            loginEmailBtn.Click(); // Click button

            // Find the "Enter email address" field
            var nameField = _driver.FindElementByName("email");
            nameField.SendKeys("hegayiv804@wonrg.com");

            var submitEmailBtn = _driver.FindElementByClassName("firebaseui-id-submit");
            submitEmailBtn.Click();

            var passwordField = _driver.FindElementByName("password");
            passwordField.SendKeys("3*YevRM1i7L9");

            submitEmailBtn = _driver.FindElementByClassName("firebaseui-id-submit");
            submitEmailBtn.Click();
        }

        static void TestApplicationSent()
        {
            var testResponse = new AddressResponse
            {
                Addr = "12345 HappytownLn",
                Addr2 = "APT 125",
                City = "Sunshine City",
                Zip5 = "12345"
            };

            ApplicationSubmitter.SubmitNewApplication(_driver, testResponse, true).Wait();
        }

        private static void WriteFile(string fileName, IEnumerable<string> lines)
        {
            using var tw = new StreamWriter(fileName);

            foreach (var s in lines)
            {
                tw.WriteLine(s);
            }
        }
    }
}
