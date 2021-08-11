using System.Globalization;

namespace NuGet
{
    public interface ICultureAwareRepository
    {
        CultureInfo Culture { get; }
    }
}
