using Origination;
using Origination.Service;
using Origination.Helpers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging();
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddSingleton<IAWSConfig, AWSConfig>();
builder.Services.AddSingleton<IInstrumentation, Instrumentation>();
builder.Services.AddScoped<IApplicationService, ApplicationService>();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/hc");

app.Run();
