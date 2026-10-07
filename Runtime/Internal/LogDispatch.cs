using System;
using UnityEngine;

namespace Unimetry.Internal
{
    internal static class LogDispatch
    {
        [ThreadStatic]
        private static int depth;

        public static bool IsDispatching => depth > 0;

        public static void Emit(UnimetryLogWriter writer, in UnimetryLogEntry entry)
        {
            if (writer == null)
            {
                return;
            }

            Exception error = null;
            depth++;
            try
            {
                writer(in entry);
            }
            catch (Exception exception)
            {
                error = exception;
            }
            finally
            {
                depth--;
            }

            if (error != null)
            {
                Debug.LogWarning("Unimetry log writer failed: " + error.Message);
            }
        }
    }
}
