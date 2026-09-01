using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FeatureDeck.Services
{
    public static class NtStatus
    {
        [DllImport("ntdll.dll")]
        private static extern int RtlNtStatusToDosError(int status);

        private const int ErrorMrMidNotFound = 0x13D;

        public static string Describe(int ntStatus, bool noTranslate = false)
        {
            int hResult = 0;
            if (!noTranslate)
                hResult = RtlNtStatusToDosError(ntStatus);
            if (noTranslate || hResult == ErrorMrMidNotFound)
                hResult = ntStatus;

            string text;
            try
            {
                text = new Win32Exception(hResult).Message;
            }
            catch
            {
                text = AppResources.Get("UnknownError");
            }
            return $"{text} (0x{(uint)ntStatus:X8})";
        }
    }
}
