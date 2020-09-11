using OpenQA.Selenium.Remote;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MapTheVoteAddressBuilder
{
    public class ApplicationSubmitter
    {
        public List<AddressResponse> SubmittedAddresses { get; private set; } = new List<AddressResponse>();

        public async Task<IEnumerable<AddressResponse>> ProcessApplications(RemoteWebDriver aDriver, BlockingCollection<AddressResponse> aResponses)
        {
            foreach (var address in aResponses.GetConsumingEnumerable())
            {
                if (await MapTheVoteScripter.SelectMapMarker(aDriver, address))
                {
                    // If we can successfully sleect a marker, this means that we have it in the cache,
                    // and it's valid. We can move on to the SoS site to submit an application.
                    var submissionSuccess = await SubmitNewApplication(aDriver, address);
                    if (submissionSuccess)
                    {
                        var applicationLogged = await MapTheVoteScripter.MarkApplicationProcessed(aDriver);
                        if (applicationLogged)
                        {
                            SubmittedAddresses.Add(address);
                        }
                    }
                }
            }

            await MapTheVoteScripter.CloseMarkerWindow(aDriver);
            
            return SubmittedAddresses;
        }

        public static async Task<bool> SubmitNewApplication(RemoteWebDriver aDriver, AddressResponse aAddress, bool openNewTab = true)
        {
            // I leave this bool around so that I can easily flip it off for when I want to debug the SoS site.
            if (openNewTab)
            {
                aDriver.ExecuteScript("window.open();");
                aDriver.SwitchTo().Window(aDriver.WindowHandles.Last());
            }

            aDriver.Navigate().GoToUrl("https://webservices.sos.state.tx.us/vrrequest/index.asp");

            // Look for the dropbox element that holds the number of applications.
            var dropdownBoxElement = aDriver.FindElementByName("xcnt");
            if (dropdownBoxElement == null)
            {
                Util.LogError(ErrorPhase.ApplicationSubmit, "Could not load submission page");
                return false;
            }

            // Select 2 applications.
            var selectElement = new SelectElement(dropdownBoxElement);
            selectElement.SelectByIndex(1);

            // Fill in a registration form from the data we've scraped earlier.
            Func<string, string, Task> fillOutField = async (fieldName, fieldValue) =>
            {
                await Util.RandomWait(200, 300);
                var requestedField = aDriver.FindElementByName(fieldName);

                // Add a little delay to naturally slow things down
                // and simulate typing.
                foreach (var letter in fieldValue)
                {
                    requestedField.SendKeys(letter.ToString());
                    await Util.RandomWait(100, 75);
                }
            };

            await fillOutField("fname", "Current");
            await fillOutField("lname", "Resident");

            await fillOutField("address", aAddress.FormattedAddress);
            await fillOutField("city", aAddress.City);
            await fillOutField("zip", aAddress.Zip5);

            await Util.RandomWait(100, 650);

            // Go for our submit button.
            var submitBtn = aDriver.FindElementByName("submit");
            submitBtn.Click();

            submitBtn = aDriver.WaitForElement("submit");
            if (submitBtn == null)
            {
                Util.LogError(ErrorPhase.ApplicationSubmit, "Could not load Confirmation page");
                return false;
            }

            await Util.RandomWait(750, 650);

            if (!Util.DebugMode)
            {
                submitBtn.Click();
                await Util.RandomWait(300, 200);
            }

            aDriver.Close();

            if (aDriver.WindowHandles.Count <= 0)
            {
                return false;
            }

            aDriver.SwitchTo().Window(aDriver.WindowHandles.First());

            Console.WriteLine($"Application successfully submitted for {aAddress.Addr}, {aAddress.Addr2}, {aAddress.City}, {aAddress.State}, {aAddress.Zip5}");
            return true;
        }
    }
}
