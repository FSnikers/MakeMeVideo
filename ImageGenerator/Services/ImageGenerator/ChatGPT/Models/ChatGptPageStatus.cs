namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Models;

public enum ChatGptPageStatus
{
    Unknown,
    LoginPage,
    ChatPage,
    SessionExpired,
    CloudflareChallenge,
    LimitReached,
    ProhibitedContent,
}
