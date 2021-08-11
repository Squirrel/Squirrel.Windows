using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NuGet
{
    public interface IShimWebRequest
    {
        HttpWebRequest Request { get; }
    }
}
