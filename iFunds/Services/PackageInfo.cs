using System;
using System.Runtime.InteropServices;
using System.Text;

namespace iFunds.Services;

/// <summary>判断当前是否以 MSIX 打包方式运行。</summary>
public static class PackageInfo
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, StringBuilder? fullName);

    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    private static bool? _isPackaged;

    public static bool IsPackaged
    {
        get
        {
            if (_isPackaged is not null) return _isPackaged.Value;
            try
            {
                int len = 0;
                int rc = GetCurrentPackageFullName(ref len, null);
                _isPackaged = rc != APPMODEL_ERROR_NO_PACKAGE;
            }
            catch
            {
                _isPackaged = false;
            }
            return _isPackaged.Value;
        }
    }
}
