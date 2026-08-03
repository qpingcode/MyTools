namespace MyTools.Plugins;

public class AdminExecute : Execute
{
    public override string Name => ActionText.Get("Action.AdminExecute.Name", "Admin Execute");
    public override string Description => ActionText.Get(
        "Action.AdminExecute.Description", "Execute a program or script with administrator privileges");
    protected override async Task ExecuteCoreAsync(string filePath, string args, bool runAsAdmin)
    {
        await base.ExecuteCoreAsync(filePath, args, true).ConfigureAwait(false);
    }
} 