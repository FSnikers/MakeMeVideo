using MakeMeVideo.Api.Data;
using MakeMeVideo.Api.Infrastructure;
using MakeMeVideo.Api.Options;
using MakeMeVideo.Api.Services;
using MakeMeVideo.Api.Workers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PlaywrightOptions>(builder.Configuration.GetSection(PlaywrightOptions.SectionName));
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=makemevideo.db"));

builder.Services.AddScoped<IGenerationOrchestrator, GenerationOrchestrator>();
builder.Services.AddScoped<IChatGptAutomationClient, PlaywrightChatGptAutomationClient>();
builder.Services.AddHostedService<GenerationWorker>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.MapControllers();
app.Run();
