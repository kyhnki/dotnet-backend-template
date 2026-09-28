using System.Globalization;
using System.Text.Json.Serialization;
using DotNetBackendTemplate.Api.Common.Extensions;
using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddApplicationServices(builder.Configuration, builder.Environment);

WebApplication app = builder.Build();

app.UseRequestLocalization();
// Content-Language must be written via OnStarting: UseExceptionHandler clears response headers
// when it re-executes the pipeline, so an eagerly written header would be lost on error responses.
app.Use(async (context, next) =>
{
    string responseCulture = CultureInfo.CurrentUICulture.Name;
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.ContentLanguage = responseCulture;
        return Task.CompletedTask;
    });

    await next(context);
});
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
// Registered before authentication so a 401 challenge, a 403 forbid, and an empty 404 all get a Problem Details body.
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi().AllowAnonymous();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "docs";
        options.SwaggerEndpoint("/openapi/v1.json", ".NET Backend Template API v1");
        options.DocumentTitle = ".NET Backend Template API";
        options.DisplayRequestDuration();
        options.ConfigObject.PersistAuthorization = false;
    });
}

app.MapApplicationEndpoints();

app.Run();

public partial class Program;
