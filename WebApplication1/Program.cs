var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => { var now = DateTime.Now.ToString(); Console.WriteLine(now); return $"Hello World at {now}!"; });

app.Run();
