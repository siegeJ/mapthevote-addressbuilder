using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace MapTheVoteAddressBuilder
{
    class MapTheVoteAddressBuilder
    {

        static void Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("MapThe.Vote/Map Address Builder");
            Console.WriteLine("By: CJ Stankovich https://github.com/siegeJ");
            Console.ForegroundColor = ConsoleColor.White;


            var tr = new TargetRequest()
            {

                //To get JESSIONID, log into mapthe.vote/map
                //Go to cookies in developer menu, get value of JSESSIONID
                JSESSIONID = "",

                //To Get Coordinates. Go to dev console and get values from "viewBounds" object.
                N = "33.018225",
                S = "32.985977",
                E = "-96.813301",
                W = "-96.957497"
            };

            if (string.IsNullOrWhiteSpace(tr.JSESSIONID))
            {
                Console.WriteLine("You need to put in your JSESSIONID and coordinates noob.");
                Console.ReadLine();
                System.Environment.FailFast("");
            }

            var baseAddress = new Uri("https://mapthe.vote/");
            var cookieContainer = new CookieContainer();
            cookieContainer.Add(baseAddress, new Cookie("JSESSIONID", tr.JSESSIONID));
            using var handler = new HttpClientHandler() { CookieContainer = cookieContainer };
            using var client = new HttpClient(handler) {BaseAddress = baseAddress };

            var targetList = (GetTargetList(tr, client).GetAwaiter().GetResult()).ToList();
            double totalTargets = targetList.Count;
            Console.WriteLine($"Getting addresses for {totalTargets} target Ids.");

            List<string> addressLines = new List<string>();
            addressLines.Add($"Coordinates N:{tr.N},S:{tr.S},E:{tr.E},W:{tr.W}");

            double totalComplete = 0;
            foreach (var target in targetList)
            {
                var addressResponses = GetAddresses(target.Id, client).GetAwaiter().GetResult();
                foreach (var address in addressResponses)
                {
                    addressLines.Add($"{address.Zip5} {address.Addr}");
                }
                totalComplete++;

                if (totalComplete % 50 == 0)
                {
                    Console.WriteLine(((totalComplete/totalTargets) * 100).ToString("##.00") + "% Complete");
                }
            }

            WriteFile($"Addresses_{DateTime.Now:yy-MM-dd_HH-mm-ss}.txt", addressLines);
        }

        public static async Task<IEnumerable<TargetResponse>> GetTargetList(TargetRequest tr, HttpClient client)
        {
            Console.WriteLine($"Requesting Address Targets. Request: " + JsonConvert.SerializeObject(tr));
  
   
            //I think the max limit is 1000
            HttpResponseMessage response =
                await client.GetAsync(
                    $"rest/targets/list?n={tr.N}&s={tr.S}&e={tr.E}&w={tr.W}&limit=10000");

            var responseString = await response.Content.ReadAsStringAsync();

            return JsonConvert.DeserializeObject<IEnumerable<TargetResponse>>(responseString);

        }

        public static async Task<IEnumerable<AddressResponse>> GetAddresses(int targetId, HttpClient client)
        {

            HttpResponseMessage response =
                await client.GetAsync(
                    $"https://mapthe.vote/rest/addresses/list?targetId={targetId}");

            var responseString = await response.Content.ReadAsStringAsync();

            var addresses = JsonConvert.DeserializeObject<IEnumerable<AddressResponse>>(responseString);

            return addresses;

        }

        private static void WriteFile(string fileName, IEnumerable<string> lines)
        {
            using var tw = new StreamWriter(fileName);

            foreach (String s in lines)
            {
                tw.WriteLine(s);
            }
        }
    }
}
