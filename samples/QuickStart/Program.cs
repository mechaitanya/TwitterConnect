// QuickStart: look up an X user with the XConnect SDK. See README.md in this folder.
// Usage: dotnet run --project samples/QuickStart -- [<username> | --id <user-id> | --me]
using QuickStart;

QuickStartConfig config;
try
{
    config = QuickStartConfig.Load(Environment.GetEnvironmentVariable, QuickStartConfig.ReadSettingFile);
}
catch (QuickStartConfigException ex)
{
    Console.WriteLine(ex.Message);
    return 1;
}

return await QuickStartApp.RunAsync(args, config, handler: null, Console.Out);
