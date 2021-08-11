using System;

namespace NuGet
{
    public interface IPackageOperationEventListener
    {
        void OnBeforeAddPackageReference(IProjectManager projectManager);
        void OnAfterAddPackageReference(IProjectManager projectManager);
        void OnAddPackageReferenceError(IProjectManager projectManager, Exception exception);
    }
}