namespace NuGet
{
    public interface IPropertyProvider
    {
        dynamic GetPropertyValue(string propertyName);
    }
}
