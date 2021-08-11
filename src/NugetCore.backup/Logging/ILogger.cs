namespace NuGet
{
    public interface ILogger : IFileConflictResolver
    {
        void Log(MessageLevel level, string message, params object[] args);       
    }
}