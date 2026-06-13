using ImageGenerator.Services.ImageGenerator.Models;

namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Modules.Interfaces;


public interface IChatGptErrorAnalyzerModule
{
    GenerationResult? AnalyzePageBody(string pageBody);
    GenerationResult? AnalyzeAssistantMessage(string message, TimeSpan elapsed);
}
