using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;

namespace MapTheVoteAddressBuilder
{
    class MapTheVoteAddressBuilder
    {
        static FirefoxDriver _driver;
        static bool MANUAL_APPLICATION_REQUESTS = true;

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

        [STAThreadAttribute]
        static void Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("MapThe.Vote/Map Address Builder");
            Console.WriteLine("By: CJ Stankovich https://github.com/siegeJ");
            Console.ForegroundColor = ConsoleColor.White;

            SetupDriver();

            // Can't set a cookie for a domain that we're not yet on.
            // Go to something that we know will 404 so that we can set cookies
            // before continuing execution.
            _driver.Navigate().GoToUrl(@"https://mapthe.vote/404page");

            string JSESSIONID = "jvnZ7yJ8ePXH0aH1tP_OLA";
            // TODO: Detect if JSESSION was valid. If not, we'll need to log in.
            if (string.IsNullOrEmpty(JSESSIONID))
            {
                Console.WriteLine("You need to put in your JSESSIONID and coordinates noob.");
                Console.ReadLine();
                System.Environment.FailFast("");
                //LoginToMapTheVote();
            }
            else
            {
                // TODO: Attempt to get cookie from browser.
                _driver.Manage().Cookies.AddCookie(new OpenQA.Selenium.Cookie("JSESSIONID", JSESSIONID, "mapthe.vote", "/", null));
            }

            // With our JSESSION initialized, we can move onto the actual map.
            _driver.Navigate().GoToUrl(@"https://mapthe.vote/map");

            var enterMapBtn = _driver.FindElementByClassName("map-msg-button");
            enterMapBtn.Click();

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

                    if (!MANUAL_APPLICATION_REQUESTS)
                    {
                        taskList.Add(appSubmitter.ProcessApplications(_driver, scraper.ParsedAddresses));
                    }

                    Task.WaitAll(taskList.ToArray());

                    if (MANUAL_APPLICATION_REQUESTS)
                    {
                        appSubmitter.ProcessApplicationsManually(_driver, scraper.ParsedAddresses);
                    }
                }
                catch (Exception e)
                {
                    Util.LogError(ErrorPhase.Misc, e.ToString());
                }

                var numAddressesSubmitted = appSubmitter.SubmittedAddresses.Count;
                Console.WriteLine($"Successfully submitted { numAddressesSubmitted } applications.");

                var adressesSubmitted = numAddressesSubmitted != 0;
                numFails = adressesSubmitted ? 0 : numFails + 1;

                if (adressesSubmitted)
                {
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

                    var addressLines = new List<string>(appSubmitter.SubmittedAddresses.Count * 2);

                    string pastZip = string.Empty;
                    foreach (var addy in appSubmitter.SubmittedAddresses)
                    {
                        if (pastZip != addy.Zip5)
                        {
                            addressLines.Add($"{addy.Zip5}");
                            pastZip = addy.Zip5;
                        }

                        addressLines.Add($"\t{addy.FormattedAddress}");
                    }

                    WriteFile($"Addresses_{DateTime.Now:yy-MM-dd_HH-mm-ss}.txt", addressLines);
                }
            }
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
            Console.WriteLine($"Creating Addresses File @ {fileName}");

            using var tw = new StreamWriter(fileName);

            foreach (var s in lines)
            {
                tw.WriteLine(s);
            }
        }
    }
}
