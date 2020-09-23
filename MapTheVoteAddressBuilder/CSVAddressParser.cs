using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace MapTheVoteAddressBuilder
{
    public class CSVAddressParser
    {
        public BlockingCollection<AddressResponse> ParsedAddresses { get; private set; } = new BlockingCollection<AddressResponse>(new ConcurrentQueue<AddressResponse>());

        // Gets all available addresses from the current user's view rectangle.
        public async Task<int> GetTargetAddresses(string fileName, int skipTo = 0)
        {
            var totalAddresses = 0;

            var tarFileExists = File.Exists(fileName);

            if (!tarFileExists)
            {
                Util.LogError(ErrorPhase.AddressSelection, $"Could not file file { fileName }");
                return totalAddresses;
            }

            totalAddresses = await Task<int>.Run(() =>
            {
                var addyCount = 0;
                using (var sr = new StreamReader(fileName))
                {
                    var headerLine = sr.ReadLine();

                    if (string.IsNullOrWhiteSpace(headerLine))
                    {
                        Util.LogError(ErrorPhase.AddressSelection, "Invalid header line");
                        return addyCount;
                    }

                    var headerTitles = headerLine.Split(',');

                    // Get indices of each title.
                    var addressIdx = -1;
                    var cityIdx = -1;
                    var stateIdx = -1;
                    var firstNameIdx = -1;
                    var lastNameIdx = -1;
                    var zipCodeIdx = -1;
                    var countyIdx = -1;

                    for (var x = 0; x < headerTitles.Length; ++x)
                    {
                        switch (headerTitles[x])
                        {
                            case "outbound_address":
                                addressIdx = x;
                                break;
                            case "outbound_city":
                                cityIdx = x;
                                break;
                            case "outbound_state":
                                stateIdx = x;
                                break;
                            case "outbound_zip":
                                zipCodeIdx = x;
                                break;
                            case "regform_lastName":
                                lastNameIdx = x;
                                break;
                            case "regform_firstName":
                                firstNameIdx = x;
                                break;
                            case "regform_resCounty":
                                countyIdx = x;
                                break;


                            default:
                                break;
                        }
                    }

                    if (countyIdx == -1 || addressIdx == -1 || cityIdx == -1 || stateIdx == -1 || firstNameIdx == -1 || lastNameIdx == -1 || zipCodeIdx == -1)
                    {
                        Util.LogError(ErrorPhase.AddressSelection, "Could not parse all header indices");
                        return addyCount;
                    }

                    string curLine = string.Empty;
                    for (var x = 0; x < skipTo; x++)
                    {
                        curLine = sr.ReadLine();
                    }

                    if (string.IsNullOrEmpty(curLine))
                    {
                        curLine = sr.ReadLine();
                    }
                    // Walk through the rest of the file parsing addresses.
                     
                    while (!string.IsNullOrWhiteSpace(curLine))
                    {
                        var splitAddressInfo = curLine.Split(',');

                        var addy = new AddressResponse()
                        {
                            State = splitAddressInfo[stateIdx],
                            Addr = splitAddressInfo[addressIdx],
                            City = splitAddressInfo[cityIdx],
                            County = splitAddressInfo[countyIdx],
                            Zip5 = splitAddressInfo[zipCodeIdx].Substring(0, 5)
                        };

                        // Make sure that the first and last names are properly
                        // capitilized.
                        addy.FirstName = $"{splitAddressInfo[firstNameIdx][0]}{splitAddressInfo[firstNameIdx].Substring(1, splitAddressInfo[firstNameIdx].Length - 1).ToLower()}";
                        addy.LastName = $"{splitAddressInfo[lastNameIdx][0]}{splitAddressInfo[lastNameIdx].Substring(1, splitAddressInfo[lastNameIdx].Length - 1).ToLower()}";

                        ParsedAddresses.Add(addy);
                        addyCount++;
                        Console.WriteLine($"Parsed address info for,  {addy.Addr} ");

                        curLine = sr.ReadLine();
                    }
                }

                ParsedAddresses.CompleteAdding();

                return addyCount;
            });

            Console.WriteLine($"A total of {totalAddresses} marker infos were successfully queried from target IDs.");
            return totalAddresses;
        }
    }
}
