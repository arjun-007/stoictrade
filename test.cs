using System;
using System.Net.Http;

class Program
{
    static void Main()
    {
        var client = new HttpClient { BaseAddress = new Uri(""https://www.nseindia.com"") };
        var req = new HttpRequestMessage(HttpMethod.Get, ""/api/option-chain-indices?symbol=NIFTY"");
        Console.WriteLine(new Uri(client.BaseAddress, req.RequestUri).ToString());
    }
}
