using System.Net.WebSockets;
using Yarp.ReverseProxy.Forwarder;

public static class TunnelExensions
{
    public static IServiceCollection AddTunnelServices(this IServiceCollection services)
    {
        var tunnelFactory = new TunnelClientFactory();
        services.AddSingleton(tunnelFactory);
        services.AddSingleton<IForwarderHttpClientFactory>(tunnelFactory);
        return services;
    }

    public static IEndpointConventionBuilder MapHttp2Tunnel(this IEndpointRouteBuilder routes, string path)
    {
        return routes.MapPost(path, static async (HttpContext context, string host, TunnelClientFactory tunnelFactory, IHostApplicationLifetime lifetime, ILoggerFactory loggerFactory) =>
        {
            // HTTP/2 duplex stream
            if (context.Request.Protocol != HttpProtocol.Http2)
            {
                return Results.BadRequest();
            }
            var logger = loggerFactory.CreateLogger("Yarp.ReverseProxy.Http2Endpoint");

            var tunnelId = context.GetHashCode();
            logger.LogDebug("Http2Tunnel {tunnelId} hitted", tunnelId);

            var (requests, responses) = tunnelFactory.GetConnectionChannel(host);

            try
            {
                await requests.Reader.ReadAsync(context.RequestAborted);
            }
            catch(Exception ex)
            {
                logger.LogDebug("requests.Reader.ReadAsync exception: {exception}", ex.Message);

                //throw;
                logger.LogDebug("Http2Tunnel {tunnelId} finished", tunnelId);
                return EmptyResult.Instance;
            }

            var stream = new DuplexHttpStream(context);
            logger.LogDebug("Stream {streamId}({tunnelId}) created", stream.GetHashCode(), tunnelId);

            using var reg = lifetime.ApplicationStopping.Register(() => stream.Abort());

            // Keep reusing this connection while, it's still open on the backend
            if(!context.RequestAborted.IsCancellationRequested && !stream.IsClosed)
            //while(!context.RequestAborted.IsCancellationRequested && !stream.IsClosed)
            {
                // Make this connection available for requests
                await responses.Writer.WriteAsync(stream, context.RequestAborted);
                logger.LogDebug("Stream {streamId}({tunnelId}) added to streams queue", stream.GetHashCode(), tunnelId);

                await stream.StreamCompleteTask;
                logger.LogDebug("Stream {streamId}({tunnelId}) completed", stream.GetHashCode(), tunnelId);

                ////if (!stream.IsClosed)
                //    stream.Reset();
            }

            logger.LogDebug("Http2Tunnel {tunnelId} finished", tunnelId);
            return EmptyResult.Instance;
        });
    }

    public static IEndpointConventionBuilder MapWebSocketTunnel(this IEndpointRouteBuilder routes, string path)
    {
        var conventionBuilder = routes.MapGet(path, static async (HttpContext context, string host, TunnelClientFactory tunnelFactory, IHostApplicationLifetime lifetime) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                return Results.BadRequest();
            }

            var (requests, responses) = tunnelFactory.GetConnectionChannel(host);

            await requests.Reader.ReadAsync(context.RequestAborted);

            var ws = await context.WebSockets.AcceptWebSocketAsync();

            var stream = new WebSocketStream(ws);

            // We should make this more graceful
            using var reg = lifetime.ApplicationStopping.Register(() => stream.Abort());

            // Keep reusing this connection while, it's still open on the backend
            while (ws.State == WebSocketState.Open)
            {
                // Make this connection available for requests
                await responses.Writer.WriteAsync(stream, context.RequestAborted);

                await stream.StreamCompleteTask;

                stream.Reset();
            }

            return EmptyResult.Instance;
        });

        // Make this endpoint do websockets automagically as middleware for this specific route
        conventionBuilder.Add(e =>
        {
            var sub = routes.CreateApplicationBuilder();
            sub.UseWebSockets().Run(e.RequestDelegate!);
            e.RequestDelegate = sub.Build();
        });

        return conventionBuilder;
    }

    // This is for .NET 6, .NET 7 has Results.Empty
    internal sealed class EmptyResult : IResult
    {
        internal static readonly EmptyResult Instance = new();

        public Task ExecuteAsync(HttpContext httpContext)
        {
            return Task.CompletedTask;
        }
    }
}