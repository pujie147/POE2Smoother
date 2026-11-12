namespace PsCore;

internal static class LocalDrives
{
    /// <summary>
    /// Fixed local drives, skipping drives that are not ready (empty card readers, disconnected
    /// network shares) — querying those throws.
    /// </summary>
    public static IEnumerable<string> FixedRoots()
    {
        if (!OperatingSystem.IsWindows())
            yield break;

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            yield break;
        }

        foreach (var drive in drives)
        {
            string? root = null;
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                    root = drive.RootDirectory.FullName;
            }
            catch
            {
                // the drive can disappear between enumeration and query
            }

            if (root != null)
                yield return root;
        }
    }
}