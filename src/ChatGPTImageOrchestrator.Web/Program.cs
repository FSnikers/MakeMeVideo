using System;
using System.Threading.Channels;
using ChatGPTImageOrchestrator.Application.Common.Interfaces;
using ChatGPTImageOrchestrator.Application.UseCases;
using ChatGPTImageOrchestrator.Infrastructure;
using ChatGPTImageOrchestrator.Web.BackgroundServices;
using ChatGPTImageOrchestrator.Web.Middlewares;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration.GetConnectionString("Default") ?? @"Data Source=C:\Users\uvev1\RiderProjects\MakeMeVideo\src\ChatGPTImageOrchestrator.Web\bin\Debug\net8.0\app.db");
builder.Services.AddScoped<CreateProjectHandler>();
builder.Services.AddScoped<GetProjectStatusHandler>();

builder.Services.AddSingleton(Channel.CreateUnbounded<Guid>());
builder.Services.AddSingleton<IBackgroundTaskQueue, InMemoryBackgroundTaskQueue>();
builder.Services.AddHostedService<ImageGenerationWorker>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var app = builder.Build();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();