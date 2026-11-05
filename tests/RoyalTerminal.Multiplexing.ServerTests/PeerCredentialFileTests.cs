using RoyalTerminal.Multiplexing.Server;
using Xunit;
namespace RoyalTerminal.Multiplexing.ServerTests;
public sealed class PeerCredentialFileTests
{
    [Theory]
    [InlineData("",true)][InlineData("\n",true)][InlineData("\r\n",true)]
    [InlineData("\n\n",false)][InlineData("\r\n\nNOT-PART-OF-TOKEN",false)][InlineData("\rX",false)]
    public async Task RequiresTheEntireFileToContainOneBoundedCredential(string suffix,bool valid)
    {
        var file=Path.GetTempFileName();string secret=new('x',512);
        try
        {
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(file,UnixFileMode.UserRead|UnixFileMode.UserWrite);
            await File.WriteAllTextAsync(file,secret+suffix);
            if(valid)Assert.Equal(secret,await WorkspacePeerRouter.ReadTokenAsync(file,CancellationToken.None));
            else {var err=await Assert.ThrowsAsync<ApiException>(()=>WorkspacePeerRouter.ReadTokenAsync(file,CancellationToken.None));Assert.Equal("peer_credential_unavailable",err.Code);Assert.DoesNotContain(secret,err.Message);}
        }
        finally{File.Delete(file);}
    }
}
