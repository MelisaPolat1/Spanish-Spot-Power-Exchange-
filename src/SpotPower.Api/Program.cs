using Microsoft.EntityFrameworkCore;
using Quartz;
using SpotPower.Core.Repositories;
using SpotPower.Infrastructure.Configuration;
using SpotPower.Infrastructure.ExternalServices;
using SpotPower.Infrastructure.Jobs;
using SpotPower.Infrastructure.Parsing;
using SpotPower.Infrastructure.Persistence;
using SpotPower.Infrastructure.Repositories;
using SpotPower.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// --- Persistence -----------------------------------------------------------
builder.Services.AddDbContext<SpotPowerDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("SpotPowerDb")));

builder.Services.AddScoped<IDayAheadPriceRepository, DayAheadPriceRepository>();

// --- OMIE ingestion ----------------------------------------------------------
builder.Services.Configure<OmieOptions>(builder.Configuration.GetSection(OmieOptions.SectionName));
builder.Services.AddHttpClient<OmieDayAheadClient>();
builder.Services.AddSingleton<MarginalPdbcParser>();

builder.Services.AddQuartz(q =>
{
    var jobKey = new JobKey(nameof(ImportDayAheadPricesJob));
    q.AddJob<ImportDayAheadPricesJob>(opts => opts.WithIdentity(jobKey));

    // Pinned to Europe/Madrid explicitly, rather than relying on the host
    // machine/container's local timezone. This means the schedule always
    // means "13:05 Madrid time" — correct through DST transitions — whether
    // this runs on your Windows dev machine, a UTC-configured container, or
    // anything else.
    var madridTz = SpotPower.Core.TimeZones.MadridTimeZone.Instance;

    // Recurring daily schedule.
    q.AddTrigger(t => t
        .ForJob(jobKey)
        .WithIdentity($"{nameof(ImportDayAheadPricesJob)}-daily-trigger")
        .WithCronSchedule(
            builder.Configuration[$"{OmieOptions.SectionName}:CronSchedule"] ?? "0 5 13 * * ?",
            cronBuilder => cronBuilder.InTimeZone(madridTz)));

    // Separate one-shot trigger: fires once, immediately, on every app startup.
    // This is what actually gives you "continue operating after restarts" in
    // practice — a SimpleTrigger's StartNow() genuinely means "fire now",
    // unlike StartNow() on a CronTrigger (which only marks the trigger's
    // earliest-possible-fire boundary, not an immediate execution).
    q.AddTrigger(t => t
        .ForJob(jobKey)
        .WithIdentity($"{nameof(ImportDayAheadPricesJob)}-startup-trigger")
        .StartNow());
});

builder.Services.AddQuartzHostedService(opts => opts.WaitForJobsToComplete = true);

// --- API / OpenAPI -----------------------------------------------------------
builder.Services.AddOpenApi();

var app = builder.Build();

// Apply any pending EF Core migrations on startup. This is what lets the app
// "continue operating after restarts" without a manual `dotnet ef database
// update` step in whatever environment it ends up running in.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SpotPowerDbContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapDayAheadPriceEndpoints();
app.Run();