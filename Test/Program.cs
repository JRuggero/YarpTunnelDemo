// See https://aka.ms/new-console-template for more information
using System.Diagnostics;
using System.Net;

Console.WriteLine("Hello, World!");

var httpClient = new HttpClient();
var timer = new Stopwatch();
var errors = new List<Error>();
var rnd = new Random();
int iterations = 0;
while (true)
{
    var delay = rnd.Next(10, 1000);
    await Task.Delay(delay);

    iterations++;
    timer.Restart();
    var result = await httpClient.GetAsync("https://localhost:7244");
    var elapsed = timer.Elapsed;
    var status = result.StatusCode;

    if(status is not System.Net.HttpStatusCode.OK)
    { 
        errors.Add(new Error(DateTime.Now, status, elapsed, TimeSpan.FromMilliseconds(delay)));
    }

    Console.WriteLine($"{DateTime.Now.ToString("HH.mm.ss.fff")} Status: {status} in: {elapsed.TotalMilliseconds}ms, iterations: {iterations}, {errors.Count} errors");

}

record Error (DateTime Time, HttpStatusCode StatusCode, TimeSpan elapsed, TimeSpan previousDelay);
