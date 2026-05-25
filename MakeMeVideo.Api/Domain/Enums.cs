namespace MakeMeVideo.Api.Domain;

public enum GeneratorAccountStatus { Active, LimitReached, Banned, PendingLogin }
public enum GenerationProjectStatus { Queued, Processing, Completed, Failed }
public enum ImageTaskStatus { Queued, Processing, Completed, Failed, RetryPending }
