using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cecilifier.Core.Misc;

namespace Cecilifier.Web;

public class AssemblyReferenceCacheHandler
{
    internal static bool HashEnoughStorageSpace(string cachePath)
    {
        var drives = DriveInfo.GetDrives();
        var targetDrive = drives
            .Where(candidate => cachePath.StartsWith(candidate.RootDirectory.FullName))
            .MaxBy(candidate => candidate.RootDirectory.FullName, StringLengthComparer.Instance);

        // if we have less than 15% of free space in the target drive, do a cache eviction
        var minimumFreeSpaceInBytes = targetDrive.TotalSize * 0.15;
        var diffInBytesBetweenGoalAndActual = minimumFreeSpaceInBytes - targetDrive.AvailableFreeSpace;
        if (diffInBytesBetweenGoalAndActual > 0)
        {
            // remove first the least accessed big files
            var candidatesToEvict = Directory.GetFiles(cachePath, "*.dll", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastAccessTime)
                .ThenByDescending(f => f.Length);

            foreach (var fileInfo in candidatesToEvict)
            {
                diffInBytesBetweenGoalAndActual -= fileInfo.Length;
                fileInfo.Delete();

                if (diffInBytesBetweenGoalAndActual <= 0)
                    break;
            }
        }

        return diffInBytesBetweenGoalAndActual <= 0;
    }

    public static async Task StoreAssemblyBytesAsync(string assemblyPath, byte[] bytes, int length)
    {
        var targetFolder = Path.GetDirectoryName(assemblyPath);
        if (!Directory.Exists(targetFolder))
            Directory.CreateDirectory(targetFolder!);

        await using var assemblyFile = new FileStream(assemblyPath, FileMode.OpenOrCreate, FileAccess.Write);
        await assemblyFile.WriteAsync(new ReadOnlyMemory<byte>(bytes, 0, length));
    }

    public static (List<string> Success, List<string> NotFound) RetrieveAssemblyReferences(string cachePath, AssemblyReference[] assemblyReferences)
    {
        var ret = new List<string>();
        var notFound = new List<string>();
        foreach (var assemblyReference in assemblyReferences)
        {
            var assemblyPath = Path.Combine(cachePath, assemblyReference.AssemblyHash, assemblyReference.AssemblyName);
            if (!File.Exists(assemblyPath))
            {
                notFound.Add(assemblyReference.AssemblyHash);
                continue;
            }

            ret.Add(assemblyPath);
        }

        return (ret, notFound);
    }
}
