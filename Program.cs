using Microsoft.Extensions.Options;
using SpotifyTrivia.Hubs;
using SpotifyTrivia.Models.Multiplayer;
using SpotifyTrivia.Services;
using SpotifyTrivia.Services.GameModes;
using SpotifyTrivia.Services.Stems;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddHttpClient();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddSingleton<ISpotifyService, SpotifyService>();
builder.Services.AddSingleton<IDeezerService, DeezerService>();
builder.Services.AddSingleton<ILobbyManager, LobbyManager>();
builder.Services.AddSingleton<IBroadcaster, Broadcaster>();
builder.Services.AddSingleton<LobbySettingsModel>();
builder.Services.AddSingleton<IGameMode, ClassicGuessSongGameMode>();
builder.Services.AddSingleton<IGameMode, GuessArtistGameMode>();
builder.Services.AddSingleton<IGameModeFactory, GameModeFactory>();
builder.Services.AddSingleton<IGameMode, StemGuessGameMode>();
builder.Services.AddSingleton<StemStore>();
builder.Services.AddSingleton<PendingStemQuestions>();
builder.Services.AddSingleton<StemPipeline>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<StemPipeline>());
builder.Services.AddHttpClient<IStemSeparator, HttpStemSeparator>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["StemWorker:BaseUrl"]!);
    client.Timeout = TimeSpan.FromSeconds(90);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseSession();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<LobbyHub>("/hubs/lobby");

app.MapGet("/api/stems/{jobId}/{stem}", (string jobId, string stem, StemStore store) =>
    store.TryGetStem(jobId, stem, out var bytes)
        ? Results.File(bytes, "audio/mpeg")
        : Results.NotFound());

app.Run();
