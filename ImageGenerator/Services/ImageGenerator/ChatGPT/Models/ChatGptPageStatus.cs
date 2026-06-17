namespace ImageGenerator.Services.ImageGenerator.ChatGPT.Models;

public enum ChatGptPageStatus
{
    Unknown,
    LoginPage,
    UnAuthChatPage,
    ChatPage,
    SessionExpired,
    CloudflareChallenge,
    LimitReached,
    ProhibitedContent,
    AccountChooser,
    GmailLogin,
}
