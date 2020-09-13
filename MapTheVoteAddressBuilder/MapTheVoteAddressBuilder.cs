﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
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

        static ApplicationSubmitter appSubmitter = new ApplicationSubmitter();

        #region AutomatedEmail
        //Information for automatically sending an email at the end.
        //You can use your own email to send it, but will need to "Allow less secure apps" in gmail. https://myaccount.google.com/security.
        private static string sendingGmailEmail = "mapthevoteaddressbuilder@gmail.com";
        private static string sendingGmailPassword = "Hit up Ray or CJ for the password :), or put your own email/password";
        private static string ToEmail = "Most likely ray's email";
        #endregion


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

            AppDomain.CurrentDomain.ProcessExit += new EventHandler(CurrentDomain_ProcessExit);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("MapThe.Vote/Map Address Builder");
            Console.WriteLine("By: CJ Stankovich https://github.com/siegeJ and Ray Batts https://github.com/RayBatts");
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

            var appSubmitter = new ApplicationSubmitter();

            while (numFails < 3)
            {
                Task<IEnumerable<AddressResponse>> processAppsTask = null;
                appSubmitter.SubmittedAddresses.Clear();

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

                var addressesSubmitted = lastNumAddressesSubmitted != 0;
                // We wait for 3 consecutive fails before ultimately deciding to call it quits.
                numFails = addressesSubmitted ? 0 : numFails + 1;
                if (addressesSubmitted)
                {
                    var adressesSubmitted = lastNumAddressesSubmitted != 0;
                    Console.WriteLine($"Successfully submitted { lastNumAddressesSubmitted } / { lastNumAddressesParsed } applications.");

                    WriteAddressesFile(AddressesFileName, appSubmitter.SubmittedAddresses);
                }

                Console.WriteLine("Completed in {0}", DateTime.Now - startingTime);
            }

            CombineAddressesFiles();

            Console.WriteLine("Execution complete. Restart the application to send more registration forms.");
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

        static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            //If we exited early
            if (!File.Exists(AddressesFileName))
            {
                WriteAddressesFile(AddressesFileName, appSubmitter.SubmittedAddresses);
                CombineAddressesFiles();
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

            string combinedFileName = $"COMBINED_{AddressesFileName}";
            using var tw = new StreamWriter(combinedFileName);

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

            tw.Dispose();
            SendEmail(combinedFileName);
        }

        private static void SendEmail(string fileName)
        {
            try
            {
                var smtpClient = new SmtpClient("smtp.gmail.com")
                {
                    Port = 587,
                    Credentials = new NetworkCredential(sendingGmailEmail, sendingGmailPassword),
                    EnableSsl = true,
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(sendingGmailEmail, "MapTheVoteAddressBuilder"),
                    Subject = "Addresses",
                    Body =
                        "This is an automated message from the MapTheVoteAddressBuilder application. https://github.com/siegeJ/mapthevote-addressbuilder",
                    IsBodyHtml = true,
                };

                mailMessage.To.Add(ToEmail);
                var attachment = new Attachment(fileName, MediaTypeNames.Text.Plain);
                mailMessage.Attachments.Add(attachment);

                smtpClient.Send(mailMessage);
            }
            catch (Exception e)
            {
                Console.WriteLine("You probably need to fill out the AutomatedEmail section above.");
                Console.WriteLine(e.ToString());
            }
        }

        public static void WriteAddressesFile(string aFileName, IEnumerable<AddressResponse> aAddresses)
        {
            var addressList = aAddresses.ToList();
            // Sort our addresses by Zip, City, and then address.
            addressList.Sort((lhs, rhs) =>
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

            var numAddresses = addressList.Count;
            if (numAddresses == 0)
            {
                return;
            }

            Console.WriteLine($"Creating Addresses File @ {aFileName}");

            using var tw = new StreamWriter(aFileName);

            string pastZip = string.Empty;
            foreach (var addy in addressList)
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
