// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace RoyalTerminal.Multiplexing.Cli;

internal static class SkillFiles
{
    private static readonly UTF8Encoding Utf8=new(false,true);
    internal static string? Read(string path)
    {
        RefuseLinks(path);
        try { using FileStream s=new(path,FileMode.Open,FileAccess.Read,FileShare.Read); if(s.Length>1<<20)throw new IOException("Oversized configuration: "+path);
            using var r=new StreamReader(s,Utf8,true);return r.ReadToEnd(); }
        catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}
    }
    internal static void Change(string path,string? observed,string? updated)
    {
        RefuseLinks(path);
        if(updated is null&&observed is null)return;
        string dir=Path.GetDirectoryName(path)!;Directory.CreateDirectory(dir);
        // Coordinate installers per target without locking agent-owned configuration files during inspection.
        string lockPath=path+".royalterminal-install.lock";
        using FileStream lease=new(lockPath,new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None,Options=FileOptions.DeleteOnClose});
        if(Read(path)!=observed)throw new IOException("Concurrent edit detected: "+path);
        if(updated is null){File.Delete(path);return;}
        string temp=Path.Combine(dir,".royalterminal-"+Guid.NewGuid().ToString("N"));
        try
        {
            var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None};
            if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
            using(var s=new FileStream(temp,options)){s.Write(Utf8.GetBytes(updated));s.Flush(true);}
            if(Read(path)!=observed)throw new IOException("Concurrent edit detected: "+path);
            File.Move(temp,path,overwrite:observed is not null);
        }
        finally {File.Delete(temp);}
    }
    private static void RefuseLinks(string path)
    {
        var file=new FileInfo(path);
        if(file.LinkTarget is not null)throw new IOException("Refusing linked configuration: "+path);
        for(var d=file.Directory;d is not null;d=d.Parent)
            if(d.LinkTarget is not null)throw new IOException("Refusing linked config directory: "+d.FullName);
    }
}
