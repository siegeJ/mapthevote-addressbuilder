
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace MapTheVoteAddressBuilder
{
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

        public async Task GetTargetAddresses()
        {
            var tempTarRequest = new TargetRequest()
            {
                N = "32.960363",
                S = "32.954188",
                E = "-96.66536",
                W = "-96.673546"
            };

            _targetResponses = QueryTargetList(tempTarRequest).Where((response) => response.NeedsApplication && response.IsSingleHousehold)
                                                .ToList();

            double totalTargets = _targetResponses.Count;
            Console.WriteLine($"Found a total of for {totalTargets} target Ids.");

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

                    Console.WriteLine($"Queued up submission for Target ID: |{addy.Id}|,  {addy.Addr} @ Lat: {addy.Lat}, Lng: {addy.Lng}");
                }
            }

            ParsedAddresses.CompleteAdding();
            Console.WriteLine("All addresses have finished querying marker info from target IDs.");
        }

        private IEnumerable<TargetResponse> QueryTargetList(TargetRequest aTarget)
        {
            Console.WriteLine($"Requesting Address Targets. Request: " + JsonConvert.SerializeObject(aTarget));

            //I think the max limit is 1000
            var response = _client.GetAsync($"rest/targets/list?n={aTarget.N}&s={aTarget.S}&e={aTarget.E}&w={aTarget.W}&limit=10000").GetAwaiter().GetResult();
            var responseString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            return JsonConvert.DeserializeObject<IEnumerable<TargetResponse>>(responseString);
        }
    }
}
