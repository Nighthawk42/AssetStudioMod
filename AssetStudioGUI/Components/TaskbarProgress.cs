using System;
using System.Runtime.InteropServices;

namespace AssetStudioGUI
{
    internal enum TaskbarProgressBarState
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8
    }

    // Minimal ITaskbarList3 wrapper (replaces the unmaintained WindowsAPICodePack)
    internal sealed class TaskbarProgress
    {
        private readonly ITaskbarList3 taskbarList;
        private readonly Func<IntPtr> getHandle;

        public TaskbarProgress(Func<IntPtr> getHandle)
        {
            this.getHandle = getHandle;
            try
            {
                taskbarList = (ITaskbarList3)new TaskbarInstance();
                taskbarList.HrInit();
            }
            catch
            {
                taskbarList = null;
            }
        }

        public void SetProgressValue(int value, int max)
        {
            var hwnd = getHandle();
            if (taskbarList == null || hwnd == IntPtr.Zero)
                return;
            taskbarList.SetProgressValue(hwnd, (ulong)Math.Max(value, 0), (ulong)Math.Max(max, 0));
        }

        public void SetProgressState(TaskbarProgressBarState state)
        {
            var hwnd = getHandle();
            if (taskbarList == null || hwnd == IntPtr.Zero)
                return;
            taskbarList.SetProgressState(hwnd, state);
        }

        [ComImport]
        [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
        {
            // ITaskbarList
            void HrInit();
            void AddTab(IntPtr hwnd);
            void DeleteTab(IntPtr hwnd);
            void ActivateTab(IntPtr hwnd);
            void SetActiveAlt(IntPtr hwnd);
            // ITaskbarList2
            void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);
            // ITaskbarList3
            void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
            void SetProgressState(IntPtr hwnd, TaskbarProgressBarState state);
        }

        [ComImport]
        [Guid("56fdf344-fd6d-11d0-958a-006097c9a090")]
        [ClassInterface(ClassInterfaceType.None)]
        private class TaskbarInstance
        {
        }
    }
}
