using OpenQA.Selenium.Remote;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MapTheVoteAddressBuilder
{
    public class ApplicationSubmitter
    {
        public List<AddressResponse> SubmittedAddresses { get; private set; } = new List<AddressResponse>();

        public async Task<IEnumerable<AddressResponse>> ProcessApplications(RemoteWebDriver aDriver, BlockingCollection<AddressResponse> aResponses)
        {
            var mapCentered = false;
            foreach (var address in aResponses.GetConsumingEnumerable())
            {
                var locString = $"{{lat: {address.Lat.ToString() }, lng: {address.Lng.ToString()}}}";

                // Temporary, ensure that we are looking at the right section of the map
                // so that the markers we need will be in the cache.
                if (!mapCentered)
                {
                    aDriver.ExecuteScript($"map.setCenter({locString});");
                    mapCentered = true;
                }

                if (await SelectAddress(aDriver, address))
                {
                    var submissionSuccess = await SubmitNewApplication(aDriver, address);
                    if (submissionSuccess)
                    {
                        // TODO: Head back to the MapTheVote tab and
                        // update the marker.temp wait until then.
                        await Util.RandomWait(250, 400);

                        SubmittedAddresses.Add(address);
                    }
                }
            }

            return SubmittedAddresses;
        }

        public static async Task<bool> SubmitNewApplication(RemoteWebDriver aDriver, AddressResponse aAddress, bool openNewTab = true)
        {
            if (openNewTab)
            {
                aDriver.ExecuteScript("window.open();");
                aDriver.SwitchTo().Window(aDriver.WindowHandles.Last());
            }

            aDriver.Navigate().GoToUrl("https://webservices.sos.state.tx.us/vrrequest/index.asp");

            var dropdownBoxElement = aDriver.FindElementByName("xcnt");
            if (dropdownBoxElement == null)
            {
                Util.LogError(ErrorPhase.ApplicationSubmit, "Could not load submission page");
                return false;
            }

            // Select 2 applications.
            var selectElement = new SelectElement(dropdownBoxElement);
            selectElement.SelectByIndex(1);

            // Fill in Current/Resident first & last name.

            Func<string, string, Task> fillOutField = async (fieldName, fieldValue) =>
            {
                await Util.RandomWait(200, 500);
                var requestedField = aDriver.FindElementByName(fieldName);

                // Add a little delay to naturally slow things down
                // and simulate typing.
                foreach (var letter in fieldValue)
                {
                    requestedField.SendKeys(letter.ToString());
                    await Util.RandomWait(100, 250);
                }
            };

            await fillOutField("fname", "Current");
            await fillOutField("lname", "Resident");

            await fillOutField("address", $"{aAddress.Addr} {aAddress.Addr2}");
            await fillOutField("city", aAddress.City);
            await fillOutField("zip", aAddress.Zip5);

            await Util.RandomWait(100, 650);

            var submitBtn = aDriver.FindElementByName("submit");
            submitBtn.Click();

            submitBtn = aDriver.WaitForElement("submit");
            if (submitBtn == null)
            {
                Util.LogError(ErrorPhase.ApplicationSubmit, "Could not load Confirmation page");
                return false;
            }

            await Util.RandomWait(750, 650);
            // TODO: fire off the submit button when we're actually ready to go.
            //submitBtn.Click();

            aDriver.Close();

            if (aDriver.WindowHandles.Count <= 0)
            {
                return false;
            }

            aDriver.SwitchTo().Window(aDriver.WindowHandles.First());

            Console.WriteLine($"Application successfully submitted for {aAddress.Addr}, {aAddress.Addr2}, {aAddress.City}, {aAddress.State}, {aAddress.Zip5}");
            return true;
        }

        async Task<bool> SelectAddress(RemoteWebDriver aDriver, AddressResponse aResponse)
        {
            var scrapedMarkers = false;

            try
            {
                // I find it much easier to read in the .js because it's much faster iteration
                // without needing to re-run the entire program and cause other request spam.
                var parsedJs = File.ReadAllText("SelectMapMarker.js");
                var trimmedJS = parsedJs.Trim('\n', '\r', ' ');

                var addressLonLat = $"{aResponse.Lat},{aResponse.Lng}";
                scrapedMarkers = (bool)aDriver.ExecuteScript(parsedJs.ToString(), addressLonLat);

                if (scrapedMarkers)
                {
                    Console.WriteLine($"Successfully selected {aResponse.Addr} @ {addressLonLat}");
                    await Util.RandomWait(1200, 2000);
                }
            }
            catch (Exception e)
            {
                Util.LogError(ErrorPhase.AddressSelection, e.ToString());
            }

            return scrapedMarkers;
        }

    }
}
