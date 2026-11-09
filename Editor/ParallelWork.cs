using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // Runs thread-safe work (external tools, file IO; no Unity API) on every core while the main thread
    // keeps a cancelable progress bar alive.
    public static class ParallelWork
    {
        const int PROGRESS_POLL_MS = 50;

        // Returns false when the user canceled; items that never started are skipped.
        public static bool Run<T>(IList<T> items, Action<T> action, string progressTitle, string progressLabel)
        {
            if (items.Count == 0)
                return true;

            var finished = 0;
            var cancel = new CancellationTokenSource();
            var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancel.Token };
            var task = Task.Run(() =>
            {
                try
                {
                    Parallel.ForEach(items, options, item =>
                    {
                        action(item);
                        Interlocked.Increment(ref finished);
                    });
                }
                catch (OperationCanceledException)
                {
                    // Canceled from the progress bar.
                }
            });

            while (!task.Wait(PROGRESS_POLL_MS))
            {
                var done = Volatile.Read(ref finished);
                if (progressTitle != null && EditorUtility.DisplayCancelableProgressBar(progressTitle,
                        progressLabel + ": " + done + " / " + items.Count, (float)done / items.Count))
                    cancel.Cancel();
            }
            // Surface exceptions thrown by the work itself.
            task.Wait();
            return !cancel.IsCancellationRequested;
        }
    }
}
