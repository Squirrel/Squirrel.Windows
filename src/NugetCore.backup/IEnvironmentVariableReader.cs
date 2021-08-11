namespace NuGet
{
    public interface IEnvironmentVariableReader
    {
        string GetEnvironmentVariable(string variable);
    }
}
