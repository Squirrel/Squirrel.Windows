namespace NuGet
{
    public class NullPropertyProvider : IPropertyProvider
    {
        private static readonly NullPropertyProvider _instance = new NullPropertyProvider();
        private NullPropertyProvider()
        {
        }

        public static NullPropertyProvider Instance
        {
            get
            {
                return _instance;
            }
        }

        public dynamic GetPropertyValue(string propertyName)
        {
            return null;
        }
    }
}
