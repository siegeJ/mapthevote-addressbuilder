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
            var x = 0;
            foreach (var address in aResponses.GetConsumingEnumerable())
            {   
                // If we can successfully sleect a marker, this means that we have it in the cache,
                // and it's valid. We can move on to the SoS site to submit an application.
                var submissionSuccess = await SubmitNewApplication(aDriver, address, x);
                if (submissionSuccess)
                {
                    ++x;
                    SubmittedAddresses.Add(address);
                }
            }
            
            return SubmittedAddresses;
        }

        public static async Task<bool> SubmitNewApplication(RemoteWebDriver aDriver, AddressResponse aAddress, int idx, bool openNewTab = true)
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
                await Util.RandomWait(0, 100);
                var requestedField = aDriver.FindElementByName(fieldName);

                // Add a little delay to naturally slow things down
                // and simulate typing.
                foreach (var letter in fieldValue)
                {
                    requestedField.SendKeys(letter.ToString());
                    await Util.RandomWait(0, 75);
                }
            };

            await fillOutField("fname", aAddress.FirstName);
            await fillOutField("lname", aAddress.LastName);

            await fillOutField("address", aAddress.FormattedAddress);
            await fillOutField("city", aAddress.City);
            await fillOutField("zip", aAddress.Zip5);

            await Util.RandomWait(100, 650);

            // Go for our submit button.
            var submitBtn = aDriver.WaitForElement("submit", ElementSearchType.Name);
            submitBtn.Click();

            submitBtn = aDriver.WaitForElement("submit", ElementSearchType.Name);
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

            Console.WriteLine($"Application successfully submitted for index {idx}, {aAddress.FormattedAddress}, {aAddress.City}, {aAddress.State}, {aAddress.Zip5}");
            return true;
        }
    }
}
