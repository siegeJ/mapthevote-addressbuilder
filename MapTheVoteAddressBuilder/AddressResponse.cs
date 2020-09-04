using System;
using System.Collections.Generic;
using System.Text;

namespace MapTheVoteAddressBuilder
{
    public class AddressResponse
    {

        //{
        //    "id": 34934369,
        //    "lat": 33.00684,
        //    "lng": -96.856996,
        //    "status": 1,
        //    "latLng": "33.00684,-96.856996",
        //    "precinct": null,
        //    "addr": "18788 Marsh Ln Apt 122",
        //    "addr2": null,
        //    "city": "Dallas",
        //    "state": "TX",
        //    "zip5": 75287,
        //    "zip4": null,
        //    "county": "DENTON",
        //    "created": 1591244383712,
        //    "modified": 1599067019417
        //},

        public int Id { get; set; }

        //Seems to always be null...
        public string Precinct { get; set; }

        public string Addr { get; set; }

        public string Addr2 { get; set; }

        public string City { get; set; }

        public string Zip5 { get; set; }

        public string State { get; set; }

        public string County { get; set; }
    }

  
}
