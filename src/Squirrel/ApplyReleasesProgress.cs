namespace Squirrel
{
    using System;

    internal class ApplyReleasesProgress : Progress<int>
    {
        private readonly int _releasesToApply;
        private int _appliedReleases;
        private int _currentReleaseProgress;

        public ApplyReleasesProgress(int releasesToApply, Action<int> handler)
            : base(handler)
        {
            _releasesToApply = releasesToApply;
        }

        public void ReportReleaseProgress(int progressOfCurrentRelease)
        {
            _currentReleaseProgress = progressOfCurrentRelease;

            CalculateProgress();
        }

        public void FinishRelease()
        {
            _appliedReleases++;
            _currentReleaseProgress = 0;

            CalculateProgress();
        }

        private void CalculateProgress()
        {
            // Per release progress
            var perReleaseProgressRange = 100 / _releasesToApply;

            var appliedReleases = Math.Min(_appliedReleases, _releasesToApply);
            var basePercentage = appliedReleases * perReleaseProgressRange;

            var currentReleasePercentage = (perReleaseProgressRange / 100d) * _currentReleaseProgress;

            var percentage = basePercentage + currentReleasePercentage;
            OnReport((int)percentage);
        }
    }
}
