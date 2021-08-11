using System;
using System.Collections.ObjectModel;

namespace NuGet
{
    public class NetPortableProfileCollection : KeyedCollection<string, NetPortableProfile>
    {
        public NetPortableProfileCollection()
            : base(StringComparer.OrdinalIgnoreCase)
        {
        }

        protected override string GetKeyForItem(NetPortableProfile item)
        {
            return item.Name;
        }
    }
}