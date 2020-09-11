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

        static string AddressesFileName { get { return $"Addresses_{DateTime.Now:yy-MM-dd_HH-mm-ss}.txt"; } }

        static void SetupDriver()
        {
            try
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
            catch(Exception e)
            {
                Util.LogError(ErrorPhase.DriverInitialization, e.ToString());
            }
        }

        static void Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("MapThe.Vote/Map Address Builder");
            Console.WriteLine("By: CJ Stankovich https://github.com/siegeJ");
            Console.ForegroundColor = ConsoleColor.White;

            Util.PreventSleep();

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
                MapTheVoteScripter.Login(_driver);

                var jsessionCookie = _driver.Manage().Cookies.GetCookieNamed("JSESSIONID");
                if (jsessionCookie != null)
                {
                    JSESSIONID = jsessionCookie.Value;
                }
            }

            // I hate this, but for some reason waiting for map-msg-button just doesn't work.
            Util.RandomWait(1000).Wait();

            _driver.ClickOnElement("map-msg-button", ElementSearchType.ClassName).Wait();

            DateTime startingTime = default;

            var numFails = 0;
            var lastNumAddressesParsed = 0;
            ViewBounds prevBounds = null;
            while (numFails < 3)
            {
                var appSubmitter = new ApplicationSubmitter();

                Task<IEnumerable<AddressResponse>> processAppsTask = null;

                try
                {
                    // Wait for user input if we've successfully parsed everything from the
                    // previous run. Otherwise we can use the same bounds again in order to 
                    // re-sweep for the markers that we may have missed.
                    // This can happpen
                    var noAddressesParsed = lastNumAddressesParsed == 0;
                    if (noAddressesParsed || (prevBounds == null))
                    {
                        if (noAddressesParsed && prevBounds != null && (prevBounds.Zoom > ViewBounds.ZOOM_LIMITS.Item1))
                        {
                            MapTheVoteScripter.DecrementZoom(_driver, prevBounds);
                            // Reset fail count if we're going to try searching around.
                            numFails = 0;
                        }
                        else
                        {
                            // End execution if the user has idled out
                            var success = MapTheVoteScripter.WaitForMarkerSelection(_driver);
                            if (!success)
                            {
                                break;
                            }
                        }

                        prevBounds = MapTheVoteScripter.GetCurrentViewBounds(_driver);
                    }
                    else
                    {
                        Console.WriteLine("Repeating the previous search to find uncached values.");
                    }

                    startingTime = DateTime.Now;

                    var scraper = new AddressScraper();
                    scraper.Initialize(JSESSIONID);

                    if (prevBounds != null)
                    {
                        MapTheVoteScripter.CenterOnViewBounds(_driver, prevBounds);
                    }

                    var getAddressesTask = scraper.GetTargetAddresses(_driver, prevBounds);
                    processAppsTask = appSubmitter.ProcessApplications(_driver, scraper.ParsedAddresses);

                    Task.WaitAll(getAddressesTask, processAppsTask);

                    lastNumAddressesParsed = getAddressesTask.Result;

                }
                catch (Exception e)
                {
                    Util.LogError(ErrorPhase.Misc, e.ToString());
                }

                // Do this in case the user did something to fuck things up. This way we can still successfully write out the file.
                // TODO: Thread the filewriting.
                if (processAppsTask != null && processAppsTask.Status == TaskStatus.Running)
                {
                    processAppsTask.Wait();
                }

                var lastNumAddressesSubmitted = appSubmitter.SubmittedAddresses.Count;
                Console.WriteLine($"Successfully submitted { lastNumAddressesSubmitted } / { lastNumAddressesParsed } applications.");

                // We wait for 3 consecutive fails before ultimately deciding to call it quits.
                var adressesSubmitted = lastNumAddressesSubmitted != 0;
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

                    WriteAddressesFile(AddressesFileName, appSubmitter.SubmittedAddresses);
                }

                Console.WriteLine("Completed in {0}", DateTime.Now - startingTime);
            }

            CombineAddressesFiles();

            Console.WriteLine("Execution complete.");
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
                else if (arg.Contains("DEBUG", StringComparison.OrdinalIgnoreCase))
                {
                    Util.DebugMode = true;
                }

                if (!argumentParsed)
                {
                    Util.LogError(ErrorPhase.ParsingArguments, $"Could not parse argument {arg}");
                }
            }
        }

        // Combines all address files into a single .txt, then renames anything
        // used to {file}.consumed
        private static void CombineAddressesFiles()
        {
            var filesToCombine = Directory.GetFiles(Directory.GetCurrentDirectory(), "Addresses_*.txt");
            if (filesToCombine.Length == 0)
            {
                return;
            }

            Console.WriteLine("Combining all addresses files.");

            using var tw = new StreamWriter($"COMBINED_{AddressesFileName}");

            foreach(var file in filesToCombine)
            {
                var existingLines = File.ReadAllLines(file);

                foreach(var line in existingLines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        tw.WriteLine(line);
                    }   
                }

                File.Move(file, Path.ChangeExtension(file, ".consumed"));
            }
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
