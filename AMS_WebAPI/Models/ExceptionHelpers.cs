using System;
using System.Diagnostics;

namespace AMS_WebAPI.Models
{
    static class ExceptionHelpers
    {
        public static string SrcInfo(this Exception ex)
        {
            var st = new StackTrace(ex, true);
            if (st != null)
            {
                var frame = st.GetFrame(st.FrameCount - 1);
                if (frame != null)
                {
                    string fileName = frame.GetFileName();
                    return $"{fileName.Substring(fileName.LastIndexOf('\\') + 1)}-L{frame.GetFileLineNumber()}";
                }
            }

            return "";
        }

        public static int LineNumber(this Exception ex)
        {
            var st = new StackTrace(ex, true);
            if (st != null)
            {
                var frame = st.GetFrame(st.FrameCount - 1);
                if (frame != null)
                {
                    return frame.GetFileLineNumber();
                }
            }

            return -1;
        }
    }
}
