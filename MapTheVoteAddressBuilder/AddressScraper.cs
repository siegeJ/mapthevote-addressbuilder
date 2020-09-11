
using Newtonsoft.Json;
using OpenQA.Selenium.Remote;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace MapTheVoteAddressBuilder
{
    public class ViewBounds
    {
        public string N { get; set; } = string.Empty;
        public string S { get; set; } = string.Empty;
        public string E { get; set; } = string.Empty;
        public string W { get; set; } = string.Empty;

        public string Lat { get; set; } = string.Empty;
        public string Lng { get; set; } = string.Empty;

        public ViewBounds(Dictionary<string, object> queriedBounds)
        {

            if (queriedBounds != null)
            {
                Func<Dictionary<string, object>, string, string> GetBound = (boundsDict, key) =>
                {
                    var returnString = string.Empty;
                    object objValue = null;
                    if (queriedBounds.TryGetValue(key, out objValue))
                    {
                        return objValue.ToString();
                    }

                    return returnString;
                };

                N = GetBound(queriedBounds, "n");
                S = GetBound(queriedBounds, "s");
                E = GetBound(queriedBounds, "e");
                W = GetBound(queriedBounds, "w");

                Lat = GetBound(queriedBounds, "lat");
                Lng = GetBound(queriedBounds, "lng");
            }
        }

        public string LatLngString
        {
            get { return $"{{lat: {Lat}, lng: {Lng}}}"; }
        }
    }

    public class AddressScraper
    {
        HttpClientHandler _handler;
        HttpClient _client;

        List<TargetResponse> _targetResponses;

        public BlockingCollection<AddressResponse> ParsedAddresses { get; private set; } = new BlockingCollection<AddressResponse>(new ConcurrentQueue<AddressResponse>());

        //To get JESSIONID, log into mapthe.vote/map
        //Go to cookies in developer menu, get value of JSESSIONID
        public void Initialize(string aJSessionID)
        {
            if (string.IsNullOrWhiteSpace(aJSessionID))
            {
                Console.WriteLine("You need to put in your JSESSIONID and coordinates noob.");
                Console.ReadLine();
                System.Environment.FailFast("");
            }

            var baseAddress = new Uri("https://mapthe.vote/");
            var cookieContainer = new CookieContainer();
            cookieContainer.Add(baseAddress, new System.Net.Cookie("JSESSIONID", aJSessionID));

            _handler = new HttpClientHandler() { CookieContainer = cookieContainer };
            _client = new HttpClient(_handler) { BaseAddress = baseAddress };
        }

        // Gets all available addresses from the current user's view rectangle.
        public async Task GetTargetAddresses(RemoteWebDriver aDriver, ViewBounds aViewBounds)
        {
            var totalResponses = 0;
            if (aViewBounds != null)
            {
                // Ensure that we aren't querying a location that someone else has already sent to.
                // We also don't support multi-family dwellings yet. (Purely becuase I haven't scripted the UX for it yet)
                _targetResponses = QueryTargetList(aViewBounds).Where((response) => response.NeedsApplication && response.IsSingleHousehold)
                                                    .ToList();

                double totalTargets = _targetResponses.Count;
                Console.WriteLine($"Found a total of for {totalTargets} target Ids.");

                // Using its own variable since the blocking collection
                // can have elements removed from it from another thread.
                foreach (var response in _targetResponses)
                {
                    var addrResponse = await _client.GetAsync($"https://mapthe.vote/rest/addresses/list?targetId={response.Id}");
                    var responseString = await addrResponse.Content.ReadAsStringAsync();

                    var addresses = JsonConvert.DeserializeObject<IEnumerable<AddressResponse>>(responseString);

                    // TODO: Support for multi family dwellings.
                    if (addresses.Count() == 1)
                    {
                        var addy = addresses.First();
                        ParsedAddresses.Add(addy);
                        ++totalResponses;

                        Console.WriteLine($"Queued up submission for Target ID: |{addy.Id}|,  {addy.Addr} @ Lat: {addy.Lat}, Lng: {addy.Lng}");
                    }
                }
            }

            ParsedAddresses.CompleteAdding();
            Console.WriteLine($"A total of {totalResponses} marker infos were successfully queried from target IDs.");
        }

        private IEnumerable<TargetResponse> QueryTargetList(ViewBounds aViewBounds)
        {
            Console.WriteLine("Requesting Address Targets. Request: {0}", aViewBounds.LatLngString);

            //I think the max limit is 1000
            var response = _client.GetAsync($"rest/targets/list?n={aViewBounds.N}&s={aViewBounds.S}&e={aViewBounds.E}&w={aViewBounds.W}&limit=1000").GetAwaiter().GetResult();
            var responseString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            return JsonConvert.DeserializeObject<IEnumerable<TargetResponse>>(responseString);
        }
    }
}
