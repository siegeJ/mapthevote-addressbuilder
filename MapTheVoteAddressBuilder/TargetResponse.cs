using System;
using System.Collections.Generic;
using System.Text;

namespace MapTheVoteAddressBuilder
{
    public class TargetRequest
    {
        public string JSESSIONID { get; set; }
        public string N { get; set; }
        public string E { get; set; }
        public string S { get; set; }
        public string W { get; set; }
    }
    public class TargetResponse
    {
        //{
        //    "id": 21426948,
        //    "status": 7,
        //    "count": 21,
        //    "latLng": "33.011543,-96.85161"
        //},

        public int Id { get; set; }
        public int Status { get; set; }
        public int Count { get; set; }
        public string LatLng { get; set; }
    }
}
