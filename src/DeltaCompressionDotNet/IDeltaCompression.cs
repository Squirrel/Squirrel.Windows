namespace DeltaCompressionDotNet
{
    public interface IDeltaCompression
    {
        void CreateDelta(string oldFilePath, string newFilePath, string deltaFilePath);

        void ApplyDelta(string deltaFilePath, string oldFilePath, string newFilePath);
    }

    // TODO IDeltaCompressionWithHandles (exclusively PatchAPI)
    // TODO IDeltaCompressionWithBuffers (PatchAPI and MSDelta)
}