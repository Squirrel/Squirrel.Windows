// If updating HostModel, mark the ResourceUpdater.cs class as partial so these functions can get mixed in

using System;
using System.Runtime.Versioning;

namespace Microsoft.NET.HostModel
{
    [SupportedOSPlatform("windows")]
    public partial class ResourceUpdater
    {
        public ResourceUpdater(string peFile, bool bDeleteExistingResources)
        {
            hUpdate = Kernel32.BeginUpdateResource(peFile, bDeleteExistingResources);
            if (hUpdate.IsInvalid) {
                ThrowExceptionForLastWin32Error();
            }
        }

        public ResourceUpdater AddResource(byte[] data, string lpType, IntPtr lpName, ushort langId)
        {
            if (hUpdate.IsInvalid) {
                ThrowExceptionForInvalidUpdate();
            }

            if (!IsIntResource(lpName)) {
                throw new ArgumentException("AddResource can only be used with integer resource names");
            }

            if (!Kernel32.UpdateResource(hUpdate, lpType, lpName, langId, data, (uint) data.Length)) {
                ThrowExceptionForLastWin32Error();
            }

            return this;
        }

        //public ResourceUpdater ClearResource(string lpType, IntPtr lpName, ushort langId)
        //{
        //    if (hUpdate.IsInvalid) {
        //        ThrowExceptionForInvalidUpdate();
        //    }

        //    if (!IsIntResource(lpName)) {
        //        throw new ArgumentException("AddResource can only be used with integer resource names");
        //    }

        //    if (!Kernel32.UpdateResource(hUpdate, lpType, lpName, langId, null, 0)) {
        //        ThrowExceptionForLastWin32Error();
        //    }

        //    return this;
        //}
    }
}
