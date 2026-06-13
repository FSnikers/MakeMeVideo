using ImageGenerator.Models;

namespace ImageGenerator.Services.ChatGPT.Interfaces;


public interface IChatGptErrorAnalyzer
{
    GenerationResult? AnalyzePageBody(string pageBody);
    GenerationResult? AnalyzeAssistantMessage(string message, TimeSpan elapsed);
}
