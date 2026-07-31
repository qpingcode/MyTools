using System.Diagnostics;

namespace MyTools.Plugins;

public class AdminExecute : Execute
{
    public override string Name => "Admin Execute";
    public override string Description => "Execute a program or script with administrator privileges";
    protected override async Task ExecuteCoreAsync(string filePath, string args, bool runAsAdmin)
    {
        await base.ExecuteCoreAsync(filePath, args, true).ConfigureAwait(false);
    }
} 