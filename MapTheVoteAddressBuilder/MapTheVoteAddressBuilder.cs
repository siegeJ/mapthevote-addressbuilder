using System;
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
        private static string sendingGmailPassword = ""; // Hit up Ray or CJ for the password :), or put your own email/password
        private static string ToEmail = ""; // Most likely ray's email

        private static bool SendAddressesInEmail = false;
        #endregion

        static bool SetupDriver(bool aPromptForBinaryLocation = false)
        {
            var success = false;
            try
            {
                var ffOptions = new FirefoxOptions();
                ffOptions.SetPreference("geo.enabled", false);
                ffOptions.SetPreference("geo.provider.use_corelocation", false);
                ffOptions.SetPreference("geo.prompt.testing", false);
                ffOptions.SetPreference("geo.prompt.testing.allow", false);

                // I've heard of some situations where firefox.exe couldn't be found by gekodriver.
                // Just in case, we'll give the user a chance to provide this location on their own.
                if (aPromptForBinaryLocation)
                {
                    Console.WriteLine("Could not find Firefox. Please enter the filepath to your firefox.exe: ");
                    ffOptions.BrowserExecutableLocation = Console.ReadLine();
                }

                if (Debugger.IsAttached)
                {
                    ffOptions.SetLoggingPreference(LogType.Browser, LogLevel.All);
                }

                // Set gekodriver location.
                string architecture = System.Environment.Is64BitOperatingSystem ? "win64" : "win32";
                _driver = new FirefoxDriver($"{Directory.GetCurrentDirectory()}/dependencies/gekodriver/{architecture}", ffOptions);

                Util.FixDriverCommandExecutionDelay(_driver);

                success = true;
            }
            catch(Exception e)
            {
                Util.LogError(ErrorPhase.DriverInitialization, e.ToString());
            }

            return success;
        }

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.ProcessExit += new EventHandler(CurrentDomain_ProcessExit);
            Util.SetAlwaysOnTop();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("MapThe.Vote/Map Address Builder");
            Console.WriteLine("By: CJ Stankovich https://github.com/siegeJ and Ray Batts https://github.com/RayBatts");
            Console.ForegroundColor = ConsoleColor.White;

            ParseCommandLineArguments(args);

            var driverSetupSuccess = SetupDriver();

            if (!driverSetupSuccess)
            {
                driverSetupSuccess = SetupDriver(true);

                if (!driverSetupSuccess)
                {
                    Util.LogError(ErrorPhase.DriverInitialization, "Fatal error. Coult not recover.");
                    return;
                }
            }

            DateTime startingTime = default;
            var lastNumAddressesParsed = 0;

            Task<IEnumerable<AddressResponse>> processAppsTask = null;
            appSubmitter.SubmittedAddresses.Clear();

            try
            {
                startingTime = DateTime.Now;

                var scraper = new CSVAddressParser();

                var getAddressesTask = scraper.GetTargetAddresses("TX_vr_postcard_chase.csv");

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
            if (addressesSubmitted)
            {
                var adressesSubmitted = lastNumAddressesSubmitted != 0;
                Console.WriteLine($"Successfully submitted { lastNumAddressesSubmitted } / { lastNumAddressesParsed } applications.");

                WriteAddressesFile(AddressesFileName, appSubmitter.SubmittedAddresses);
            }

            Console.WriteLine("Completed in {0}", DateTime.Now - startingTime);

            CombineAddressesFiles();

            Console.WriteLine("Execution complete. Would you like to restart? Press Y to restart or any other key to exit.");
            Console.ReadKey(true);
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
                    argumentParsed = true;

                    Console.WriteLine("EXECUTING IN DEBUG MODE. NO PERMANANT CHANGES WILL BE MADE!");
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

            Console.WriteLine("Exiting application. Please remember to send any COMBINED_.txt files to the registration drive organizers.");
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

            // Temporarily disabling this this until we have the UX to use this without 
            // require a recompile.
            if (SendAddressesInEmail)
            {
                SendEmail(combinedFileName);
            }
            else
            {
                Console.WriteLine($"Please send the following file to the drive organizers: ${ combinedFileName }");
            }
        }

        private static void SendEmail(string fileName)
        {
            if (string.IsNullOrEmpty(ToEmail) || string.IsNullOrEmpty(sendingGmailEmail))
            {
                Console.WriteLine("Could not log into email to send address file.");
                return;
            }

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

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Successfully submitted {fileName} to project organizers. Thank you!");
                Console.ForegroundColor = ConsoleColor.White;
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
