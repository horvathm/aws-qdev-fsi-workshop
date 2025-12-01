using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Contrib.Extensions.AWSXRay.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

namespace Origination.Helpers;

/// <summary>
/// It is recommended to use a custom type to hold references for
/// ActivitySource and Instruments. This avoids possible type collisions
/// with other components in the DI container.
/// </summary>
public class Instrumentation : IInstrumentation
{
    private readonly IConfiguration _configuration;
    
    public Instrumentation(IConfiguration configuration)
    {
        _configuration = configuration;
        var activitySourceName = Environment.GetEnvironmentVariable("SERVICE_NAME") ?? 
                _configuration["ServiceName"];
        string? version = typeof(Instrumentation).Assembly.GetName().Version?.ToString();
        ArgumentNullException.ThrowIfNull(activitySourceName);
        this.ActivitySource = new ActivitySource(activitySourceName, version);
        
        // Initialize OpenTelemetry
        InitializeOpenTelemetry(activitySourceName);
    }

    public ActivitySource ActivitySource { get; }

    private void InitializeOpenTelemetry(string activitySourceName)
    {
        // This is required if the collector doesn't expose an https endpoint
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        // Get endpoint from configuration
        string endpoint = _configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317";
        
        // Configure tracer provider
        Sdk.CreateTracerProviderBuilder()
            .AddSource(activitySourceName)
            .SetResourceBuilder(
                ResourceBuilder.CreateDefault()
                    .AddService(serviceName: activitySourceName)
                    .AddTelemetrySdk())
            .SetSampler(new AlwaysOnSampler())
            .AddXRayTraceId()
            .AddAWSInstrumentation()
            .AddAspNetCoreInstrumentation()
            .AddOtlpExporter()
            .Build();

        // Configure meter provider
        Sdk.CreateMeterProviderBuilder()
            .AddMeter("adot")
            .AddOtlpExporter()
            .Build();

        // Configure the propagator to use AWS X-Ray format
        Sdk.SetDefaultTextMapPropagator(new AWSXRayPropagator());
    }

    public void Dispose()
    {
        this.ActivitySource.Dispose();
    }
}