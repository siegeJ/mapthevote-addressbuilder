using OpenQA.Selenium;
using OpenQA.Selenium.Remote;
using OpenQA.Selenium.Support.UI;
using System;
using System.Threading.Tasks;

namespace MapTheVoteAddressBuilder
{
    public static class MapTheVoteScripter
    {
        static string SelectMarkerJS = "var success = markerHashMap.has(arguments[0]); if (success) { google.maps.event.trigger(markerHashMap.get(arguments[0]), 'click'); success = true; } return success;";

        public static void Login(RemoteWebDriver aDriver)
        {
            // Find the "Login with email" button.
            try
            {
                aDriver.ClickOnElement("firebaseui-idp-password", ElementSearchType.ClassName).Wait();
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
            var nameField = aDriver.FindElementByName("email");
            nameField.SendKeys("hegayiv804@wonrg.com");

            aDriver.ClickOnElement("firebaseui-id-submit", ElementSearchType.ClassName).Wait();

            var passwordField = aDriver.WaitForElement("password", ElementSearchType.Name);
            passwordField.SendKeys("3*YevRM1i7L9");

            aDriver.ClickOnElement("firebaseui-id-submit", ElementSearchType.ClassName).Wait();
        }

        public static bool WaitForMarkerSelection(RemoteWebDriver aDriver, bool aInfiniteWait = false)
        {
            Console.WriteLine("Please select a map marker in MapTheVote.");

            bool success = false;
            var retryCounter = 0;
            do
            {
                try
                {
                    // Wait for the window that opens once selecting a map marker.
                    var mapMarkerWindow = aDriver.WaitForElement("map-infowindow", ElementSearchType.ID);

                    if (mapMarkerWindow != null)
                    {
                        var btnWait = new WebDriverWait(aDriver, TimeSpan.FromSeconds(8));
                        var elementComplete = btnWait.Until(ExpectedConditions.ElementToBeClickable(mapMarkerWindow));

                        success = elementComplete != null;
                    }
                }
                catch (Exception e)
                {
                    // We expect these two exceptions a lot while waiting on user input.
                    if (((e is NoSuchElementException) || (e is WebDriverTimeoutException)))
                    {
                        Util.LogError(ErrorPhase.AddressSelection, e.ToString());
                    }
                }
            }
            while (retryCounter++ < 10 || !aInfiniteWait);

            if (success)
            {
                Console.WriteLine("Starting marker selected. Continuing execution.");
            }

            return success;
        }

        public static ViewBounds GetCurrentViewBounds(RemoteWebDriver aDriver)
        {
            ViewBounds returnVal = null;

            // grab the current viewbounds from the user.
            var objDict = aDriver.ExecuteScript("return viewBounds") as System.Collections.Generic.Dictionary<string, object>;

            if (objDict != null)
            {
                returnVal = new ViewBounds(objDict);
            }

            return returnVal;
        }

        public static void CenterOnViewBounds(RemoteWebDriver aDriver, ViewBounds aBounds)
        {
            if (aBounds != null)
            {
                // You can provide your own debug coordinates by 
                // going to the dev console and get values from "viewBounds" object.

                // Force a recenter and refresh to ensure that our cache is correct before we query for this marker info.
                // likely unnecessary, but being paranoid here in case a desync happens.
                aDriver.ExecuteScript($"map.setCenter({aBounds.LatLngString});");
                aDriver.ExecuteScript($"refreshMapMarkers();");
            }
        }

        public static void DecrementZoom(RemoteWebDriver aDriver, ViewBounds aViewBounds)
        {
            aViewBounds.Zoom = Math.Clamp(aViewBounds.Zoom - 1, 10, 21);
            aDriver.ExecuteScript($"map.setZoom(arguments[0]);", aViewBounds.Zoom);

            // Another shitty hack, but we need to wait for the map to zoom out and recache markers, 
            // and this is a lot easier than tryign to hook into the JS event.
            Util.RandomWait(1500).Wait();
        }

        // MapTheVote holds onto their data in two different places. They internally have a database of
        // address info that they translate to google maps markers as the user pans the map around.
        // We use this function to select a marker from the map to ensure that it's still in its cache after
        // panning around. (Selecting a marker cause smap movement that causes the cache to flush and rebuild,
        // meaning that markers can be missing from when we initially queried it).
        public static async Task<bool> SelectMapMarker(RemoteWebDriver aDriver, AddressResponse aResponse)
        {
            var scrapedMarkers = false;

            try
            {
                // I find it much easier to read in the .js because it's much faster iteration
                // without needing to re-run the entire program and cause other request spam.
                //var parsedJs = File.ReadAllText("SelectMapMarker.js");
                //var trimmedJS = parsedJs.Trim('\n', '\r', ' ');
                var trimmedJS = SelectMarkerJS;

                var addressLonLat = $"{aResponse.Lat},{aResponse.Lng}";

                // Call the JS function that would select a marker by its lnglat.
                // My little function returns true if the map marker was successfully queried and selected.
                scrapedMarkers = (bool)aDriver.ExecuteScript(SelectMarkerJS, addressLonLat);
            }
            catch (Exception e)
            {
                Util.LogError(ErrorPhase.AddressSelection, e.ToString());
            }

            if (scrapedMarkers)
            {
                Console.WriteLine($"Successfully selected {aResponse.FormattedAddress}");
                await Util.RandomWait(1200, 2000);
            }
            else
            {
                Util.LogError(ErrorPhase.AddressSelection, $"Couldn't find the following address in the cache: {aResponse.FormattedAddress}");
            }

            return scrapedMarkers;
        }

        public static async Task<bool> MarkApplicationProcessed(RemoteWebDriver aDriver)
        {
            var applicationProcessed = false;

            try
            {
                // Click on the "tap to start questionnaire" button
                applicationProcessed = await aDriver.ClickOnElement("map-infowindow");
                if (applicationProcessed)
                {
                    // Click on the "Everyone Registered!" button

                    if (Util.DebugMode)
                    {
                        // Ensure first that the element exists on screen.
                        var dbgWait = new WebDriverWait(aDriver, TimeSpan.FromSeconds(10));
                        var elementToClick = dbgWait.Until(ExpectedConditions.ElementExists(By.Id("wizard-button-all-done")));   

                        if (elementToClick != null)
                        {
                            elementToClick.SendKeys(Keys.Escape);
                        }
                    }
                    else
                    {
                        applicationProcessed = await aDriver.ClickOnElement("wizard-button-all-done");

                        // It takes a while for this operation to complete. Wait until the window has closed,
                        // and the new "Registered" window re-opens before returning back to the caller.
                        var btnWait = new WebDriverWait(aDriver, TimeSpan.FromSeconds(10));
                        btnWait.Until(ExpectedConditions.ElementToBeClickable(By.ClassName("map-infowindow")));
                    }
                }
            }
            catch (Exception e)
            {
                Util.LogError(ErrorPhase.MarkApplicationProcessed, e.ToString());
                applicationProcessed = false;
            }

            return applicationProcessed;
        }

        public static async Task CloseMarkerWindow(RemoteWebDriver aDriver)
        {
            var closeInfoWindowScript = "if (typeof infoWindow != 'undefined') infoWindow.close();";
            aDriver.ExecuteScript(closeInfoWindowScript);
            await Util.RandomWait(300, 150);
        }

    } // MapTheVoteDriver
} //namespace MapTheVoteAddressBuilder
