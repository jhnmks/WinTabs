using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using WinMan;

namespace wintabs.Utilities
{
    internal static class TransitionTargetGroup
    {
        private static async ValueTask RunOnThreadPool(Action action)
        {
            await await Task.Run(() =>
            {
                try
                {
                    action();
                    return ValueTask.CompletedTask;
                }
                catch (Exception e)
                {
                    return ValueTask.FromException(e);
                }
            });
        }

        public static Task PerformTransitionAsync(List<TransitionTarget> targets)
        {
            return Task.WhenAll(targets.Select(target =>
            {
                return RunOnThreadPool(() =>
                {
                    try
                    {
                        target.Window.SetPosition(target.ComputedPosition);
                    }
                    catch (InvalidWindowReferenceException)
                    {
                    }
                    catch (InvalidOperationException) when (target.Window.State != WindowState.Restored)
                    {
                    }
                }).AsTask();
            }).ToArray());
        }
    }
}
