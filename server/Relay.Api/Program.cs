var builder = WebApplication.CreateBuilder(args);

// MVC controllers + RFC 7807 ProblemDetails for error responses.
builder.Services.AddControllers();
builder.Services.AddProblemDetails();

// Allow the Vite dev/preview client to call the API during development.
const string ClientCors = "client";
builder.Services.AddCors(options =>
{
    options.AddPolicy(ClientCors, policy =>
        policy.WithOrigins(
                "http://localhost:5173",
                "http://localhost:4173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseCors(ClientCors);
app.MapControllers();

app.Run();

// Exposed so Relay.Tests can spin the app up with WebApplicationFactory<Program>.
public partial class Program { }
