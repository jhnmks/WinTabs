using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

using wintabs.DllImports;

using static wintabs.DllImports.PInvoke;

namespace wintabs.Utilities
{
    public class SystemParameters
    {
        public static SystemParameters Instance { get; } = new();

        public bool WindowArranging
        {
            get
            {
                unsafe
                {
                    uint value = 0;
                    if (!SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETWINARRANGING, 0, &value, 0))
                        throw new Win32Exception("Failed to read window arranging setting.");
                    return value != 0;
                }
            }
            set
            {
                unsafe
                {
                    uint param = value ? 1u : 0u;
                    if (!SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETWINARRANGING, param, null, SystemParametersInfo_fWinIni.SPIF_SENDCHANGE))
                        throw new Win32Exception("Failed to set window arranging enabled state.");
                }
            }
        }
    }
}
