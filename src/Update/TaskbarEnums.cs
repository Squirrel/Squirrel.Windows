//Copyright (c) Microsoft Corporation.  All rights reserved.

namespace Microsoft.WindowsAPICodePack.Taskbar
{

    internal enum TaskbarProxyWindowType
    {
        TabbedThumbnail,
        ThumbnailToolbar,
    }

    /// <summary>
    /// Known category to display
    /// </summary>
    public enum JumpListKnownCategoryType
    {
        /// <summary>
        /// Don't display either known category. You must have at least one
        /// user task or custom category link in order to not see the
        /// default 'Recent' known category
        /// </summary>
        Neither = 0,

        /// <summary>
        /// Display the 'Recent' known category
        /// </summary>
        Recent,

        /// <summary>
        /// Display the 'Frequent' known category
        /// </summary>
        Frequent,
    }

    /// <summary>
    /// Represents the thumbnail progress bar state.
    /// </summary>
    public enum TaskbarProgressBarState
    {
        /// <summary>
        /// No progress is displayed.
        /// </summary>
        NoProgress = 0,

        /// <summary>
        /// The progress is indeterminate (marquee).
        /// </summary>
        Indeterminate = 0x1,

        /// <summary>
        /// Normal progress is displayed.
        /// </summary>
        Normal = 0x2,

        /// <summary>
        /// An error occurred (red).
        /// </summary>
        Error = 0x4,

        /// <summary>
        /// The operation is paused (yellow).
        /// </summary>
        Paused = 0x8
    }
}
